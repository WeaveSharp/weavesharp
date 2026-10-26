using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeaveSharp.Projection.Spike;

// Start and Length use UTF-16 offsets in the complete input snapshot.
// Path uses zero-based ChildNodes() positions, starting at the chosen body ($).
public sealed record SourceAnchor(
    string Document, string Revision, int Start, int Length, string SyntaxKind, string Path);

[JsonConverter(typeof(JsonStringEnumConverter<ElementKind>))]
public enum ElementKind
{
    Statement, Operation, Block, Conditional, Loop, Switch, SwitchSection,
    Try, Catch, Finally, Using, Lock, Await, CSharpRegion
}

public sealed record GraphElement(
    string Id, string? Parent, string Role, ElementKind Kind, SourceAnchor Anchor,
    bool OwnsStatement, string? Source = null, string? FallbackReason = null);

public sealed record Endpoint(string Element, string Port);

[JsonConverter(typeof(JsonStringEnumConverter<RelationshipKind>))]
public enum RelationshipKind
{
    Sequence, Branch, LoopBack, Exit, Exception, Cleanup, Await, Resume, Value, Storage
}

public sealed record Relationship(
    Endpoint From, Endpoint To, RelationshipKind Kind, string? Label = null);

// An abrupt transfer may cross several cleanup scopes, innermost first.
public sealed record CleanupStep(string Transfer, int Order, string Region);

public sealed class ProjectionGraph : IEquatable<ProjectionGraph>
{
    [JsonConstructor]
    public ProjectionGraph(ImmutableArray<GraphElement> elements,
        ImmutableArray<Relationship> relationships, ImmutableArray<CleanupStep> cleanups)
    {
        Elements = elements;
        Relationships = relationships;
        Cleanups = cleanups;
    }

    public ImmutableArray<GraphElement> Elements { get; }
    public ImmutableArray<Relationship> Relationships { get; }
    public ImmutableArray<CleanupStep> Cleanups { get; }

    public bool Equals(ProjectionGraph? other) => other is not null
        && Elements.SequenceEqual(other.Elements)
        && Relationships.SequenceEqual(other.Relationships)
        && Cleanups.SequenceEqual(other.Cleanups);

    public override bool Equals(object? obj) => obj is ProjectionGraph other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var element in Elements) hash.Add(element);
        foreach (var relationship in Relationships) hash.Add(relationship);
        foreach (var cleanup in Cleanups) hash.Add(cleanup);
        return hash.ToHashCode();
    }

    public string ToJson() => JsonSerializer.Serialize(this);
}
