using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace WeaveSharp.Layout;

/// <summary>Serializes saved presentation using a repeatable, line-oriented XML format.</summary>
public static class LayoutWriter
{
    /// <summary>Produces UTF-8 bytes for a document using the reader's default limits.</summary>
    /// <param name="document">Saved records; positions and extents must represent user choices.</param>
    /// <returns>XML with no BOM, two-space nesting, and LF line endings.</returns>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    /// <exception cref="ArgumentException">The supplied records cannot form a valid layout file.</exception>
    /// <remarks>
    /// Callers reading an existing file must check <see cref="LayoutReadResult.CanWrite"/>
    /// before passing its document here. This method does not replace files or resolve anchors.
    /// </remarks>
    public static byte[] Write(VisualDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        byte[] bytes;
        try
        {
            // Check raw fragments with the existing reader before parsing them for
            // namespace-aware emission. The draft leaves the default namespace empty.
            RequireReadable(Serialize(document, canonical: false));
            bytes = Serialize(document, canonical: true);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or XmlException)
        {
            throw new ArgumentException("The layout model could not be serialized: " + error.Message, nameof(document), error);
        }

        RequireReadable(bytes);
        return bytes;
    }

    private static void RequireReadable(byte[] bytes)
    {
        var read = LayoutReader.Read(bytes);
        if (!read.CanWrite)
        {
            var details = string.Join("; ", read.Problems.Select(p => $"{p.Kind}: {p.Message}"));
            throw new ArgumentException($"The serialized layout was {read.Status}. {details}", "document");
        }
    }

    private static byte[] Serialize(VisualDocument document, bool canonical)
    {
        using var buffer = new MemoryStream();
        using (var xml = XmlWriter.Create(buffer, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Entitize,
            Indent = false,
            OmitXmlDeclaration = !canonical,
            CloseOutput = false,
        }))
        {
            var output = new Output(xml, canonical);
            xml.WriteStartDocument();
            if (canonical)
            {
                xml.WriteWhitespace("\n");
            }
            output.Start("visualDocument", 0);
            if (canonical)
            {
                xml.WriteAttributeString("xmlns", LayoutReader.Namespace);
            }
            else
            {
                xml.WriteAttributeString("xmlns", "v", null, LayoutReader.Namespace);
            }
            output.Attribute("schemaVersion", document.SchemaVersion);
            output.Attribute("source", document.Source);
            output.Attributes(document.UnknownAttributes);
            output.Children();
            foreach (var view in document.Views.OrderBy(v => v.Id, StringComparer.Ordinal))
            {
                output.View(view);
            }

            output.Unknown(document.UnknownElements, 1);
            output.End(0);
            xml.WriteEndDocument();
        }

        return buffer.ToArray();
    }

    private sealed class Output(XmlWriter xml, bool canonical)
    {
        public void Start(string name, int depth)
        {
            Indent(depth);
            xml.WriteStartElement(canonical ? string.Empty : "v", name, LayoutReader.Namespace);
        }

        public void Attribute(string name, string? value)
        {
            if (value is not null)
            {
                xml.WriteAttributeString(name, value);
            }
        }

        public void Attribute(string name, int value) => Attribute(name, value.ToString(CultureInfo.InvariantCulture));

        private void Flag(string name, bool? value)
        {
            if (value.HasValue)
            {
                Attribute(name, value.Value ? "true" : "false");
            }
        }

        public void Attributes(IReadOnlyList<UnknownAttribute> attributes)
        {
            var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var attribute in attributes.OrderBy(a => a.Namespace, StringComparer.Ordinal)
                .ThenBy(a => a.LocalName, StringComparer.Ordinal))
            {
                var ns = attribute.Namespace;
                var prefix = string.Empty;
                if (ns == "http://www.w3.org/XML/1998/namespace")
                {
                    prefix = "xml";
                }
                else if (ns.Length != 0 && !prefixes.TryGetValue(ns, out prefix))
                {
                    prefix = "e" + (prefixes.Count + 1).ToString(CultureInfo.InvariantCulture);
                    prefixes.Add(ns, prefix);
                }

                xml.WriteAttributeString(prefix, attribute.LocalName, ns, attribute.Value);
            }
        }

        public void Children()
        {
            if (canonical)
            {
                xml.WriteWhitespace("\n");
            }
        }

        public void End(int depth)
        {
            Indent(depth);
            xml.WriteFullEndElement();
            Children();
        }

        private void EmptyEnd()
        {
            xml.WriteEndElement();
            Children();
        }

        private void Indent(int depth)
        {
            if (canonical)
            {
                xml.WriteWhitespace(new string(' ', depth * 2));
            }
        }

        public void Unknown(IReadOnlyList<UnknownElement> elements, int depth)
        {
            foreach (var element in elements)
            {
                Indent(depth);
                if (canonical)
                {
                    var content = XElement.Parse(element.Xml, LoadOptions.PreserveWhitespace);
                    if (content.Name != XName.Get(element.LocalName, element.Namespace))
                    {
                        throw new ArgumentException("An extension's XML does not match its recorded name.");
                    }

                    content.WriteTo(xml);
                }
                else
                {
                    xml.WriteRaw(element.Xml);
                }
                Children();
            }
        }

        public void View(LayoutView view)
        {
            Start("view", 1);
            Attribute("id", view.Id);
            Attribute("memberHint", view.MemberHint);
            Attribute("contextHint", view.ContextHint);
            var flow = view.Flow;
            if (flow is null && (view.Nodes.Any(n => n.Position.HasValue) ||
                view.Notes.Any(n => n.Position.HasValue) || view.EdgeRoutes.Any(r => r.Points.Count != 0)))
            {
                flow = FlowDirection.LeftToRight;
            }

            if (flow.HasValue)
            {
                Attribute("flow", flow.Value switch
                {
                    FlowDirection.LeftToRight => "left-to-right",
                    FlowDirection.TopToBottom => "top-to-bottom",
                    FlowDirection.RightToLeft => "right-to-left",
                    _ => flow.Value.ToString(),
                });
            }

            Attributes(view.UnknownAttributes);
            Children();
            foreach (var node in view.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
            {
                Node(node);
            }

            foreach (var note in view.Notes.OrderBy(n => n.Id, StringComparer.Ordinal))
            {
                Note(note);
            }

            foreach (var group in view.Groups.OrderBy(g => g.Id, StringComparer.Ordinal))
            {
                Group(group);
            }

            foreach (var route in view.EdgeRoutes.OrderBy(r => r.Id, StringComparer.Ordinal))
            {
                Route(route);
            }

            Unknown(view.UnknownElements, 2);
            End(1);
        }

        private void Node(LayoutNode node)
        {
            Start("node", 2);
            Attribute("id", node.Id);
            Attribute("syntaxKind", node.SyntaxKind);
            Flag("collapsed", node.Collapsed);
            Position(node.Position);
            Attributes(node.UnknownAttributes);
            Children();
            ArgumentNullException.ThrowIfNull(node.Anchor);
            Start("anchor", 3);
            Attribute("syntaxPathHint", node.Anchor.SyntaxPathHint);
            Attribute("memberSignature", node.Anchor.MemberSignature);
            Attribute("syntaxRole", node.Anchor.SyntaxRole);
            Attribute("fingerprint", node.Anchor.Fingerprint);
            Attributes(node.Anchor.UnknownAttributes);
            EmptyEnd();
            Extent(node.Extent);
            Unknown(node.UnknownElements, 3);
            End(2);
        }

        private void Note(LayoutNote note)
        {
            Start("note", 2);
            Attribute("id", note.Id);
            Position(note.Position);
            Attributes(note.UnknownAttributes);
            Children();
            Start("text", 3);
            if (note.Text.Length > 0 && string.IsNullOrWhiteSpace(note.Text))
            {
                xml.WriteAttributeString("xml", "space", null, "preserve");
            }

            xml.WriteString(note.Text);
            xml.WriteFullEndElement();
            Children();
            Extent(note.Extent);
            Unknown(note.UnknownElements, 3);
            End(2);
        }

        private void Group(PresentationGroup group)
        {
            Start("group", 2);
            Attribute("id", group.Id);
            Attribute("title", group.Title);
            Flag("collapsed", group.Collapsed);
            Attribute("note", group.Note);
            Attributes(group.UnknownAttributes);
            Children();
            foreach (var member in group.Members)
            {
                Start("member", 3);
                Attribute("node", member);
                EmptyEnd();
            }

            Unknown(group.UnknownElements, 3);
            End(2);
        }

        private void Route(EdgeRoute route)
        {
            Start("edgeRoute", 2);
            Attribute("id", route.Id);
            Attribute("from", route.From);
            Attribute("fromPort", route.FromPort);
            Attribute("to", route.To);
            Attribute("toPort", route.ToPort);
            Attributes(route.UnknownAttributes);
            Children();
            foreach (var point in route.Points)
            {
                Start("point", 3);
                Position(point);
                EmptyEnd();
            }

            Unknown(route.UnknownElements, 3);
            End(2);
        }

        private void Position(LayoutPoint? point)
        {
            if (point.HasValue)
            {
                Attribute("x", point.Value.X);
                Attribute("y", point.Value.Y);
            }
        }

        private void Extent(LayoutExtent? extent)
        {
            if (extent.HasValue)
            {
                Start("extent", 3);
                Attribute("along", extent.Value.Along);
                Attribute("across", extent.Value.Across);
                EmptyEnd();
            }
        }
    }
}
