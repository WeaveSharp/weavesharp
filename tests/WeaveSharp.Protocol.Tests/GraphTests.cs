using System.Collections.Generic;
using Xunit;

namespace WeaveSharp.Protocol.Tests;

public sealed class GraphTests
{
    private static PublishGraphPayload Complete(long revision = 5) => new(SessionTests.Context, "body-a", revision,
        "complete", [new("n1", "return", "System.Int32")], [], [], [], [], "complete");
    private static PublishGraphPayload Delta(long revision, long baseRevision) => Complete(revision) with
    {
        Mode = "incremental", BaseRevision = baseRevision,
        RemovedNodeIds = [], RemovedEdgeIds = [], RemovedRegionIds = []
    };

    [Fact]
    public void MissingBaseRequestsACompleteGraphAndDoesNotAdvanceTheRevision()
    {
        var requested = new List<CompleteGraphRequest>();
        var receiver = new GraphReceiver(requested.Add);
        Assert.Equal(GraphDisposition.Applied, receiver.Receive(Complete(), SessionTests.Context));
        Assert.Equal(GraphDisposition.NeedCompleteSnapshot, receiver.Receive(Delta(7, 6), SessionTests.Context));
        Assert.Equal(5, receiver.Revision(SessionTests.Context, "body-a"));
        Assert.Equal(new CompleteGraphRequest(SessionTests.Context, "body-a"), Assert.Single(requested));
        Assert.Equal(GraphDisposition.Applied, receiver.Receive(Complete(7), SessionTests.Context));
        Assert.Equal(GraphDisposition.Applied, receiver.Receive(Delta(8, 7), SessionTests.Context));
        Assert.Equal(8, receiver.Revision(SessionTests.Context, "body-a"));
    }

    [Fact]
    public void NewReceiverCannotApplyAnIncrementalGraphWithoutItsBase()
    {
        var requested = new List<CompleteGraphRequest>();
        var receiver = new GraphReceiver(requested.Add);
        Assert.Equal(GraphDisposition.NeedCompleteSnapshot, receiver.Receive(Delta(5, 4), SessionTests.Context));
        Assert.Null(receiver.Revision(SessionTests.Context, "body-a"));
        Assert.Single(requested);
    }

    [Fact]
    public void GraphRevisionBelongsToItsContextAndBody()
    {
        var requested = new List<CompleteGraphRequest>();
        var receiver = new GraphReceiver(requested.Add);
        receiver.Receive(Complete(), SessionTests.Context);
        Assert.Equal(GraphDisposition.NeedCompleteSnapshot, receiver.Receive(Delta(6, 5) with { GraphId = "body-b" }, SessionTests.Context));
        var other = SessionTests.Context with { CompilationContextId = "ctx-4" };
        Assert.Equal(GraphDisposition.StaleContext, receiver.Receive(Complete(9), other));
        Assert.Equal(GraphDisposition.NeedCompleteSnapshot, receiver.Receive(Delta(6, 5) with { Context = other }, other));
        Assert.Equal(2, requested.Count);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void OldAndDuplicateGraphsCannotMoveTheRevisionBackwards(long revision)
    {
        var requested = new List<CompleteGraphRequest>();
        var receiver = new GraphReceiver(requested.Add);
        receiver.Receive(Complete(), SessionTests.Context);
        Assert.Equal(GraphDisposition.StaleRevision, receiver.Receive(Complete(revision), SessionTests.Context));
        Assert.Equal(5, receiver.Revision(SessionTests.Context, "body-a"));
        Assert.Empty(requested);
        receiver.Clear();
        Assert.Null(receiver.Revision(SessionTests.Context, "body-a"));
    }
}
