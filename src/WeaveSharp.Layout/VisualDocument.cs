using System.Collections.Generic;

namespace WeaveSharp.Layout;

/// <summary>The direction execution reads across a view (design/layout-and-direction.md, section 5).</summary>
public enum FlowDirection
{
    /// <summary>Leading side left, trailing side right.</summary>
    LeftToRight,

    /// <summary>Leading side top, trailing side bottom.</summary>
    TopToBottom,

    /// <summary>Leading side right, trailing side left.</summary>
    RightToLeft,
}

/// <summary>A position in whole logical units: <c>X</c> along the view's flow, <c>Y</c> across it.</summary>
/// <param name="X">The distance along the flow.</param>
/// <param name="Y">The distance across the flow.</param>
public readonly record struct LayoutPoint(int X, int Y);

/// <summary>A size the user set, in whole logical units along and across the flow.</summary>
/// <param name="Along">The length along the flow.</param>
/// <param name="Across">The extent across the flow.</param>
public readonly record struct LayoutExtent(int Along, int Across);

/// <summary>An attribute the reader does not interpret, kept so a writer can preserve it.</summary>
/// <param name="Namespace">The attribute's namespace, or empty.</param>
/// <param name="LocalName">The attribute's local name.</param>
/// <param name="Value">The attribute's value, as text.</param>
public sealed record UnknownAttribute(string Namespace, string LocalName, string Value);

/// <summary>An element the reader does not interpret, kept as XML text so a writer can preserve it.</summary>
/// <param name="Namespace">The element's namespace, or empty.</param>
/// <param name="LocalName">The element's local name.</param>
/// <param name="Xml">The element and its content, serialized without DTDs, entities or processing instructions.</param>
public sealed record UnknownElement(string Namespace, string LocalName, string Xml);

/// <summary>A reference from a record to an identifier no record in its view holds; kept, never refused (META-03).</summary>
/// <param name="RecordId">The record that holds the reference.</param>
/// <param name="MissingId">The identifier it refers to.</param>
public sealed record OrphanedReference(string RecordId, string MissingId);

/// <summary>A layout file's contents: optional presentation for one C# document, never program meaning (META-01).</summary>
/// <param name="SchemaVersion">The schema version, currently always 1.</param>
/// <param name="Source">The C# document, relative to the layout file.</param>
/// <param name="Views">The views read.</param>
/// <param name="UnknownAttributes">Attributes of the root the reader does not interpret.</param>
/// <param name="UnknownElements">Child elements of the root the reader does not interpret.</param>
public sealed record VisualDocument(
    int SchemaVersion,
    string Source,
    IReadOnlyList<LayoutView> Views,
    IReadOnlyList<UnknownAttribute> UnknownAttributes,
    IReadOnlyList<UnknownElement> UnknownElements);

/// <summary>One view of a member: its saved presentation records.</summary>
/// <param name="Id">The document-unique identifier.</param>
/// <param name="MemberHint">The member the view presents, as an advisory hint.</param>
/// <param name="ContextHint">An optional compilation-context hint that partitions presentation.</param>
/// <param name="Flow">The saved flow direction, or null when the view saved none.</param>
/// <param name="Nodes">The node records.</param>
/// <param name="Notes">The notes.</param>
/// <param name="Groups">The presentation groups.</param>
/// <param name="EdgeRoutes">The user-rerouted edges.</param>
/// <param name="Orphans">References to identifiers no record in the view holds.</param>
/// <param name="UnknownAttributes">Attributes the reader does not interpret.</param>
/// <param name="UnknownElements">Child elements the reader does not interpret.</param>
public sealed record LayoutView(
    string Id,
    string? MemberHint,
    string? ContextHint,
    FlowDirection? Flow,
    IReadOnlyList<LayoutNode> Nodes,
    IReadOnlyList<LayoutNote> Notes,
    IReadOnlyList<PresentationGroup> Groups,
    IReadOnlyList<EdgeRoute> EdgeRoutes,
    IReadOnlyList<OrphanedReference> Orphans,
    IReadOnlyList<UnknownAttribute> UnknownAttributes,
    IReadOnlyList<UnknownElement> UnknownElements);

/// <summary>A node's saved presentation: its anchor, and a position only if the user placed it.</summary>
/// <param name="Id">The view-unique identifier.</param>
/// <param name="SyntaxKind">The syntax kind the node presents, as an advisory hint.</param>
/// <param name="Collapsed">The saved collapsed state, or null.</param>
/// <param name="Position">The user-placed position, or null.</param>
/// <param name="Extent">The user-set size, or null.</param>
/// <param name="Anchor">The advisory source-matching information.</param>
/// <param name="UnknownAttributes">Attributes the reader does not interpret.</param>
/// <param name="UnknownElements">Child elements the reader does not interpret.</param>
public sealed record LayoutNode(
    string Id,
    string SyntaxKind,
    bool? Collapsed,
    LayoutPoint? Position,
    LayoutExtent? Extent,
    SourceAnchor Anchor,
    IReadOnlyList<UnknownAttribute> UnknownAttributes,
    IReadOnlyList<UnknownElement> UnknownElements);

/// <summary>Advisory source-matching information; it never authorizes a source edit (META-05).</summary>
/// <param name="SyntaxPathHint">A structural path hint.</param>
/// <param name="MemberSignature">The enclosing member's signature.</param>
/// <param name="SyntaxRole">The syntax role.</param>
/// <param name="Fingerprint">A normalized-source fingerprint.</param>
/// <param name="UnknownAttributes">Attributes the reader does not interpret.</param>
public sealed record SourceAnchor(
    string? SyntaxPathHint,
    string? MemberSignature,
    string? SyntaxRole,
    string? Fingerprint,
    IReadOnlyList<UnknownAttribute> UnknownAttributes);

/// <summary>A plain-text note on a view.</summary>
/// <param name="Id">The view-unique identifier.</param>
/// <param name="Position">The user-placed position, or null.</param>
/// <param name="Extent">The user-set size, or null.</param>
/// <param name="Text">The note's text.</param>
/// <param name="UnknownAttributes">Attributes the reader does not interpret.</param>
/// <param name="UnknownElements">Child elements the reader does not interpret.</param>
public sealed record LayoutNote(
    string Id,
    LayoutPoint? Position,
    LayoutExtent? Extent,
    string Text,
    IReadOnlyList<UnknownAttribute> UnknownAttributes,
    IReadOnlyList<UnknownElement> UnknownElements);

/// <summary>A visual group of nodes; it creates no scope and no program operation.</summary>
/// <param name="Id">The view-unique identifier.</param>
/// <param name="Title">The group's title.</param>
/// <param name="Collapsed">The saved collapsed state, or null.</param>
/// <param name="Note">An optional note.</param>
/// <param name="Members">The identifiers of the nodes it groups.</param>
/// <param name="UnknownAttributes">Attributes the reader does not interpret.</param>
/// <param name="UnknownElements">Child elements the reader does not interpret.</param>
public sealed record PresentationGroup(
    string Id,
    string Title,
    bool? Collapsed,
    string? Note,
    IReadOnlyList<string> Members,
    IReadOnlyList<UnknownAttribute> UnknownAttributes,
    IReadOnlyList<UnknownElement> UnknownElements);

/// <summary>A user-rerouted edge: control points that affect drawing only, never a connection (META-02).</summary>
/// <param name="Id">The view-unique identifier.</param>
/// <param name="From">The source node's identifier.</param>
/// <param name="FromPort">The source port's role.</param>
/// <param name="To">The destination node's identifier.</param>
/// <param name="ToPort">The destination port's role.</param>
/// <param name="Points">The control points, in order.</param>
/// <param name="UnknownAttributes">Attributes the reader does not interpret.</param>
/// <param name="UnknownElements">Child elements the reader does not interpret.</param>
public sealed record EdgeRoute(
    string Id,
    string From,
    string FromPort,
    string To,
    string ToPort,
    IReadOnlyList<LayoutPoint> Points,
    IReadOnlyList<UnknownAttribute> UnknownAttributes,
    IReadOnlyList<UnknownElement> UnknownElements);
