using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaveSharp.Projection.Spike;

namespace WeaveSharp.DebugMapping.Spike;

[JsonConverter(typeof(JsonStringEnumConverter<BuildMode>))]
public enum BuildMode { Debug, Release }

[JsonConverter(typeof(JsonStringEnumConverter<MappingQuality>))]
public enum MappingQuality { Exact, Approximate, Unavailable }

[JsonConverter(typeof(JsonStringEnumConverter<MappingRule>))]
public enum MappingRule
{
    AnchorEquality, SharedSequencePoint, SpanIntersection, OneConfigurationOnly,
    NoVisibleSequencePoint, NoAssociatedMethod, HiddenSequencePoint, UnknownDocument,
    OutsideProjectedBody, AmbiguousBody, GeneratedHelper, NoSequencePoints,
    UnknownMethod, OffsetOutsideMethod, BeforeFirstPoint, BetweenPoints
}

public sealed record ProjectedBody(string Id, string Callable, int Start, int Length, ProjectionGraph Graph);
public sealed record PdbExtra(string Kind, string Bytes);
public sealed record MethodEvidence(int Token, string DeclaringType, string Name, int CodeSize,
    int? KickoffToken, string? KickoffName, string? BodyId, string BodyRule,
    ImmutableArray<PdbExtra> CustomDebugInformation);
public sealed record PointMapping(int MethodToken, int Offset, bool Hidden, string Document,
    int StartLine, int StartColumn, int EndLine, int EndColumn, int? Start, int? Length,
    string? BodyId, MappingQuality Quality, MappingRule Rule, ImmutableArray<string> Targets);
public sealed record ElementMapping(string BodyId, string ElementId, MappingQuality Quality,
    MappingRule Rule, ImmutableArray<string> Points);
public sealed record BuildMapping(BuildMode Mode, ImmutableArray<MethodEvidence> Methods,
    ImmutableArray<PointMapping> Points, ImmutableArray<ElementMapping> Elements);
public sealed record MappingStudy(string Document, string Revision, ImmutableArray<ProjectedBody> Bodies,
    ImmutableArray<BuildMapping> Builds)
{
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}

// Tokens and offsets belong to one emitted module; this record is a lookup result, not a pause event.
public sealed record StopMapping(int MethodToken, int Offset, int? SequenceOffset, string? BodyId,
    MappingQuality Quality, MappingRule Rule, ImmutableArray<string> Targets);
