using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using WeaveSharp.Layout;
using Xunit;

namespace WeaveSharp.Layout.Tests;

public sealed class LayoutWriterTests
{
    private static readonly XNamespace Visual = LayoutReader.Namespace;
    private static readonly Lazy<XmlSchemaSet> Schemas = new(() =>
    {
        var schemas = new XmlSchemaSet { XmlResolver = null };
        using var input = XmlReader.Create(Path.Combine(AppContext.BaseDirectory, "visual-1.xsd"),
            new XmlReaderSettings { XmlResolver = null });
        schemas.Add(LayoutReader.Namespace, input);
        schemas.Compile();
        return schemas;
    });

    private static VisualDocument Read(byte[] bytes)
    {
        var result = LayoutReader.Read(bytes);
        Assert.True(result.CanWrite, string.Join("; ", result.Problems.Select(p => p.Message)));
        return Assert.IsType<VisualDocument>(result.Document);
    }

    private static VisualDocument Read(string xml) => Read(Encoding.UTF8.GetBytes(xml));

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static VisualDocument Sample() => Read(Fixture("canonical-records.xml"));

    private static VisualDocument WithView(LayoutView view) => new(1, "Tiles.cs", [view], [], []);

    private static LayoutView EmptyView(string id = "tiles") => new(id, null, null, null, [], [], [], [], [], [], []);

    private static LayoutNode Node(string id = "chip") => new(id, "LiteralExpression", null, null, null,
        new SourceAnchor(null, null, "Value", null, []), [], []);

    private static LayoutNote Note(string id = "legend") => new(id, null, null, "Tile legend", [], []);

    private static PresentationGroup Group(string id = "tray") => new(id, "Tray", null, null, ["missing"], [], []);

    private static EdgeRoute Route(string id = "route") => new(id, "missing", "next", "also-missing", "previous", [new(4, 9)], [], []);

    private static List<string> SchemaErrors(byte[] bytes)
    {
        var errors = new List<string>();
        using var input = new MemoryStream(bytes);
        XDocument.Load(input).Validate(Schemas.Value, (_, error) => errors.Add(error.Message));
        return errors;
    }

    private static byte[] CheckedWrite(VisualDocument document)
    {
        var bytes = LayoutWriter.Write(document);
        var read = Read(bytes);
        Assert.Empty(SchemaErrors(bytes));
        Assert.Equal(bytes, LayoutWriter.Write(read));
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.EndsWith("\n", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        return bytes;
    }

    private static string Text(VisualDocument document) => Encoding.UTF8.GetString(CheckedWrite(document));

    private static void Refused(VisualDocument document, string reason)
    {
        var error = Assert.Throws<ArgumentException>(() => LayoutWriter.Write(document));
        Assert.Equal("document", error.ParamName);
        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("canonical-empty.xml")]
    [InlineData("canonical-records.xml")]
    [InlineData("canonical-extensions.xml")]
    public void CanonicalFilesKeepTheirBytesAndEveryModelField(string name)
    {
        var expected = Fixture(name);
        var before = Read(expected);
        var actual = CheckedWrite(before);
        Assert.Equal(expected, actual);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(Read(actual)));
    }

    [Fact]
    public void MovingAPlacedNodeChangesExactlyItsOpeningLine()
    {
        var document = Sample();
        var view = document.Views[0];
        var moved = view.Nodes[0] with { Position = new LayoutPoint(-31, 82) };
        var updated = document with { Views = [view with { Nodes = [moved, view.Nodes[1]] }, document.Views[1]] };
        var before = Text(document).Split('\n');
        var after = Text(updated).Split('\n');
        Assert.Equal(before.Length, after.Length);
        var changed = Assert.Single(before.Zip(after), pair => pair.First != pair.Second);
        Assert.Contains("<node id=\"load\"", changed.First, StringComparison.Ordinal);
        Assert.Equal(changed.First.Replace("x=\"-12\" y=\"45\"", "x=\"-31\" y=\"82\"", StringComparison.Ordinal), changed.Second);
    }

    [Theory]
    [InlineData("node")]
    [InlineData("note")]
    [InlineData("group")]
    [InlineData("edgeRoute")]
    [InlineData("view")]
    public void AddingOrRemovingARecordOnlyChangesItsBlock(string kind)
    {
        var document = Sample();
        var view = document.Views[0];
        const string id = "new-record";
        var updated = kind switch
        {
            "node" => document with { Views = [view with { Nodes = [.. view.Nodes, Node(id)] }, document.Views[1]] },
            "note" => document with { Views = [view with { Notes = [.. view.Notes, Note(id)] }, document.Views[1]] },
            "group" => document with { Views = [view with { Groups = [.. view.Groups, Group(id)] }, document.Views[1]] },
            "edgeRoute" => document with { Views = [view with { EdgeRoutes = [.. view.EdgeRoutes, Route(id)] }, document.Views[1]] },
            _ => document with { Views = [.. document.Views, EmptyView(id)] },
        };
        Assert.Equal(Text(document), RemoveBlock(Text(updated), kind, id));
    }

    [Theory]
    [InlineData("node")]
    [InlineData("note")]
    [InlineData("group")]
    [InlineData("edgeRoute")]
    public void FirstAndLastRecordsDoNotReformatTheirView(string kind)
    {
        var view = EmptyView() with { Flow = FlowDirection.LeftToRight };
        var populated = kind switch
        {
            "node" => view with { Nodes = [Node()] },
            "note" => view with { Notes = [Note("chip")] },
            "group" => view with { Groups = [Group("chip")] },
            _ => view with { EdgeRoutes = [Route("chip")] },
        };
        Assert.Equal(Text(WithView(view)), RemoveBlock(Text(WithView(populated)), kind, "chip"));
    }

    [Fact]
    public void FirstAndLastViewsDoNotReformatTheirDocument()
    {
        var empty = new VisualDocument(1, "Tiles.cs", [], [], []);
        Assert.Equal(Text(empty), RemoveBlock(Text(empty with { Views = [EmptyView()] }), "view", "tiles"));
    }

    private static string RemoveBlock(string xml, string name, string id)
    {
        var lines = xml.Split('\n').ToList();
        var first = lines.FindIndex(line => line.TrimStart().StartsWith($"<{name} id=\"{id}\"", StringComparison.Ordinal));
        Assert.True(first >= 0);
        var last = lines.FindIndex(first, line => line.Trim() == $"</{name}>");
        Assert.True(last > first);
        lines.RemoveRange(first, last - first + 1);
        return string.Join('\n', lines);
    }

    [Fact]
    public void RecordOrderingIsOrdinalAndDoesNotDependOnCultureOrInputOrder()
    {
        var view = EmptyView() with
        {
            Nodes = [Node("z"), Node("I"), Node("i"), Node("A")],
            Notes = [Note("zz"), Note("AA")],
            Groups = [Group("g-z"), Group("g-A")],
            EdgeRoutes = [Route("r-z"), Route("r-A")],
        };
        var document = WithView(view) with { Views = [view, EmptyView("A")] };
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var first = CheckedWrite(document);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var reversed = document with { Views = [document.Views[1], view with
            {
                Nodes = view.Nodes.Reverse().ToArray(), Notes = view.Notes.Reverse().ToArray(),
                Groups = view.Groups.Reverse().ToArray(), EdgeRoutes = view.EdgeRoutes.Reverse().ToArray(),
            }] };
            Assert.Equal(first, CheckedWrite(reversed));
            var written = Read(first);
            Assert.Equal(new[] { "A", "tiles" }, written.Views.Select(v => v.Id));
            Assert.Equal(new[] { "A", "I", "i", "z" }, written.Views[1].Nodes.Select(n => n.Id));
            var children = XDocument.Parse(Encoding.UTF8.GetString(first)).Root!.Elements().Last().Elements();
            Assert.Equal(new[] { "node", "node", "node", "node", "note", "note", "group", "group", "edgeRoute", "edgeRoute" },
                children.Select(e => e.Name.LocalName));
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
        }
    }

    [Fact]
    public void ExtensionAttributesSortLocallyWithoutRenamingOtherRecords()
    {
        UnknownAttribute[] attributes = [new("urn:zeta", "z", "3"), new("urn:alpha", "b", "2"), new("", "future", "1"), new("urn:alpha", "a", "0")];
        var node = Node() with { UnknownAttributes = attributes };
        var document = WithView(EmptyView() with { Nodes = [node] });
        var first = Text(document);
        Assert.Equal(first, Text(WithView(EmptyView() with { Nodes = [node with { UnknownAttributes = attributes.Reverse().ToArray() }] })));
        var start = first.Split('\n').Single(line => line.Contains("<node ", StringComparison.Ordinal));
        Assert.Contains("syntaxKind=\"LiteralExpression\" future=\"1\" e1:a=\"0\" e1:b=\"2\" e2:z=\"3\"", start, StringComparison.Ordinal);
        var added = Node("a-new") with { UnknownAttributes = [new("urn:another", "shade", "violet")] };
        Assert.Equal(first, RemoveBlock(Text(WithView(EmptyView() with { Nodes = [added, node] })), "node", "a-new"));
    }

    [Fact]
    public void ExtensionsOnEverySupportedRecordAndAnchorSurvive()
    {
        var attribute = new UnknownAttribute("urn:tiles:extra", "mark", "kept");
        var unknown = Read("""
            <visualDocument xmlns="urn:weavesharp:visual:1" schemaVersion="1" source="Tiles.cs">
              <extra xmlns="urn:tiles:extra" order="2"><nested />tail</extra>
              <extra xmlns="urn:tiles:extra" order="1" />
            </visualDocument>
            """).UnknownElements;
        var node = Node() with { UnknownAttributes = [attribute], UnknownElements = unknown,
            Anchor = Node().Anchor with { UnknownAttributes = [attribute] } };
        var view = EmptyView() with
        {
            Flow = FlowDirection.TopToBottom,
            Nodes = [node], Notes = [Note() with { UnknownAttributes = [attribute], UnknownElements = unknown }],
            Groups = [Group() with { UnknownAttributes = [attribute], UnknownElements = unknown }],
            EdgeRoutes = [Route() with { UnknownAttributes = [attribute], UnknownElements = unknown }],
            UnknownAttributes = [attribute], UnknownElements = unknown,
        };
        var document = WithView(view) with { UnknownAttributes = [attribute], UnknownElements = unknown };
        var written = Read(CheckedWrite(document));
        Assert.Equal(document.UnknownAttributes, written.UnknownAttributes);
        Assert.Equal(unknown, written.UnknownElements);
        var result = Assert.Single(written.Views);
        Assert.Equal(view.UnknownAttributes, result.UnknownAttributes);
        Assert.Equal(unknown, result.UnknownElements);
        Assert.Equal(node.UnknownAttributes, result.Nodes[0].UnknownAttributes);
        Assert.Equal(node.Anchor.UnknownAttributes, result.Nodes[0].Anchor.UnknownAttributes);
        Assert.Equal(unknown, result.Nodes[0].UnknownElements);
        Assert.Equal(view.Notes[0].UnknownAttributes, result.Notes[0].UnknownAttributes);
        Assert.Equal(unknown, result.Notes[0].UnknownElements);
        Assert.Equal(view.Groups[0].UnknownAttributes, result.Groups[0].UnknownAttributes);
        Assert.Equal(unknown, result.Groups[0].UnknownElements);
        Assert.Equal(view.EdgeRoutes[0].UnknownAttributes, result.EdgeRoutes[0].UnknownAttributes);
        Assert.Equal(unknown, result.EdgeRoutes[0].UnknownElements);
    }

    [Fact]
    public void OrphansRemainRecordsAndReferenceOrderIsPreserved()
    {
        var original = Sample();
        var view = Read(CheckedWrite(original)).Views[0];
        Assert.Equal(original.Views[0].Groups[0].Members, view.Groups[0].Members);
        Assert.Equal(original.Views[0].EdgeRoutes[0].Points, view.EdgeRoutes[0].Points);
        Assert.Equal(original.Views[0].Orphans, view.Orphans);
        Assert.Equal(new[] { new OrphanedReference("selection", "retired"), new("retired-link", "absent"), new("retired-link", "retired") }, view.Orphans);
        var withoutNode = original with { Views = [original.Views[0] with { Nodes = [original.Views[0].Nodes[1]] }, original.Views[1]] };
        var afterRemoval = Read(CheckedWrite(withoutNode)).Views[0];
        Assert.Equal(view.Groups[0].Members, afterRemoval.Groups[0].Members);
        Assert.Equal(view.EdgeRoutes.Count, afterRemoval.EdgeRoutes.Count);
        Assert.Contains(new OrphanedReference("selection", "load"), afterRemoval.Orphans);
        Assert.Equal(Text(withoutNode), RemoveBlock(Text(original), "node", "load"));
    }

    [Fact]
    public void MissingPositionsAndExtentsAreNeverInvented()
    {
        var document = WithView(EmptyView() with { Nodes = [Node()], Notes = [Note()], Groups = [Group()] });
        var xml = XDocument.Parse(Text(document));
        Assert.DoesNotContain(xml.Descendants().Attributes(), a => a.Name.LocalName is "x" or "y" or "flow");
        Assert.Empty(xml.Descendants(Visual + "extent"));
        Assert.Empty(xml.Descendants(Visual + "edgeRoute"));
        var resized = WithView(EmptyView() with { Nodes = [Node() with { Extent = new LayoutExtent(1, 100000) }] });
        var result = Read(CheckedWrite(resized)).Views[0];
        Assert.Equal(new LayoutExtent(1, 100000), result.Nodes[0].Extent);
        Assert.Null(result.Nodes[0].Position);
        Assert.Null(result.Flow);
    }

    [Theory]
    [InlineData("node")]
    [InlineData("note")]
    [InlineData("edgeRoute")]
    public void EachKindOfPositionRequiresAnExplicitFlow(string kind)
    {
        var view = kind switch
        {
            "node" => EmptyView() with { Nodes = [Node() with { Position = new LayoutPoint(-1000000, 1000000) }] },
            "note" => EmptyView() with { Notes = [Note() with { Position = new LayoutPoint(0, 0) }] },
            _ => EmptyView() with { EdgeRoutes = [Route()] },
        };
        Assert.Equal(FlowDirection.LeftToRight, Read(CheckedWrite(WithView(view))).Views[0].Flow);
        foreach (var flow in Enum.GetValues<FlowDirection>())
        {
            Assert.Equal(flow, Read(CheckedWrite(WithView(view with { Flow = flow }))).Views[0].Flow);
        }

        Assert.Null(view.Flow);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\n\r ")]
    [InlineData("café <tile> & \"label\"\r\nnext\tline")]
    public void TextAndAttributeValuesAreEscapedWithoutLosingCharacters(string value)
    {
        var view = EmptyView() with { MemberHint = value, Notes = [Note() with { Text = value }] };
        var written = Read(CheckedWrite(WithView(view))).Views[0];
        Assert.Equal(value, written.MemberHint);
        Assert.Equal(value, written.Notes[0].Text);
    }

    [Fact]
    public void MultilineGroupTitlesAreSchemaCompatible()
    {
        var document = WithView(EmptyView() with { Groups = [Group() with { Title = "Tray\r\nlabels\tand colors" }] });
        Assert.Equal(document.Views[0].Groups[0].Title, Read(CheckedWrite(document)).Views[0].Groups[0].Title);
    }

    [Fact]
    public void CommentsAlreadyOmittedByTheReaderAreNotRecreated()
    {
        var document = Read("""<visualDocument xmlns="urn:weavesharp:visual:1" schemaVersion="1" source="Tiles.cs"><!-- annotation --><view id="tiles" /></visualDocument>""");
        Assert.DoesNotContain("<!--", Text(document), StringComparison.Ordinal);
    }

    [Fact]
    public void NullDocumentsAreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => LayoutWriter.Write(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnsupportedVersionsAreRefused(int version) => Refused(Sample() with { SchemaVersion = version }, "SchemaVersion");

    [Theory]
    [InlineData("")]
    [InlineData("/Tiles.cs")]
    [InlineData("../Tiles.cs")]
    [InlineData("part/../Tiles.cs")]
    [InlineData("./Tiles.cs")]
    [InlineData("D:/Tiles.cs")]
    [InlineData("file:Tiles.cs")]
    [InlineData("part\\Tiles.cs")]
    [InlineData("part//Tiles.cs")]
    [InlineData("part/\nTiles.cs")]
    public void InvalidSourcePathsAreRefused(string source) => Refused(Sample() with { Source = source }, "InvalidRecord");

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("_first")]
    public void InvalidIdentifiersAreRefusedInEveryScope(string id)
    {
        Refused(WithView(EmptyView(id)), "InvalidRecord");
        Refused(WithView(EmptyView() with { Nodes = [Node(id)] }), "InvalidRecord");
        Refused(WithView(EmptyView() with { Notes = [Note(id)] }), "InvalidRecord");
        Refused(WithView(EmptyView() with { Groups = [Group(id)] }), "InvalidRecord");
        Refused(WithView(EmptyView() with { EdgeRoutes = [Route(id)] }), "InvalidRecord");
    }

    [Fact]
    public void OverlongIdentifiersAndDuplicateIdentifiersAreRefused()
    {
        Refused(WithView(EmptyView(new string('v', 65))), "InvalidRecord");
        Refused(Sample() with { Views = [EmptyView(), EmptyView()] }, "DuplicateId");
        Refused(WithView(EmptyView() with { Nodes = [Node(), Node()] }), "DuplicateId");
        Refused(WithView(EmptyView() with { Notes = [Note(), Note()] }), "DuplicateId");
        Refused(WithView(EmptyView() with { Groups = [Group(), Group()] }), "DuplicateId");
        Refused(WithView(EmptyView() with { EdgeRoutes = [Route(), Route()] }), "DuplicateId");
        Refused(WithView(EmptyView() with { Nodes = [Node()], Notes = [Note("chip")] }), "DuplicateId");
    }

    [Theory]
    [InlineData(-1000001)]
    [InlineData(1000001)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void CoordinatesOutsideTheDefaultBoundsAreRefused(int value)
    {
        foreach (var point in new[] { new LayoutPoint(value, 0), new LayoutPoint(0, value) })
        {
            Refused(WithView(EmptyView() with { Nodes = [Node() with { Position = point }] }), "InvalidRecord");
            Refused(WithView(EmptyView() with { Notes = [Note() with { Position = point }] }), "InvalidRecord");
            Refused(WithView(EmptyView() with { EdgeRoutes = [Route() with { Points = [point] }] }), "InvalidRecord");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100001)]
    [InlineData(int.MaxValue)]
    public void ExtentsOutsideTheDefaultBoundsAreRefused(int value)
    {
        foreach (var extent in new[] { new LayoutExtent(value, 1), new LayoutExtent(1, value) })
        {
            Refused(WithView(EmptyView() with { Nodes = [Node() with { Extent = extent }] }), "InvalidRecord");
            Refused(WithView(EmptyView() with { Notes = [Note() with { Extent = extent }] }), "InvalidRecord");
        }
    }

    [Fact]
    public void IncompleteOrMalformedRecordsAreRefused()
    {
        Refused(WithView(EmptyView() with { Flow = (FlowDirection)37 }), "InvalidRecord");
        Refused(WithView(EmptyView() with { Nodes = [Node() with { SyntaxKind = "bad-kind" }] }), "InvalidRecord");
        Refused(WithView(EmptyView() with { Nodes = [Node() with { Anchor = new(null, null, null, null, []) }] }), "InvalidRecord");
        Refused(WithView(EmptyView() with { Nodes = [Node() with { Anchor = null! }] }), "serialized");
        Refused(WithView(EmptyView() with { Groups = [Group() with { Title = " " }] }), "InvalidRecord");
        Refused(WithView(EmptyView() with { Groups = [Group() with { Members = [] }] }), "InvalidRecord");
        Refused(WithView(EmptyView() with { Groups = [Group() with { Members = ["bad ref"] }] }), "InvalidRecord");
        Refused(WithView(EmptyView() with { EdgeRoutes = [Route() with { Points = [] }] }), "InvalidRecord");
        Refused(WithView(EmptyView() with { EdgeRoutes = [Route() with { From = "bad ref" }] }), "InvalidRecord");
        Refused(WithView(EmptyView() with { EdgeRoutes = [Route() with { To = "bad ref" }] }), "InvalidRecord");
        Refused(WithView(EmptyView() with { EdgeRoutes = [Route() with { FromPort = "bad role" }] }), "InvalidRecord");
        Refused(WithView(EmptyView() with { EdgeRoutes = [Route() with { ToPort = "bad role" }] }), "InvalidRecord");
    }

    [Fact]
    public void InvalidXmlAndCollidingAttributesAreRefused()
    {
        Refused(WithView(EmptyView() with { Notes = [Note() with { Text = "\0" }] }), "serialized");
        Refused(WithView(EmptyView()) with { UnknownElements = [new("urn:tiles:extra", "extra", "<extra")] }, "NotWellFormed");
        Refused(WithView(EmptyView()) with { UnknownAttributes = [new("", "source", "Another.cs")] }, "serialized");
        Refused(WithView(EmptyView()) with { UnknownElements = [new("", "wrong", "<actual />")] }, "recorded name");
    }

    [Fact]
    public void TheFinalReadEnforcesTheDefaultSizeLimits()
    {
        Refused(WithView(EmptyView() with { MemberHint = new string('x', 4097) }), "ValueTooLong");
        Refused(WithView(EmptyView() with { Notes = [Note() with { Text = new string('x', 16385) }] }), "ValueTooLong");
        Refused(WithView(EmptyView()) with { UnknownAttributes = Enumerable.Range(0, 31).Select(i => new UnknownAttribute("", "a" + i, "v")).ToArray() }, "TooManyAttributes");
        var manyNotes = Enumerable.Range(0, 270).Select(i => Note("n" + i) with { Text = new string('x', 16000) }).ToArray();
        Refused(WithView(EmptyView() with { Notes = manyNotes }), "TooLarge");
    }

    [Fact]
    public void CanonicalBytesAtTheSizeLimitRoundTripAndOneMoreByteIsRefused()
    {
        var notes = Enumerable.Range(0, 256).Select(i => Note("n" + i) with { Text = string.Empty }).ToArray();
        var shell = WithView(EmptyView() with { Notes = notes });
        var padding = LayoutReaderLimits.Default.MaxBytes - LayoutWriter.Write(shell).Length;
        for (var i = 0; i < notes.Length; i++)
        {
            notes[i] = notes[i] with { Text = new string('x', padding / notes.Length + (i < padding % notes.Length ? 1 : 0)) };
        }

        Assert.Equal(LayoutReaderLimits.Default.MaxBytes, CheckedWrite(shell).Length);
        notes[0] = notes[0] with { Text = notes[0].Text + "x" };
        var error = Assert.Throws<ArgumentException>(() => LayoutWriter.Write(shell));
        Assert.StartsWith("The serialized layout was Refused.", error.Message, StringComparison.Ordinal);
        Assert.Contains("TooLarge", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\" />")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\"><anchor syntaxRole=\"Value\" /><anchor syntaxRole=\"Value\" /></node>")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\"><anchor syntaxRole=\"Value\" /><extent along=\"1\" across=\"1\" /><extent along=\"1\" across=\"1\" /></node>")]
    [InlineData("<note id=\"n\" />")]
    [InlineData("<note id=\"n\"><text /><text /></note>")]
    [InlineData("<group id=\"g\" title=\"Tray\" />")]
    [InlineData("<edgeRoute id=\"r\" from=\"a\" fromPort=\"out\" to=\"b\" toPort=\"in\" />")]
    [InlineData("<note id=\"n\"><text /></note><node id=\"n\" syntaxKind=\"Block\"><anchor syntaxRole=\"Value\" /></node>")]
    public void SchemaRejectsMissingChildrenExcessChildrenAndDuplicateIds(string records)
    {
        var bytes = Encoding.UTF8.GetBytes($"<visualDocument xmlns=\"{Visual}\" schemaVersion=\"1\" source=\"Tiles.cs\"><view id=\"tiles\">{records}</view></visualDocument>");
        Assert.NotEmpty(SchemaErrors(bytes));
        Assert.False(LayoutReader.Read(bytes).CanWrite);
    }

    [Fact]
    public void SchemaAcceptsReaderChildOrdersAndForeignInterleaving()
    {
        var bytes = Encoding.UTF8.GetBytes("""
            <visualDocument xmlns="urn:weavesharp:visual:1" xmlns:t="urn:tiles:extra" schemaVersion="1" source="Tiles.cs">
              <t:extra /><view id="tiles"><t:extra />
                <note id="n"><t:extra /><extent along="1" across="1" /><t:extra /><text>Tile</text><t:extra /></note>
                <node id="c" syntaxKind="Block"><t:extra /><extent along="1" across="1" /><t:extra /><anchor syntaxRole="Value" /><t:extra /></node>
                <group id="g" title="Tray"><t:extra /><member node="c" /><t:extra /><member node="missing" /><t:extra /></group>
                <edgeRoute id="r" from="c" fromPort="out" to="missing" toPort="in"><t:extra /><point x="0" y="0" /><t:extra /><point x="1" y="1" /><t:extra /></edgeRoute>
                <t:extra />
              </view><t:extra />
            </visualDocument>
            """);
        Assert.Empty(SchemaErrors(bytes));
        CheckedWrite(Read(bytes));
    }

    [Theory]
    [InlineData("<node id=\"_bad\" syntaxKind=\"Block\"><anchor syntaxRole=\"Value\" /></node>")]
    [InlineData("<node id=\"c\" syntaxKind=\"bad-kind\"><anchor syntaxRole=\"Value\" /></node>")]
    [InlineData("<node id=\"c\" syntaxKind=\"Block\" collapsed=\"1\"><anchor syntaxRole=\"Value\" /></node>")]
    [InlineData("<node id=\"c\" syntaxKind=\"Block\" x=\"1.5\" y=\"0\"><anchor syntaxRole=\"Value\" /></node>")]
    [InlineData("<node id=\"c\" syntaxKind=\"Block\" x=\"1000001\" y=\"0\"><anchor syntaxRole=\"Value\" /></node>")]
    [InlineData("<node id=\"c\" syntaxKind=\"Block\"><anchor syntaxRole=\"Value\" /><extent along=\"0\" across=\"5\" /></node>")]
    [InlineData("<node id=\"c\" syntaxKind=\"Block\"><anchor syntaxRole=\"Value\" /><extent along=\"5\" across=\"100001\" /></node>")]
    [InlineData("<group id=\"g\" title=\" \t\"><member node=\"missing\" /></group>")]
    [InlineData("<edgeRoute id=\"r\" from=\"missing\" fromPort=\"bad role\" to=\"absent\" toPort=\"in\"><point x=\"0\" y=\"0\" /></edgeRoute>")]
    public void SchemaRejectsInvalidKnownAttributeValues(string records)
    {
        var bytes = Encoding.UTF8.GetBytes($"<visualDocument xmlns=\"{Visual}\" schemaVersion=\"1\" source=\"Tiles.cs\"><view id=\"tiles\">{records}</view></visualDocument>");
        Assert.NotEmpty(SchemaErrors(bytes));
        Assert.False(LayoutReader.Read(bytes).CanWrite);
    }

    [Theory]
    [InlineData("<visualDocument xmlns=\"urn:weavesharp:visual:1\" schemaVersion=\"2\" source=\"Tiles.cs\" />")]
    [InlineData("<visualDocument xmlns=\"urn:weavesharp:visual:1\" schemaVersion=\"1\" />")]
    [InlineData("<visualDocument xmlns=\"urn:weavesharp:visual:1\" schemaVersion=\"1\" source=\"Tiles.cs\"><view id=\"v\" /><view id=\"v\" /></visualDocument>")]
    [InlineData("<visualDocument xmlns=\"urn:weavesharp:visual:1\" schemaVersion=\"1\" source=\"Tiles.cs\"><view id=\"v\" flow=\"diagonal\" /></visualDocument>")]
    public void SchemaRejectsInvalidDocumentsAndViews(string xml)
    {
        var bytes = Encoding.UTF8.GetBytes(xml);
        Assert.NotEmpty(SchemaErrors(bytes));
        Assert.False(LayoutReader.Read(bytes).CanWrite);
    }

    [Fact]
    public void ReaderExtensionsOutsideTheXsdNamespacePolicyAreStillPreserved()
    {
        var original = Read("""
            <visualDocument xmlns="urn:weavesharp:visual:1" schemaVersion="1" source="Tiles.cs">
              <view id="tiles" futureFlag="on"><futureRecord value="4" /><localRecord xmlns="" /><node xmlns="" hint="extension" /></view>
            </visualDocument>
            """);
        var bytes = LayoutWriter.Write(original);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(Read(bytes)));
        Assert.Equal(bytes, LayoutWriter.Write(Read(bytes)));
        Assert.NotEmpty(SchemaErrors(bytes));
    }

    [Fact]
    public void UnqualifiedExtensionsKeepTheirNamesWithoutBecomingLayoutRecords()
    {
        var original = Read("""
            <visualDocument xmlns="urn:weavesharp:visual:1" schemaVersion="1" source="Tiles.cs">
              <view id="tiles">
                <node id="chip" syntaxKind="Block"><anchor syntaxRole="Value" /><anchor xmlns="" hint="extra" /></node>
                <node xmlns="" hint="extension" />
              </view>
              <view xmlns="" hint="extension" />
            </visualDocument>
            """);
        var bytes = CheckedWrite(original);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(Read(bytes)));
    }

    [Fact]
    public void ReaderLeafExtensionLossIsVisibleAsAnUpstreamModelLimitation()
    {
        var original = Read("""
            <visualDocument xmlns="urn:weavesharp:visual:1" xmlns:t="urn:tiles:extra" schemaVersion="1" source="Tiles.cs">
              <view id="tiles">
                <node id="c" syntaxKind="Block"><anchor syntaxRole="Value" /><extent along="10" across="10" t:leaf="extent" /></node>
                <note id="n"><text t:leaf="text">Tile</text></note>
                <group id="g" title="Tray"><member node="c" t:leaf="member" /></group>
                <edgeRoute id="r" from="c" fromPort="out" to="missing" toPort="in"><point x="0" y="0" t:leaf="point" /></edgeRoute>
              </view>
            </visualDocument>
            """);
        Assert.DoesNotContain("leaf", Text(original), StringComparison.Ordinal);
    }
}
