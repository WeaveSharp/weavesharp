using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeaveSharp.Protocol;

public static class ProtocolJson
{
    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public enum MessageKind { Query, Mutation, Response, Notification }
public sealed record ProtocolVersion(int Major, int Minor);
public sealed record ProtocolExtension(bool Mandatory, string Value);
public sealed record Envelope<T>(ProtocolVersion Version, string Family, string RequestId,
    MessageKind Kind, T Payload, IReadOnlyDictionary<string, ProtocolExtension> Extensions);

public sealed record SnapshotIdentity(string HostId, string DocumentUri, string HostVersion, string SnapshotId);
public sealed record RequestContext(string WorkspaceId, string ProjectId, string CompilationContextId,
    SnapshotIdentity Snapshot);
public sealed record SourceSpan(SnapshotIdentity Snapshot, string Coordinates, int Start, int Length);
public sealed record NamedValue(string Name, string Value);
public sealed record ComponentVersion(string Name, string Version);
public sealed record SourceReference(string DocumentUri, string Revision);
public sealed record TextChange(SourceSpan Span, string Text);
public sealed record SourceMapping(string NodeId, SourceSpan Source);
public sealed record ProtocolDiagnostic(string Code, string Severity, string Message, SourceSpan Source);

public sealed record InitializePayload(IReadOnlyList<ProtocolVersion> Versions,
    IReadOnlyList<ComponentVersion> Components, string HostId, string WorkspaceId,
    IReadOnlyList<string> Capabilities, string TrustState);
public sealed record CompilationContextPayload(string WorkspaceId, string ProjectId, string ContextId,
    string TargetId, string LanguageVersion, string CompilerVersion, IReadOnlyList<NamedValue> Options,
    IReadOnlyList<SourceReference> References, string GeneratedSourceRevision);
public sealed record SourceSnapshotPayload(RequestContext Context, string Mode, string Provenance,
    string? Encoding = null, string? Text = null, string? BaseSnapshotId = null,
    IReadOnlyList<TextChange>? Changes = null);
public sealed record RequestGraphPayload(RequestContext Context, SourceSpan Body, string Detail, bool RequireComplete);
public sealed record GraphNode(string Id, string Kind, string TypeName);
public sealed record GraphEdge(string Id, string From, string To, string Kind);
public sealed record GraphRegion(string Id, string Kind, IReadOnlyList<string> NodeIds);
public sealed record PublishGraphPayload(RequestContext Context, string GraphId, long Revision, string Mode,
    IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges, IReadOnlyList<GraphRegion> Regions,
    IReadOnlyList<SourceMapping> Mappings, IReadOnlyList<ProtocolDiagnostic> Diagnostics, string Coverage,
    long? BaseRevision = null, IReadOnlyList<string>? RemovedNodeIds = null,
    IReadOnlyList<string>? RemovedEdgeIds = null, IReadOnlyList<string>? RemovedRegionIds = null);
public sealed record PrepareEditPayload(RequestContext Context, string Command,
    IReadOnlyList<SourceSpan> Anchors, IReadOnlyList<NamedValue> Choices,
    IReadOnlyList<SnapshotIdentity> Destinations);
public sealed record EditResultPayload(RequestContext Context, string Stage, string Outcome, string Description,
    IReadOnlyList<TextChange> Changes, IReadOnlyList<SnapshotIdentity> Documents,
    IReadOnlyList<SourceMapping> Mappings, IReadOnlyList<ProtocolDiagnostic> Diagnostics, string? RejectionReason = null);
public sealed record ExecutionStatePayload(string SessionId, RequestContext Context, string SourceGeneration,
    string State, string EvidenceKind, IReadOnlyList<string> Capabilities,
    string? PauseId = null, string? SelectedFrameId = null);
public sealed record LayoutPosition(string NodeId, double X, double Y);
public sealed record LayoutPayload(int SchemaVersion, RequestContext Context, string View, string Operation,
    string Revision, IReadOnlyList<LayoutPosition> Positions, IReadOnlyList<string> Conflicts);
public sealed record CancelOrDisposePayload(string Operation, string TargetKind, string TargetId);
