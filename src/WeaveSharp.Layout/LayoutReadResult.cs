using System.Collections.Generic;

namespace WeaveSharp.Layout;

/// <summary>The bounds the layout reader enforces before and while it parses a file.</summary>
public sealed record LayoutReaderLimits
{
    /// <summary>The limits used when a caller supplies none.</summary>
    public static LayoutReaderLimits Default { get; } = new();

    /// <summary>The largest file, in bytes, the reader will parse.</summary>
    public int MaxBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>The deepest element nesting, counting the root element as depth zero.</summary>
    public int MaxDepth { get; init; } = 12;

    /// <summary>The most elements a file may hold.</summary>
    public int MaxElements { get; init; } = 100_000;

    /// <summary>The most attributes an element may carry, namespace declarations excluded.</summary>
    public int MaxAttributesPerElement { get; init; } = 32;

    /// <summary>The longest attribute value, in characters.</summary>
    public int MaxAttributeLength { get; init; } = 4096;

    /// <summary>The longest text content of one element, in characters.</summary>
    public int MaxTextLength { get; init; } = 16 * 1024;

    /// <summary>The largest coordinate magnitude, in logical units.</summary>
    public int MaxCoordinate { get; init; } = 1_000_000;

    /// <summary>The largest user-set extent, in logical units.</summary>
    public int MaxExtent { get; init; } = 100_000;
}

/// <summary>What became of a layout file the reader was given.</summary>
public enum LayoutReadStatus
{
    /// <summary>Every record was read; the file may be rewritten.</summary>
    Valid,

    /// <summary>The file was read, but some records were refused; it must be preserved, not rewritten.</summary>
    Damaged,

    /// <summary>The file declares a newer schema version; it must be preserved, and presentation derived from source.</summary>
    UnsupportedVersion,

    /// <summary>The file was refused as a whole; nothing in it was interpreted.</summary>
    Refused,
}

/// <summary>Why the reader refused a file or a record.</summary>
public enum LayoutProblemKind
{
    /// <summary>The input is larger than <see cref="LayoutReaderLimits.MaxBytes"/>.</summary>
    TooLarge,

    /// <summary>The input is not strictly UTF-8, or declares another encoding.</summary>
    Encoding,

    /// <summary>The input contains a document type declaration, so DTDs and entities are never processed.</summary>
    DocumentType,

    /// <summary>The input contains a processing instruction other than the XML declaration.</summary>
    ProcessingInstruction,

    /// <summary>The input is not well-formed XML.</summary>
    NotWellFormed,

    /// <summary>Elements nest deeper than <see cref="LayoutReaderLimits.MaxDepth"/>.</summary>
    TooDeep,

    /// <summary>The input has more elements than <see cref="LayoutReaderLimits.MaxElements"/>.</summary>
    TooManyElements,

    /// <summary>An element has more attributes than <see cref="LayoutReaderLimits.MaxAttributesPerElement"/>.</summary>
    TooManyAttributes,

    /// <summary>An attribute value or text content is longer than its limit.</summary>
    ValueTooLong,

    /// <summary>The root element is not a WeaveSharp visual document.</summary>
    NotLayout,

    /// <summary>The schema version is missing or not a supported whole number.</summary>
    SchemaVersion,

    /// <summary>A record breaks the schema: a missing or malformed field, or an unsafe value.</summary>
    InvalidRecord,

    /// <summary>A record reuses an identifier already used in its scope.</summary>
    DuplicateId,
}

/// <summary>One refusal, with the position in the file where it was found.</summary>
/// <param name="Kind">Why the reader refused.</param>
/// <param name="Message">A description that names the record or value, never its interpreted meaning.</param>
/// <param name="Line">The one-based line, or zero when unknown.</param>
/// <param name="Column">The one-based column, or zero when unknown.</param>
public sealed record LayoutProblem(LayoutProblemKind Kind, string Message, int Line, int Column);

/// <summary>The outcome of reading one layout file.</summary>
public sealed class LayoutReadResult
{
    internal LayoutReadResult(LayoutReadStatus status, VisualDocument? document, IReadOnlyList<LayoutProblem> problems, int? declaredSchemaVersion)
    {
        Status = status;
        Document = document;
        Problems = problems;
        DeclaredSchemaVersion = declaredSchemaVersion;
    }

    /// <summary>What became of the file.</summary>
    public LayoutReadStatus Status { get; }

    /// <summary>The records read, when the file was valid or damaged; otherwise null.</summary>
    public VisualDocument? Document { get; }

    /// <summary>Every refusal, in the order found.</summary>
    public IReadOnlyList<LayoutProblem> Problems { get; }

    /// <summary>The schema version the file declares, when it declares a whole number.</summary>
    public int? DeclaredSchemaVersion { get; }

    /// <summary>True only when every record was read, so a writer may replace the file without losing anything.</summary>
    public bool CanWrite => Status == LayoutReadStatus.Valid;
}
