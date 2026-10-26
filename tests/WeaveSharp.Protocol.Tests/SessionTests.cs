using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace WeaveSharp.Protocol.Tests;

public sealed class SessionTests
{
    internal static readonly SnapshotIdentity Snapshot = new("host-a", "workspace://sample/Counter.cs", "v8", "s8");
    internal static readonly RequestContext Context = new("workspace-a", "app", "ctx-3", Snapshot);
    private static readonly TimeSpan Start = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    internal sealed class Lease : IDisposable
    {
        public int Releases { get; private set; }
        public void Dispose() => Releases++;
    }

    internal sealed class RecordingTransport : IProtocolTransport
    {
        public List<Envelope<JsonElement>> Sent { get; } = [];
        public List<MutationReconciliation> Inspections { get; } = [];
        public bool ThrowAfterSending { get; set; }
        public void Send(Envelope<JsonElement> message)
        {
            Sent.Add(message);
            if (ThrowAfterSending)
                throw new InvalidOperationException("The transport lost its acknowledgement.");
        }
        public void InspectHostDocuments(MutationReconciliation reconciliation) => Inspections.Add(reconciliation);
    }

    private static (ProtocolSession Session, RecordingTransport Transport) Open()
    {
        var transport = new RecordingTransport();
        var negotiated = VersionNegotiator.Negotiate([new(0, 0)], [new(0, 0)], new HashSet<string>(),
            new Dictionary<string, ProtocolExtension>());
        return (new(negotiated, Context, transport, "session-a", "view-a"), transport);
    }

    internal static Envelope<RequestGraphPayload> Query(string id = "q1") => new(new(0, 0), "request-graph", id,
        MessageKind.Query, new(Context, new(Snapshot, "utf16-offset", 0, 17), "full", true),
        new Dictionary<string, ProtocolExtension>());

    private static Envelope<EditResultPayload> Mutation(string id = "m1") => new(new(0, 0), "edit-result", id,
        MessageKind.Mutation, new(Context, "commit", "requested", "Insert a return statement.",
            [new(new(Snapshot, "utf16-offset", 0, 0), "return 12;")], [Snapshot], [], []),
        new Dictionary<string, ProtocolExtension>());

    private static Lease BeginQuery(ProtocolSession session, string id = "q1", string slot = "body")
    {
        var lease = new Lease();
        session.Begin(Query(id), Context, "publish-graph", lease, Start, Timeout, slot);
        return lease;
    }

    private static Lease BeginMutation(ProtocolSession session, IReadOnlyList<SnapshotIdentity>? documents = null)
    {
        var lease = new Lease();
        session.Begin(Mutation(), Context, "edit-result", lease, Start, Timeout, mutationDocuments: documents ?? [Snapshot]);
        return lease;
    }

    [Fact]
    public void CompletionCorrelatesIdentityAndFamilyAndReleasesOnce()
    {
        var (session, _) = Open();
        using (session)
        {
            var lease = BeginQuery(session);
            Assert.Equal(ResponseDisposition.UnknownRequest, session.Receive("other", "publish-graph", Context));
            Assert.Equal(ResponseDisposition.WrongFamily, session.Receive("q1", "edit-result", Context));
            Assert.Equal(0, lease.Releases);
            Assert.Equal(ResponseDisposition.Accepted, session.Receive("q1", "publish-graph", Context));
            Assert.Equal(ResponseDisposition.AlreadyCompleted, session.Receive("q1", "publish-graph", Context));
            Assert.False(session.Cancel("q1"));
            Assert.Equal(1, lease.Releases);
            Assert.Equal(0, session.ActiveResourceCount);
        }
    }

    [Fact]
    public void CancellationDropsResourcesAndIgnoresLaterReplies()
    {
        var (session, _) = Open();
        using (session)
        {
            var lease = BeginQuery(session);
            Assert.True(session.CancelOrDispose(new("cancel", "request", "q1")));
            Assert.False(session.Cancel("q1"));
            Assert.Equal(1, lease.Releases);
            Assert.Equal(0, session.ActiveResourceCount);
            Assert.Equal(ResponseDisposition.Cancelled, session.Receive("q1", "publish-graph", Context));
            Assert.Throws<InvalidOperationException>(() => session.Begin(Query(), Context, "publish-graph", new Lease(), Start, Timeout));
        }
    }

    [Theory]
    [InlineData("view", "view-a")]
    [InlineData("context", "ctx-3")]
    [InlineData("session", "session-a")]
    [InlineData("workspace", "workspace-a")]
    public void DisposalReleasesEverySlotAndPreventsRevival(string kind, string id)
    {
        var (session, _) = Open();
        var first = BeginQuery(session, "q1", "body-a");
        var second = BeginQuery(session, "q2", "body-b");
        Assert.False(session.CancelOrDispose(new("dispose", kind, "different")));
        Assert.Equal(2, session.ActiveResourceCount);
        Assert.True(session.CancelOrDispose(new("dispose", kind, id)));
        session.Dispose();
        Assert.Equal(1, first.Releases);
        Assert.Equal(1, second.Releases);
        Assert.Equal(0, session.ActiveResourceCount);
        Assert.Equal(ResponseDisposition.Disposed, session.Receive("q1", "publish-graph", Context));
        Assert.Equal(ResponseDisposition.Disposed, session.Receive("q2", "publish-graph", Context));
        Assert.Throws<ObjectDisposedException>(() => BeginQuery(session, "q3"));
    }

    public static IEnumerable<object[]> OtherContexts()
    {
        yield return [Context with { WorkspaceId = "other-workspace" }];
        yield return [Context with { ProjectId = "other-project" }];
        yield return [Context with { CompilationContextId = "other-context" }];
        yield return [Context with { Snapshot = Snapshot with { HostId = "other-host" } }];
        yield return [Context with { Snapshot = Snapshot with { DocumentUri = "workspace://sample/Other.cs" } }];
        yield return [Context with { Snapshot = Snapshot with { HostVersion = "v9" } }];
        yield return [Context with { Snapshot = Snapshot with { SnapshotId = "s9" } }];
    }

    [Theory]
    [MemberData(nameof(OtherContexts))]
    public void MismatchedResponsesCannotConsumeTheActiveRequest(RequestContext other)
    {
        var (session, _) = Open();
        using (session)
        {
            var lease = BeginQuery(session);
            Assert.Equal(ResponseDisposition.StaleContext, session.Receive("q1", "publish-graph", other));
            Assert.Equal(0, lease.Releases);
            Assert.Equal(ResponseDisposition.Accepted, session.Receive("q1", "publish-graph", Context));
        }
    }

    [Theory]
    [MemberData(nameof(OtherContexts))]
    public void ChangingContextRetiresOldWorkEvenIfItsReplyMatchesTheOriginal(RequestContext other)
    {
        var (session, _) = Open();
        using (session)
        {
            var lease = BeginQuery(session);
            session.SetActiveContext(other);
            Assert.Equal(1, lease.Releases);
            Assert.Equal(ResponseDisposition.Superseded, session.Receive("q1", "publish-graph", Context));
        }
    }

    [Fact]
    public void OlderQueryInTheSameSlotCannotReplaceTheNewerResult()
    {
        var (session, _) = Open();
        using (session)
        {
            var older = BeginQuery(session, "q1");
            var newer = BeginQuery(session, "q2");
            Assert.Equal(1, older.Releases);
            Assert.Equal(ResponseDisposition.Accepted, session.Receive("q2", "publish-graph", Context));
            Assert.Equal(ResponseDisposition.Superseded, session.Receive("q1", "publish-graph", Context));
            Assert.Equal(1, newer.Releases);
        }
    }

    [Fact]
    public void MissingQueryReplyCanBeRetriedWithANewIdentity()
    {
        var (session, transport) = Open();
        using (session)
        {
            var first = BeginQuery(session);
            Assert.Equal(0, session.Expire(Start + Timeout - TimeSpan.FromTicks(1)));
            Assert.Equal(1, session.Expire(Start + Timeout));
            Assert.Equal(0, session.Expire(Start + Timeout));
            Assert.Equal(ResponseDisposition.LostQuery, session.Receive("q1", "publish-graph", Context));
            var second = new Lease();
            session.RetryQuery("q1", "q2", second, Start + Timeout, Timeout);
            Assert.Equal(["q1", "q2"], transport.Sent.Select(m => m.RequestId));
            Assert.True(JsonElement.DeepEquals(transport.Sent[0].Payload, transport.Sent[1].Payload));
            Assert.Equal(1, first.Releases);
            Assert.Equal(ResponseDisposition.Superseded, session.Receive("q1", "publish-graph", Context));
            Assert.Equal(ResponseDisposition.Accepted, session.Receive("q2", "publish-graph", Context));
            Assert.Equal(1, second.Releases);
            Assert.Empty(transport.Inspections);
            Assert.Throws<InvalidOperationException>(() => session.RetryQuery("q1", "q3", new Lease(), Start, Timeout));
        }
    }

    [Fact]
    public void QueryRetryCannotReuseAnIdentityOrCarryAnOldContextForward()
    {
        var (session, transport) = Open();
        using (session)
        {
            BeginQuery(session);
            session.LoseResponse("q1");
            Assert.Throws<InvalidOperationException>(() => session.RetryQuery("q1", "q1", new Lease(), Start, Timeout));
            session.SetActiveContext(Context with { CompilationContextId = "ctx-4" });
            Assert.Throws<InvalidOperationException>(() => session.RetryQuery("q1", "q2", new Lease(), Start, Timeout));
            Assert.Single(transport.Sent);
        }
    }

    [Fact]
    public void NewQueryRetiresTheRetryOptionForOlderWorkInItsSlot()
    {
        var (session, transport) = Open();
        using (session)
        {
            BeginQuery(session);
            session.LoseResponse("q1");
            BeginQuery(session, "q2");
            Assert.Throws<InvalidOperationException>(() => session.RetryQuery("q1", "q3", new Lease(), Start, Timeout));
            Assert.Equal(2, transport.Sent.Count);
            Assert.Equal(ResponseDisposition.Accepted, session.Receive("q2", "publish-graph", Context));
        }
    }

    [Theory]
    [InlineData("applied", ReconciliationOutcome.Applied)]
    [InlineData("unchanged", ReconciliationOutcome.NotApplied)]
    [InlineData("concurrent", ReconciliationOutcome.Diverged)]
    public void MissingMutationReplyUsesHostStateAndNeverResends(string state, ReconciliationOutcome expected)
    {
        var (session, transport) = Open();
        using (session)
        {
            var lease = BeginMutation(session);
            Assert.Equal(1, session.Expire(Start + Timeout));
            Assert.Equal(1, lease.Releases);
            var inspection = Assert.Single(transport.Inspections);
            Assert.Equal("m1", inspection.RequestId);
            Assert.Equal([Snapshot], inspection.Before);
            Assert.Equal(ResponseDisposition.AwaitingReconciliation, session.Receive("m1", "edit-result", Context));
            Assert.Throws<InvalidOperationException>(() => session.RetryQuery("m1", "m2", new Lease(), Start, Timeout));
            var host = state switch
            {
                "applied" => new HostDocumentState(Snapshot with { HostVersion = "v10", SnapshotId = "host-normalized" }, "m1"),
                "unchanged" => new HostDocumentState(Snapshot, null),
                _ => new HostDocumentState(Snapshot with { HostVersion = "v9", SnapshotId = "s9" }, "another-edit")
            };
            Assert.Equal(expected, session.Reconcile(new("m1", true, [host])));
            Assert.Equal(Context, session.ActiveContext);
            Assert.Single(transport.Sent);
            Assert.Equal(0, session.Expire(Start + Timeout + Timeout));
            Assert.Single(transport.Sent);
        }
    }

    [Fact]
    public void ReconciliationWaitsForSettledStateAndAllDocuments()
    {
        var (session, transport) = Open();
        using (session)
        {
            var second = Snapshot with { DocumentUri = "workspace://sample/Helper.cs" };
            BeginMutation(session, [Snapshot, second]);
            session.LoseResponse("m1");
            Assert.Equal(ReconciliationOutcome.Pending, session.Reconcile(new("m1", false, [new(Snapshot, null), new(second, null)])));
            Assert.Equal(ReconciliationOutcome.Pending, session.Reconcile(new("m1", true, [new(Snapshot, null)])));
            Assert.Equal(ReconciliationOutcome.Pending, session.Reconcile(new("m1", true,
                [new(Snapshot with { HostId = "another-host" }, null), new(second, null)])));
            Assert.Equal(ReconciliationOutcome.Pending, session.Reconcile(new("m1", true,
                [new(Snapshot, null), new(Snapshot, null), new(second, null)])));
            Assert.Throws<InvalidOperationException>(() => session.Reconcile(new("another-request", true, [])));
            Assert.Equal(ReconciliationOutcome.Diverged, session.Reconcile(new("m1", true,
                [new(Snapshot with { HostVersion = "v9", SnapshotId = "s9" }, "m1"), new(second, null)])));
            Assert.Single(transport.Sent);
        }
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("dispose")]
    [InlineData("context")]
    public void RetiringAMutationStillRequiresHostInspection(string cause)
    {
        var (session, transport) = Open();
        using (session)
        {
            var lease = BeginMutation(session);
            if (cause == "cancel") session.Cancel("m1");
            if (cause == "dispose") session.Dispose();
            if (cause == "context") session.SetActiveContext(Context with { CompilationContextId = "ctx-4" });
            Assert.Equal(1, lease.Releases);
            Assert.Single(transport.Inspections);
            Assert.NotEqual(ResponseDisposition.Accepted, session.Receive("m1", "edit-result", Context));
            Assert.Equal(ReconciliationOutcome.NotApplied, session.Reconcile(new("m1", true, [new(Snapshot, null)])));
            Assert.Single(transport.Sent);
        }
    }

    [Fact]
    public void ThrowingTransportLeavesAnUncertainMutationForReconciliation()
    {
        var (session, transport) = Open();
        using (session)
        {
            transport.ThrowAfterSending = true;
            Assert.Throws<InvalidOperationException>(() => BeginMutation(session));
            Assert.Single(transport.Sent);
            Assert.Single(transport.Inspections);
            Assert.Equal(0, session.ActiveResourceCount);
            Assert.Throws<InvalidOperationException>(() => session.RetryQuery("m1", "m2", new Lease(), Start, Timeout));
        }
    }

    [Fact]
    public void RequestVersionMustMatchTheAgreement()
    {
        var (session, transport) = Open();
        using (session)
        {
            Assert.Throws<InvalidOperationException>(() => session.Begin(Query() with { Version = new(1, 0) },
                Context, "publish-graph", new Lease(), Start, Timeout));
            Assert.Empty(transport.Sent);
        }
    }
}
