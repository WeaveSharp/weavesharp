using System;
using System.Collections.Generic;

namespace WeaveSharp.Protocol;

public enum GraphDisposition { Applied, NeedCompleteSnapshot, StaleRevision, StaleContext }
public sealed record CompleteGraphRequest(RequestContext Context, string GraphId);

// Revision bookkeeping for a single view; graph materialization belongs to its caller.
public sealed class GraphReceiver(Action<CompleteGraphRequest> requestComplete)
{
    private readonly Dictionary<(RequestContext Context, string GraphId), long> revisions = [];

    public long? Revision(RequestContext context, string graphId) =>
        revisions.TryGetValue((context, graphId), out var revision) ? revision : null;

    public GraphDisposition Receive(PublishGraphPayload graph, RequestContext activeContext)
    {
        if (graph.Context != activeContext)
            return GraphDisposition.StaleContext;
        var key = (graph.Context, graph.GraphId);
        bool known = revisions.TryGetValue(key, out var current);
        if (known && graph.Revision <= current)
            return GraphDisposition.StaleRevision;
        if (graph.Mode == "incremental" && (!known || graph.BaseRevision != current))
        {
            requestComplete(new(graph.Context, graph.GraphId));
            return GraphDisposition.NeedCompleteSnapshot;
        }
        if (graph.Mode is not ("complete" or "incremental"))
            throw new ArgumentException("The graph mode has no revision rule.", nameof(graph));
        revisions[key] = graph.Revision;
        return GraphDisposition.Applied;
    }

    public void Clear() => revisions.Clear();
}
