using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace WeaveSharp.Layout;

/// <summary>
/// Reads WeaveSharp layout files as hostile input (SEC-03). A layout file comes from a
/// repository anyone may write, so the reader processes no DTD or entity, follows no
/// reference, runs nothing, and bounds every size before or while it parses. It reads the
/// vocabulary of META-02 and design/layout-and-direction.md into a typed model, refuses each
/// invalid record, keeps content it does not interpret, and never infers program meaning.
/// </summary>
public static partial class LayoutReader
{
    /// <summary>The namespace of schema version 1.</summary>
    public const string Namespace = "urn:weavesharp:visual:1";

    /// <summary>The newest schema version this reader understands.</summary>
    public const int SupportedSchemaVersion = 1;

    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly byte[] Utf8ByteOrderMark = [0xEF, 0xBB, 0xBF];

    /// <summary>Reads a layout file held in memory.</summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="limits">The bounds to enforce, or null for <see cref="LayoutReaderLimits.Default"/>.</param>
    /// <returns>What became of the file, the records read and every refusal.</returns>
    public static LayoutReadResult Read(ReadOnlySpan<byte> bytes, LayoutReaderLimits? limits = null)
    {
        limits ??= LayoutReaderLimits.Default;
        if (bytes.Length > limits.MaxBytes)
        {
            return Refused(LayoutProblemKind.TooLarge, $"The file is {bytes.Length} bytes; the limit is {limits.MaxBytes}.");
        }

        return Parse(bytes, limits);
    }

    /// <summary>Reads a layout file from a stream, never reading more than one byte past the size limit.</summary>
    /// <param name="stream">The file's contents; the reader does not close it.</param>
    /// <param name="limits">The bounds to enforce, or null for <see cref="LayoutReaderLimits.Default"/>.</param>
    /// <returns>What became of the file, the records read and every refusal.</returns>
    public static LayoutReadResult Read(Stream stream, LayoutReaderLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        limits ??= LayoutReaderLimits.Default;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var wanted = (int)Math.Min(chunk.Length, (long)limits.MaxBytes + 1 - buffer.Length);
            var read = stream.Read(chunk, 0, wanted);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
            if (buffer.Length > limits.MaxBytes)
            {
                return Refused(LayoutProblemKind.TooLarge, $"The file is larger than the limit of {limits.MaxBytes} bytes.");
            }
        }

        return Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), limits);
    }

    private static LayoutReadResult Parse(ReadOnlySpan<byte> bytes, LayoutReaderLimits limits)
    {
        if (StartsWithAnotherByteOrderMark(bytes))
        {
            return Refused(LayoutProblemKind.Encoding, "The file is not UTF-8.");
        }

        if (bytes.StartsWith(Utf8ByteOrderMark))
        {
            bytes = bytes[Utf8ByteOrderMark.Length..];
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Refused(LayoutProblemKind.Encoding, "The file is not valid UTF-8.");
        }

        var (root, refusal) = BuildTree(text, limits);
        if (refusal is not null)
        {
            return refusal;
        }

        return new Mapper(limits).Map(root!);
    }

    private static bool StartsWithAnotherByteOrderMark(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) ||
        bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) ||
        bytes.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]) ||
        (bytes.Length >= 2 && (bytes[0] == 0x00 || bytes[1] == 0x00));

    private static (Element? Root, LayoutReadResult? Refusal) BuildTree(string text, LayoutReaderLimits limits)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = limits.MaxBytes,
            IgnoreComments = true,
            IgnoreProcessingInstructions = false,
            IgnoreWhitespace = true,
            CheckCharacters = true,
            ConformanceLevel = ConformanceLevel.Document,
        };

        Element? root = null;
        var open = new Stack<Element>();
        var elements = 0;
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), settings);
            var position = (IXmlLineInfo)reader;
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.XmlDeclaration:
                        var encoding = reader.GetAttribute("encoding");
                        if (encoding is not null && !encoding.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
                        {
                            return (null, Refused(LayoutProblemKind.Encoding, "The file declares an encoding other than UTF-8.", position));
                        }

                        break;

                    case XmlNodeType.ProcessingInstruction:
                        return (null, Refused(LayoutProblemKind.ProcessingInstruction, "The file contains a processing instruction.", position));

                    case XmlNodeType.DocumentType:
                    case XmlNodeType.EntityReference:
                        return (null, Refused(LayoutProblemKind.DocumentType, "The file declares a document type or an entity.", position));

                    case XmlNodeType.Element:
                        if (reader.Depth > limits.MaxDepth)
                        {
                            return (null, Refused(LayoutProblemKind.TooDeep, $"Elements nest deeper than {limits.MaxDepth}.", position));
                        }

                        if (++elements > limits.MaxElements)
                        {
                            return (null, Refused(LayoutProblemKind.TooManyElements, $"The file has more than {limits.MaxElements} elements.", position));
                        }

                        var element = new Element(reader.NamespaceURI, reader.LocalName, position.LineNumber, position.LinePosition);
                        if (reader.MoveToFirstAttribute())
                        {
                            do
                            {
                                if (reader.NamespaceURI == XmlnsNamespace)
                                {
                                    continue;
                                }

                                if (element.Attributes.Count == limits.MaxAttributesPerElement)
                                {
                                    return (null, Refused(LayoutProblemKind.TooManyAttributes, $"An element has more than {limits.MaxAttributesPerElement} attributes.", element));
                                }

                                if (reader.Value.Length > limits.MaxAttributeLength)
                                {
                                    return (null, Refused(LayoutProblemKind.ValueTooLong, $"An attribute value is longer than {limits.MaxAttributeLength} characters.", element));
                                }

                                element.Attributes.Add(new UnknownAttribute(reader.NamespaceURI, reader.LocalName, reader.Value));
                            }
                            while (reader.MoveToNextAttribute());

                            reader.MoveToElement();
                        }

                        if (open.Count > 0)
                        {
                            open.Peek().Children.Add(element);
                        }
                        else
                        {
                            root = element;
                        }

                        if (!reader.IsEmptyElement)
                        {
                            open.Push(element);
                        }

                        break;

                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.SignificantWhitespace:
                        var current = open.Peek();
                        if (current.Text.Length + reader.Value.Length > limits.MaxTextLength)
                        {
                            return (null, Refused(LayoutProblemKind.ValueTooLong, $"An element's text is longer than {limits.MaxTextLength} characters.", current));
                        }

                        current.Text.Append(reader.Value);
                        break;

                    case XmlNodeType.EndElement:
                        open.Pop();
                        break;
                }
            }
        }
        catch (XmlException exception)
        {
            // Prohibited DTD processing surfaces as a parse error; name it for what it is.
            var kind = text.Contains("<!DOCTYPE", StringComparison.Ordinal) ? LayoutProblemKind.DocumentType : LayoutProblemKind.NotWellFormed;
            var message = kind == LayoutProblemKind.DocumentType
                ? "The file contains a document type declaration; DTDs and entities are never processed."
                : "The file is not well-formed XML.";
            return (null, Refused(kind, message, exception.LineNumber, exception.LinePosition));
        }

        return root is null
            ? (null, Refused(LayoutProblemKind.NotWellFormed, "The file has no root element."))
            : (root, null);
    }

    private static LayoutReadResult Refused(LayoutProblemKind kind, string message, int line = 0, int column = 0) =>
        new(LayoutReadStatus.Refused, null, [new LayoutProblem(kind, message, line, column)], null);

    private static LayoutReadResult Refused(LayoutProblemKind kind, string message, IXmlLineInfo position) =>
        Refused(kind, message, position.LineNumber, position.LinePosition);

    private static LayoutReadResult Refused(LayoutProblemKind kind, string message, Element element) =>
        Refused(kind, message, element.Line, element.Column);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SyntaxKindPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex PortRolePattern();

    [GeneratedRegex("^-?[0-9]{1,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex CoordinatePattern();

    [GeneratedRegex("^[0-9]{1,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtentPattern();

    /// <summary>A parsed element, bounded by the reader's limits, before any interpretation.</summary>
    private sealed class Element(string ns, string localName, int line, int column)
    {
        public string Namespace { get; } = ns;

        public string LocalName { get; } = localName;

        public int Line { get; } = line;

        public int Column { get; } = column;

        public List<UnknownAttribute> Attributes { get; } = [];

        public List<Element> Children { get; } = [];

        public StringBuilder Text { get; } = new();

        public bool Is(string localName) => Namespace == LayoutReader.Namespace && LocalName == localName;

        public string? Attribute(string localName) =>
            Attributes.FirstOrDefault(a => a.Namespace.Length == 0 && a.LocalName == localName)?.Value;

        public List<UnknownAttribute> AttributesExcept(params string[] known) =>
            [.. Attributes.Where(a => a.Namespace.Length != 0 || !known.Contains(a.LocalName))];

        public bool HasText => Text.ToString().Trim().Length > 0;

        /// <summary>True when every attribute is one of the unqualified names given; the model keeps nothing else here.</summary>
        public bool OnlyAttributes(params string[] known) =>
            Attributes.All(a => a.Namespace.Length == 0 && known.Contains(a.LocalName));

        public XElement ToXElement()
        {
            var element = new XElement(XName.Get(LocalName, Namespace));
            foreach (var attribute in Attributes)
            {
                element.SetAttributeValue(XName.Get(attribute.LocalName, attribute.Namespace), attribute.Value);
            }

            foreach (var child in Children)
            {
                element.Add(child.ToXElement());
            }

            if (Text.Length > 0)
            {
                element.Add(new XText(Text.ToString()));
            }

            return element;
        }

        public UnknownElement ToUnknown() =>
            new(Namespace, LocalName, ToXElement().ToString(SaveOptions.DisableFormatting));
    }

    /// <summary>Interprets a parsed tree as schema version 1, refusing each invalid record.</summary>
    private sealed class Mapper(LayoutReaderLimits limits)
    {
        private readonly List<LayoutProblem> problems = [];

        public LayoutReadResult Map(Element root)
        {
            if (!root.Is("visualDocument"))
            {
                return Refused(LayoutProblemKind.NotLayout, $"The root element is not a visual document in {LayoutReader.Namespace}.", root);
            }

            var versionText = root.Attribute("schemaVersion");
            if (versionText is null || !int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            {
                return Refused(LayoutProblemKind.SchemaVersion, "The schema version is missing or is not a whole number of at least 1.", root);
            }

            if (version > SupportedSchemaVersion)
            {
                var newer = new LayoutProblem(
                    LayoutProblemKind.SchemaVersion,
                    $"The file declares schema version {version}; this reader understands {SupportedSchemaVersion}. Preserve the file and derive presentation from source.",
                    root.Line,
                    root.Column);
                return new LayoutReadResult(LayoutReadStatus.UnsupportedVersion, null, [newer], version);
            }

            var source = root.Attribute("source");
            if (source is null || !IsSafeRelativePath(source))
            {
                return new LayoutReadResult(
                    LayoutReadStatus.Refused,
                    null,
                    [new LayoutProblem(LayoutProblemKind.InvalidRecord, "The source must be a relative path inside the workspace: no root, drive, scheme, backslash or dot segment.", root.Line, root.Column)],
                    version);
            }

            if (root.HasText)
            {
                return new LayoutReadResult(
                    LayoutReadStatus.Refused,
                    null,
                    [new LayoutProblem(LayoutProblemKind.InvalidRecord, "The visual document holds text outside its records.", root.Line, root.Column)],
                    version);
            }

            var views = new List<LayoutView>();
            var unknown = new List<UnknownElement>();
            var viewIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in root.Children)
            {
                if (child.Is("view"))
                {
                    var view = MapView(child, viewIds);
                    if (view is not null)
                    {
                        views.Add(view);
                    }
                }
                else
                {
                    unknown.Add(child.ToUnknown());
                }
            }

            var document = new VisualDocument(version, source, views, root.AttributesExcept("schemaVersion", "source"), unknown);
            return new LayoutReadResult(problems.Count == 0 ? LayoutReadStatus.Valid : LayoutReadStatus.Damaged, document, problems, version);
        }

        private LayoutView? MapView(Element view, HashSet<string> viewIds)
        {
            if (!Identifier(view, "id", out var id) || !Unique(view, id, viewIds))
            {
                return null;
            }

            FlowDirection? flow = null;
            var flowText = view.Attribute("flow");
            if (flowText is not null)
            {
                flow = flowText switch
                {
                    "left-to-right" => FlowDirection.LeftToRight,
                    "top-to-bottom" => FlowDirection.TopToBottom,
                    "right-to-left" => FlowDirection.RightToLeft,
                    _ => null,
                };
                if (flow is null)
                {
                    return Invalid<LayoutView>(view, $"View {id} has an unknown flow direction.");
                }
            }

            if (view.HasText)
            {
                return Invalid<LayoutView>(view, $"View {id} holds text outside its records.");
            }

            var recordIds = new HashSet<string>(StringComparer.Ordinal);
            var nodes = new List<LayoutNode>();
            var notes = new List<LayoutNote>();
            var groups = new List<PresentationGroup>();
            var routes = new List<EdgeRoute>();
            var unknown = new List<UnknownElement>();
            foreach (var child in view.Children)
            {
                if (child.Is("node"))
                {
                    Add(nodes, MapNode(child, recordIds));
                }
                else if (child.Is("note"))
                {
                    Add(notes, MapNote(child, recordIds));
                }
                else if (child.Is("group"))
                {
                    Add(groups, MapGroup(child, recordIds));
                }
                else if (child.Is("edgeRoute"))
                {
                    Add(routes, MapRoute(child, recordIds));
                }
                else
                {
                    unknown.Add(child.ToUnknown());
                }
            }

            var nodeIds = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            var orphans = new List<OrphanedReference>();
            foreach (var group in groups)
            {
                orphans.AddRange(group.Members.Where(m => !nodeIds.Contains(m)).Select(m => new OrphanedReference(group.Id, m)));
            }

            foreach (var route in routes)
            {
                orphans.AddRange(new[] { route.From, route.To }.Where(n => !nodeIds.Contains(n)).Select(n => new OrphanedReference(route.Id, n)));
            }

            return new LayoutView(
                id,
                view.Attribute("memberHint"),
                view.Attribute("contextHint"),
                flow,
                nodes,
                notes,
                groups,
                routes,
                orphans,
                view.AttributesExcept("id", "memberHint", "contextHint", "flow"),
                unknown);
        }

        private LayoutNode? MapNode(Element node, HashSet<string> recordIds)
        {
            if (!Identifier(node, "id", out var id))
            {
                return null;
            }

            var syntaxKind = node.Attribute("syntaxKind");
            if (syntaxKind is null || !SyntaxKindPattern().IsMatch(syntaxKind))
            {
                return Invalid<LayoutNode>(node, $"Node {id} needs a syntax kind made of letters and digits.");
            }

            if (!Flag(node, "collapsed", out var collapsed) || !Position(node, out var position) || node.HasText)
            {
                return Invalid<LayoutNode>(node, $"Node {id} has an invalid collapsed state, position or text.");
            }

            var anchors = node.Children.Where(c => c.Is("anchor")).ToList();
            var extents = node.Children.Where(c => c.Is("extent")).ToList();
            if (anchors.Count != 1 || extents.Count > 1)
            {
                return Invalid<LayoutNode>(node, $"Node {id} needs exactly one anchor and at most one extent.");
            }

            var anchor = MapAnchor(anchors[0], id);
            LayoutExtent? extent = null;
            if (anchor is null || (extents.Count == 1 && (extent = MapExtent(extents[0], id)) is null))
            {
                return null;
            }

            if (!Unique(node, id, recordIds))
            {
                return null;
            }

            return new LayoutNode(
                id,
                syntaxKind,
                collapsed,
                position,
                extent,
                anchor,
                node.AttributesExcept("id", "syntaxKind", "collapsed", "x", "y"),
                [.. node.Children.Where(c => !c.Is("anchor") && !c.Is("extent")).Select(c => c.ToUnknown())]);
        }

        private SourceAnchor? MapAnchor(Element anchor, string nodeId)
        {
            var known = new[] { "syntaxPathHint", "memberSignature", "syntaxRole", "fingerprint" };
            if (anchor.Children.Count > 0 || anchor.HasText || known.All(name => string.IsNullOrEmpty(anchor.Attribute(name))))
            {
                return Invalid<SourceAnchor>(anchor, $"Node {nodeId}'s anchor needs at least one hint and holds nothing else.");
            }

            return new SourceAnchor(
                anchor.Attribute("syntaxPathHint"),
                anchor.Attribute("memberSignature"),
                anchor.Attribute("syntaxRole"),
                anchor.Attribute("fingerprint"),
                anchor.AttributesExcept(known));
        }

        private LayoutNote? MapNote(Element note, HashSet<string> recordIds)
        {
            if (!Identifier(note, "id", out var id))
            {
                return null;
            }

            var texts = note.Children.Where(c => c.Is("text")).ToList();
            var extents = note.Children.Where(c => c.Is("extent")).ToList();
            if (!Position(note, out var position) || note.HasText || texts.Count != 1 || texts[0].Children.Count > 0 ||
                !texts[0].OnlyAttributes() || extents.Count > 1)
            {
                return Invalid<LayoutNote>(note, $"Note {id} needs exactly one text, at most one extent and a valid position.");
            }

            LayoutExtent? extent = null;
            if ((extents.Count == 1 && (extent = MapExtent(extents[0], id)) is null) || !Unique(note, id, recordIds))
            {
                return null;
            }

            return new LayoutNote(
                id,
                position,
                extent,
                texts[0].Text.ToString(),
                note.AttributesExcept("id", "x", "y"),
                [.. note.Children.Where(c => !c.Is("text") && !c.Is("extent")).Select(c => c.ToUnknown())]);
        }

        private PresentationGroup? MapGroup(Element group, HashSet<string> recordIds)
        {
            if (!Identifier(group, "id", out var id))
            {
                return null;
            }

            var title = group.Attribute("title");
            var members = new List<string>();
            foreach (var member in group.Children.Where(c => c.Is("member")))
            {
                var node = member.Attribute("node");
                if (node is null || !IdentifierPattern().IsMatch(node) || member.Children.Count > 0 || member.HasText || !member.OnlyAttributes("node"))
                {
                    return Invalid<PresentationGroup>(member, $"Group {id} has a member that names no valid node.");
                }

                members.Add(node);
            }

            if (string.IsNullOrWhiteSpace(title) || members.Count == 0 || !Flag(group, "collapsed", out var collapsed) || group.HasText)
            {
                return Invalid<PresentationGroup>(group, $"Group {id} needs a title, at least one member and a valid collapsed state.");
            }

            if (!Unique(group, id, recordIds))
            {
                return null;
            }

            return new PresentationGroup(
                id,
                title,
                collapsed,
                group.Attribute("note"),
                members,
                group.AttributesExcept("id", "title", "collapsed", "note"),
                [.. group.Children.Where(c => !c.Is("member")).Select(c => c.ToUnknown())]);
        }

        private EdgeRoute? MapRoute(Element route, HashSet<string> recordIds)
        {
            if (!Identifier(route, "id", out var id))
            {
                return null;
            }

            var from = route.Attribute("from");
            var to = route.Attribute("to");
            var fromPort = route.Attribute("fromPort");
            var toPort = route.Attribute("toPort");
            if (from is null || to is null || fromPort is null || toPort is null ||
                !IdentifierPattern().IsMatch(from) || !IdentifierPattern().IsMatch(to) ||
                !PortRolePattern().IsMatch(fromPort) || !PortRolePattern().IsMatch(toPort) || route.HasText)
            {
                return Invalid<EdgeRoute>(route, $"Edge route {id} needs valid from, fromPort, to and toPort values.");
            }

            var points = new List<LayoutPoint>();
            foreach (var point in route.Children.Where(c => c.Is("point")))
            {
                if (!Position(point, out var value) || value is null || point.Children.Count > 0 || point.HasText || !point.OnlyAttributes("x", "y"))
                {
                    return Invalid<EdgeRoute>(point, $"Edge route {id} has a point without valid whole-number coordinates.");
                }

                points.Add(value.Value);
            }

            if (points.Count == 0)
            {
                return Invalid<EdgeRoute>(route, $"Edge route {id} needs at least one point.");
            }

            if (!Unique(route, id, recordIds))
            {
                return null;
            }

            return new EdgeRoute(
                id,
                from,
                fromPort,
                to,
                toPort,
                points,
                route.AttributesExcept("id", "from", "fromPort", "to", "toPort"),
                [.. route.Children.Where(c => !c.Is("point")).Select(c => c.ToUnknown())]);
        }

        private LayoutExtent? MapExtent(Element extent, string recordId)
        {
            var along = extent.Attribute("along");
            var across = extent.Attribute("across");
            if (along is null || across is null || !ExtentPattern().IsMatch(along) || !ExtentPattern().IsMatch(across) ||
                extent.Children.Count > 0 || extent.HasText || !extent.OnlyAttributes("along", "across"))
            {
                return Invalid<LayoutExtent?>(extent, $"Record {recordId} has an extent without whole-number along and across values.");
            }

            var a = int.Parse(along, NumberStyles.None, CultureInfo.InvariantCulture);
            var c = int.Parse(across, NumberStyles.None, CultureInfo.InvariantCulture);
            if (a < 1 || c < 1 || a > limits.MaxExtent || c > limits.MaxExtent)
            {
                return Invalid<LayoutExtent?>(extent, $"Record {recordId} has an extent outside 1 to {limits.MaxExtent}.");
            }

            return new LayoutExtent(a, c);
        }

        private bool Position(Element element, out LayoutPoint? position)
        {
            position = null;
            var x = element.Attribute("x");
            var y = element.Attribute("y");
            if (x is null && y is null)
            {
                return true;
            }

            if (x is null || y is null || !CoordinatePattern().IsMatch(x) || !CoordinatePattern().IsMatch(y))
            {
                return false;
            }

            var px = int.Parse(x, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            var py = int.Parse(y, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (Math.Abs(px) > limits.MaxCoordinate || Math.Abs(py) > limits.MaxCoordinate)
            {
                return false;
            }

            position = new LayoutPoint(px, py);
            return true;
        }

        private static bool Flag(Element element, string name, out bool? value)
        {
            value = element.Attribute(name) switch
            {
                null => null,
                "true" => true,
                "false" => false,
                _ => null,
            };
            return element.Attribute(name) is null or "true" or "false";
        }

        private bool Identifier(Element element, string name, out string id)
        {
            id = element.Attribute(name) ?? string.Empty;
            if (IdentifierPattern().IsMatch(id))
            {
                return true;
            }

            Invalid<object>(element, $"A {element.LocalName} record needs an {name} of letters, digits, dots, hyphens or underscores.");
            return false;
        }

        private bool Unique(Element element, string id, HashSet<string> scope)
        {
            if (scope.Add(id))
            {
                return true;
            }

            problems.Add(new LayoutProblem(LayoutProblemKind.DuplicateId, $"Identifier {id} is used twice in its scope; the second record is refused.", element.Line, element.Column));
            return false;
        }

        private T? Invalid<T>(Element element, string message)
        {
            problems.Add(new LayoutProblem(LayoutProblemKind.InvalidRecord, message, element.Line, element.Column));
            return default;
        }

        private static void Add<T>(List<T> list, T? item)
            where T : class
        {
            if (item is not null)
            {
                list.Add(item);
            }
        }
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (path.Length is 0 or > 1024 || path[0] == '/' || path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl))
        {
            return false;
        }

        return path.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");
    }
}
