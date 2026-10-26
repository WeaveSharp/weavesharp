using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace WeaveSharp.Protocol;

public enum ResponseDisposition
{
    Accepted, UnknownRequest, WrongFamily, StaleContext, Cancelled, Disposed,
    Superseded, LostQuery, AwaitingReconciliation, AlreadyCompleted
}

public enum ReconciliationOutcome { Pending, Applied, NotApplied, Diverged }
public sealed record MutationReconciliation(string RequestId, IReadOnlyList<SnapshotIdentity> Before);
public sealed record HostDocumentState(SnapshotIdentity Snapshot, string? LastMutationRequestId);
public sealed record HostMutationObservation(string RequestId, bool Settled, IReadOnlyList<HostDocumentState> Documents);

public interface IProtocolTransport
{
    void Send(Envelope<JsonElement> message);
    void InspectHostDocuments(MutationReconciliation reconciliation);
}

// One session owns one view. Callers serialize its calls on their own event loop.
// Only already-decoded contract data belongs here; there is no transport parser.
public sealed class ProtocolSession : IDisposable
{
    private enum State { Pending, Completed, Cancelled, Disposed, Superseded, LostQuery, Reconciling }

    private sealed class Request(Envelope<JsonElement> message, RequestContext context,
        string responseFamily, string slot, IDisposable resources, TimeSpan deadline,
        IReadOnlyList<SnapshotIdentity> documents)
    {
        public Envelope<JsonElement> Message { get; } = message;
        public RequestContext Context { get; } = context;
        public string ResponseFamily { get; } = responseFamily;
        public string Slot { get; } = slot;
        public IDisposable? Resources { get; set; } = resources;
        public TimeSpan Deadline { get; } = deadline;
        public IReadOnlyList<SnapshotIdentity> Documents { get; } = documents;
        public State State { get; set; } = State.Pending;
        public bool ReconciliationRequired { get; set; }
        public ReconciliationOutcome? Outcome { get; set; }
    }

    private readonly Dictionary<string, Request> requests = new(StringComparer.Ordinal);
    private readonly IProtocolTransport transport;
    private readonly ProtocolVersion version;
    private readonly string sessionId;
    private readonly string viewId;
    private bool disposed;

    public ProtocolSession(NegotiationResult negotiation, RequestContext context,
        IProtocolTransport transport, string sessionId, string viewId)
    {
        if (!negotiation.Accepted)
            throw new InvalidOperationException("A version agreement is needed before opening a session.");
        version = negotiation.Version!;
        ActiveContext = context;
        this.transport = transport;
        this.sessionId = sessionId;
        this.viewId = viewId;
    }

    public RequestContext ActiveContext { get; private set; }
    public int ActiveResourceCount => requests.Values.Count(request => request.Resources is not null);

    public void Begin<T>(Envelope<T> message, RequestContext context, string responseFamily,
        IDisposable resources, TimeSpan now, TimeSpan timeout, string slot = "default",
        IReadOnlyList<SnapshotIdentity>? mutationDocuments = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (message.Version != version || context != ActiveContext)
            throw new InvalidOperationException("The request must use the current session version and context.");
        if (message.Kind is not (MessageKind.Query or MessageKind.Mutation))
            throw new ArgumentException("Only requests can be tracked.", nameof(message));
        if (requests.ContainsKey(message.RequestId))
            throw new InvalidOperationException("A request identity cannot be used twice in a session.");
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        var documents = mutationDocuments?.ToArray() ?? [];
        if (message.Kind == MessageKind.Mutation && documents.Length == 0)
            throw new ArgumentException("Mutation tracking needs the host's starting document versions.", nameof(mutationDocuments));

        var wireMessage = new Envelope<JsonElement>(message.Version, message.Family, message.RequestId,
            message.Kind, JsonSerializer.SerializeToElement(message.Payload, ProtocolJson.CreateOptions()),
            new Dictionary<string, ProtocolExtension>(message.Extensions, StringComparer.Ordinal));
        var request = new Request(wireMessage, context, responseFamily, slot, resources, now + timeout, documents);
        if (message.Kind == MessageKind.Query)
        {
            foreach (var previous in requests.Values.Where(r => r.State is State.Pending or State.LostQuery &&
                r.Message.Kind == MessageKind.Query && r.ResponseFamily == responseFamily && r.Slot == slot).ToArray())
                Finish(previous, State.Superseded);
        }
        requests.Add(message.RequestId, request);
        try
        {
            transport.Send(wireMessage);
        }
        catch
        {
            // A throwing transport may already have delivered the request.
            LoseResponse(message.RequestId);
            throw;
        }
    }

    public ResponseDisposition Receive(string requestId, string family, RequestContext context)
    {
        if (!requests.TryGetValue(requestId, out var request))
            return disposed ? ResponseDisposition.Disposed : ResponseDisposition.UnknownRequest;
        if (request.State != State.Pending)
            return request.State switch
            {
                State.Cancelled => ResponseDisposition.Cancelled,
                State.Disposed => ResponseDisposition.Disposed,
                State.Superseded => ResponseDisposition.Superseded,
                State.LostQuery => ResponseDisposition.LostQuery,
                State.Reconciling => ResponseDisposition.AwaitingReconciliation,
                _ => ResponseDisposition.AlreadyCompleted
            };
        if (family != request.ResponseFamily)
            return ResponseDisposition.WrongFamily;
        if (context != request.Context || context != ActiveContext)
            return ResponseDisposition.StaleContext;
        Finish(request, State.Completed);
        return ResponseDisposition.Accepted;
    }

    public void SetActiveContext(RequestContext context)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ActiveContext = context;
        foreach (var request in requests.Values.Where(r => r.State == State.Pending && r.Context != context).ToArray())
        {
            Finish(request, State.Superseded);
            if (request.Message.Kind == MessageKind.Mutation)
                RequireReconciliation(request);
        }
    }

    public bool Cancel(string requestId)
    {
        if (!requests.TryGetValue(requestId, out var request) ||
            request.State is State.Completed or State.Cancelled or State.Disposed or State.Superseded)
            return false;
        Finish(request, State.Cancelled);
        if (request.Message.Kind == MessageKind.Mutation)
            RequireReconciliation(request);
        return true;
    }

    public bool CancelOrDispose(CancelOrDisposePayload message)
    {
        if (message.Operation == "cancel" && message.TargetKind == "request")
            return Cancel(message.TargetId);
        if (message.Operation != "dispose")
            return false;
        bool matches = message.TargetKind switch
        {
            "view" => message.TargetId == viewId,
            "context" => message.TargetId == ActiveContext.CompilationContextId,
            "session" => message.TargetId == sessionId,
            "workspace" => message.TargetId == ActiveContext.WorkspaceId,
            _ => false
        };
        if (matches)
            Dispose();
        return matches;
    }

    public bool LoseResponse(string requestId)
    {
        if (!requests.TryGetValue(requestId, out var request) || request.State != State.Pending)
            return false;
        bool mutation = request.Message.Kind == MessageKind.Mutation;
        Finish(request, mutation ? State.Reconciling : State.LostQuery);
        if (mutation)
            RequireReconciliation(request);
        return true;
    }

    public int Expire(TimeSpan now)
    {
        var expired = requests.Values.Where(r => r.State == State.Pending && r.Deadline <= now).ToArray();
        foreach (var request in expired)
            LoseResponse(request.Message.RequestId);
        return expired.Length;
    }

    public void RetryQuery(string lostRequestId, string newRequestId, IDisposable resources,
        TimeSpan now, TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!requests.TryGetValue(lostRequestId, out var previous) ||
            previous.State != State.LostQuery || previous.Message.Kind != MessageKind.Query)
            throw new InvalidOperationException("Only a query with a missing reply can be retried.");
        Begin(previous.Message with { RequestId = newRequestId }, previous.Context,
            previous.ResponseFamily, resources, now, timeout, previous.Slot);
        previous.State = State.Superseded;
    }

    public ReconciliationOutcome Reconcile(HostMutationObservation observation)
    {
        if (!requests.TryGetValue(observation.RequestId, out var request) || !request.ReconciliationRequired)
            throw new InvalidOperationException("There is no outstanding host inspection for this identity.");
        if (!observation.Settled)
            return ReconciliationOutcome.Pending;
        var states = new List<(SnapshotIdentity Before, HostDocumentState After)>();
        foreach (var before in request.Documents)
        {
            var matching = observation.Documents.Where(d => d.Snapshot.HostId == before.HostId &&
                d.Snapshot.DocumentUri == before.DocumentUri).ToArray();
            if (matching.Length != 1)
                return ReconciliationOutcome.Pending;
            states.Add((before, matching[0]));
        }
        var outcome = states.All(s => s.After.LastMutationRequestId == observation.RequestId &&
                s.After.Snapshot.HostVersion != s.Before.HostVersion &&
                s.After.Snapshot.SnapshotId != s.Before.SnapshotId)
            ? ReconciliationOutcome.Applied
            : states.All(s => s.After.Snapshot == s.Before && s.After.LastMutationRequestId != observation.RequestId)
                ? ReconciliationOutcome.NotApplied : ReconciliationOutcome.Diverged;
        request.ReconciliationRequired = false;
        request.Outcome = outcome;
        if (request.State == State.Reconciling)
            request.State = State.Completed;
        return outcome;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        foreach (var request in requests.Values.Where(r => r.State is State.Pending or State.LostQuery or State.Reconciling).ToArray())
        {
            Finish(request, State.Disposed);
            if (request.Message.Kind == MessageKind.Mutation)
                RequireReconciliation(request);
        }
    }

    private static void Finish(Request request, State state)
    {
        request.State = state;
        var resources = request.Resources;
        request.Resources = null;
        resources?.Dispose();
    }

    private void RequireReconciliation(Request request)
    {
        if (request.ReconciliationRequired || request.Outcome is not null)
            return;
        request.ReconciliationRequired = true;
        transport.InspectHostDocuments(new(request.Message.RequestId, request.Documents));
    }
}
