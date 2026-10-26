using System;
using System.IO;
using System.Linq;
using System.Text;
using WeaveSharp.Layout;
using Xunit;

namespace WeaveSharp.Layout.Tests;

public sealed class LayoutReaderTests
{
    private const string Valid = """
        <?xml version="1.0" encoding="UTF-8"?>
        <visualDocument xmlns="urn:weavesharp:visual:1" source="Orders/OrderService.cs" schemaVersion="1">
          <view id="submit-order" flow="top-to-bottom" memberHint="OrderService.SubmitAsync">
            <node id="validate" syntaxKind="InvocationExpression" x="0" y="120" collapsed="true">
              <anchor syntaxRole="Statement" memberSignature="OrderService.SubmitAsync(Order)" />
            </node>
            <node id="persist" syntaxKind="AwaitExpression">
              <anchor fingerprint="persist-await" />
              <extent across="64" along="180" />
            </node>
            <note id="ordering" y="300" x="-40">
              <text>Validation runs before anything is stored.</text>
              <extent across="48" along="200" />
            </note>
            <group id="pipeline" collapsed="false" title="Pipeline" note="Validate, then persist.">
              <member node="validate" />
              <member node="persist" />
            </group>
            <edgeRoute id="validate-persist" from="validate" fromPort="next" to="persist" toPort="previous">
              <point x="60" y="180" />
              <point x="60" y="240" />
            </edgeRoute>
          </view>
        </visualDocument>
        """;

    private static LayoutReadResult Read(string xml, LayoutReaderLimits? limits = null) =>
        LayoutReader.Read(Encoding.UTF8.GetBytes(xml), limits);

    private static string Document(string viewContent, string viewAttributes = "") =>
        $"""<visualDocument xmlns="urn:weavesharp:visual:1" schemaVersion="1" source="A.cs"><view id="v"{viewAttributes}>{viewContent}</view></visualDocument>""";

    private static void AssertRefused(LayoutReadResult result, LayoutProblemKind kind)
    {
        Assert.Equal(LayoutReadStatus.Refused, result.Status);
        Assert.Null(result.Document);
        Assert.False(result.CanWrite);
        Assert.Contains(result.Problems, p => p.Kind == kind);
    }

    private static void AssertDamaged(LayoutReadResult result, LayoutProblemKind kind)
    {
        Assert.Equal(LayoutReadStatus.Damaged, result.Status);
        Assert.NotNull(result.Document);
        Assert.False(result.CanWrite);
        Assert.Contains(result.Problems, p => p.Kind == kind);
    }

    [Fact]
    public void AValidDocumentIsReadWhole()
    {
        var result = Read(Valid);

        Assert.Equal(LayoutReadStatus.Valid, result.Status);
        Assert.True(result.CanWrite);
        Assert.Empty(result.Problems);
        var document = Assert.IsType<VisualDocument>(result.Document);
        Assert.Equal(1, document.SchemaVersion);
        Assert.Equal("Orders/OrderService.cs", document.Source);
        var view = Assert.Single(document.Views);
        Assert.Equal(FlowDirection.TopToBottom, view.Flow);
        Assert.Equal(2, view.Nodes.Count);
        Assert.Equal(new LayoutPoint(0, 120), view.Nodes[0].Position);
        Assert.Equal(true, view.Nodes[0].Collapsed);
        Assert.Null(view.Nodes[1].Position);
        Assert.Equal(new LayoutExtent(180, 64), view.Nodes[1].Extent);
        Assert.Equal("OrderService.SubmitAsync(Order)", view.Nodes[0].Anchor.MemberSignature);
        Assert.Equal("persist-await", view.Nodes[1].Anchor.Fingerprint);
        Assert.Equal("Validation runs before anything is stored.", Assert.Single(view.Notes).Text);
        Assert.Equal(new LayoutPoint(-40, 300), view.Notes[0].Position);
        Assert.Equal(new[] { "validate", "persist" }, Assert.Single(view.Groups).Members);
        Assert.Equal(2, Assert.Single(view.EdgeRoutes).Points.Count);
        Assert.Empty(view.Orphans);
    }

    [Fact]
    public void AByteOrderMarkIsAccepted()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Valid)).ToArray();
        Assert.Equal(LayoutReadStatus.Valid, LayoutReader.Read(bytes).Status);
    }

    [Fact]
    public void AnExternalEntityIsRefusedWithoutBeingResolved()
    {
        var xml = """
            <?xml version="1.0"?>
            <!DOCTYPE visualDocument [ <!ENTITY secret SYSTEM "file:///etc/passwd"> ]>
            <visualDocument xmlns="urn:weavesharp:visual:1" schemaVersion="1" source="A.cs"><view id="v" memberHint="&secret;" /></visualDocument>
            """;
        AssertRefused(Read(xml), LayoutProblemKind.DocumentType);
    }

    [Fact]
    public void InternalEntityExpansionIsRefused()
    {
        var xml = """
            <?xml version="1.0"?>
            <!DOCTYPE lol [ <!ENTITY a "aaaaaaaaaa"> <!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;"> <!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;"> ]>
            <visualDocument xmlns="urn:weavesharp:visual:1" schemaVersion="1" source="A.cs"><view id="v" memberHint="&c;" /></visualDocument>
            """;
        AssertRefused(Read(xml), LayoutProblemKind.DocumentType);
    }

    [Fact]
    public void AnyDocumentTypeDeclarationIsRefused()
    {
        var xml = """<!DOCTYPE visualDocument><visualDocument xmlns="urn:weavesharp:visual:1" schemaVersion="1" source="A.cs" />""";
        AssertRefused(Read(xml), LayoutProblemKind.DocumentType);
    }

    [Fact]
    public void AProcessingInstructionIsRefused()
    {
        var xml = """<?xml-stylesheet href="https://example.invalid/x.xsl"?><visualDocument xmlns="urn:weavesharp:visual:1" schemaVersion="1" source="A.cs" />""";
        AssertRefused(Read(xml), LayoutProblemKind.ProcessingInstruction);
    }

    [Fact]
    public void Utf16IsRefused()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Valid)).ToArray();
        AssertRefused(LayoutReader.Read(bytes), LayoutProblemKind.Encoding);
        AssertRefused(LayoutReader.Read(Encoding.Unicode.GetBytes(Valid)), LayoutProblemKind.Encoding);
    }

    [Fact]
    public void InvalidUtf8IsRefused()
    {
        var bytes = Encoding.UTF8.GetBytes(Valid);
        bytes[100] = 0xC3;
        bytes[101] = 0x28;
        AssertRefused(LayoutReader.Read(bytes), LayoutProblemKind.Encoding);
    }

    [Fact]
    public void ADeclaredEncodingOtherThanUtf8IsRefused()
    {
        var xml = """<?xml version="1.0" encoding="iso-8859-1"?><visualDocument xmlns="urn:weavesharp:visual:1" schemaVersion="1" source="A.cs" />""";
        AssertRefused(Read(xml), LayoutProblemKind.Encoding);
    }

    [Fact]
    public void OversizedInputIsRefusedBeforeParsing()
    {
        var limits = new LayoutReaderLimits { MaxBytes = 64 };
        AssertRefused(Read(Valid, limits), LayoutProblemKind.TooLarge);
    }

    [Fact]
    public void AnUnboundedStreamIsReadNoFurtherThanTheLimit()
    {
        var limits = new LayoutReaderLimits { MaxBytes = 1000 };
        var stream = new EndlessStream();

        AssertRefused(LayoutReader.Read(stream, limits), LayoutProblemKind.TooLarge);
        Assert.True(stream.BytesRead <= limits.MaxBytes + 1, $"read {stream.BytesRead} bytes");
    }

    [Fact]
    public void AStreamWithinTheLimitIsRead()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Valid));
        Assert.Equal(LayoutReadStatus.Valid, LayoutReader.Read(stream).Status);
    }

    [Fact]
    public void ExcessDepthIsRefused()
    {
        var nested = string.Concat(Enumerable.Repeat("<x>", 20)) + string.Concat(Enumerable.Repeat("</x>", 20));
        AssertRefused(Read(Document($"<node id=\"n\" syntaxKind=\"Block\"><anchor syntaxRole=\"r\" />{nested}</node>")), LayoutProblemKind.TooDeep);
    }

    [Fact]
    public void TooManyElementsAreRefused()
    {
        var limits = new LayoutReaderLimits { MaxElements = 10 };
        var notes = string.Concat(Enumerable.Range(0, 10).Select(i => $"<note id=\"n{i}\"><text>t</text></note>"));
        AssertRefused(Read(Document(notes), limits), LayoutProblemKind.TooManyElements);
    }

    [Fact]
    public void TooManyAttributesAreRefused()
    {
        var attributes = string.Concat(Enumerable.Range(0, 40).Select(i => $" extra{i}=\"v\""));
        AssertRefused(Read(Document(string.Empty, attributes)), LayoutProblemKind.TooManyAttributes);
    }

    [Fact]
    public void AnOverlongAttributeIsRefused()
    {
        AssertRefused(Read(Document(string.Empty, $" memberHint=\"{new string('a', 5000)}\"")), LayoutProblemKind.ValueTooLong);
    }

    [Fact]
    public void OverlongTextIsRefused()
    {
        AssertRefused(Read(Document($"<note id=\"n\"><text>{new string('a', 20_000)}</text></note>")), LayoutProblemKind.ValueTooLong);
    }

    [Theory]
    [InlineData("<visualDocument xmlns=\"urn:weavesharp:visual:1\" schemaVersion=\"1\" source=\"A.cs\">")]
    [InlineData("<visualDocument xmlns=\"urn:weavesharp:visual:1\" schemaVersion=\"1\" source=\"A.cs\"><view id=\"v\" x=\"&#0;\" /></visualDocument>")]
    [InlineData("")]
    [InlineData("not xml")]
    public void MalformedInputIsRefused(string xml)
    {
        AssertRefused(Read(xml), LayoutProblemKind.NotWellFormed);
    }

    [Theory]
    [InlineData("<visualDocument schemaVersion=\"1\" source=\"A.cs\" />")]
    [InlineData("<layout xmlns=\"urn:weavesharp:visual:1\" schemaVersion=\"1\" source=\"A.cs\" />")]
    [InlineData("<visualDocument xmlns=\"urn:weavesharp:visual:2\" schemaVersion=\"1\" source=\"A.cs\" />")]
    public void ARootThatIsNotAVisualDocumentIsRefused(string xml)
    {
        AssertRefused(Read(xml), LayoutProblemKind.NotLayout);
    }

    [Theory]
    [InlineData("")]
    [InlineData("schemaVersion=\"\"")]
    [InlineData("schemaVersion=\"0\"")]
    [InlineData("schemaVersion=\"-1\"")]
    [InlineData("schemaVersion=\"1.0\"")]
    [InlineData("schemaVersion=\" 1\"")]
    public void AMissingOrMalformedSchemaVersionIsRefused(string version)
    {
        AssertRefused(Read($"<visualDocument xmlns=\"urn:weavesharp:visual:1\" {version} source=\"A.cs\" />"), LayoutProblemKind.SchemaVersion);
    }

    [Fact]
    public void ANewerSchemaVersionIsPreservedNotRead()
    {
        var result = Read("<visualDocument xmlns=\"urn:weavesharp:visual:1\" schemaVersion=\"2\" source=\"A.cs\"><future /></visualDocument>");

        Assert.Equal(LayoutReadStatus.UnsupportedVersion, result.Status);
        Assert.Null(result.Document);
        Assert.False(result.CanWrite);
        Assert.Equal(2, result.DeclaredSchemaVersion);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("../Other.cs")]
    [InlineData("src/../../Other.cs")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("file:///etc/passwd")]
    [InlineData("src\\A.cs")]
    [InlineData("./A.cs")]
    [InlineData("src//A.cs")]
    [InlineData("")]
    public void AnUnsafeSourcePathIsRefused(string source)
    {
        AssertRefused(Read($"<visualDocument xmlns=\"urn:weavesharp:visual:1\" schemaVersion=\"1\" source=\"{source}\" />"), LayoutProblemKind.InvalidRecord);
    }

    [Fact]
    public void ANestedRelativeSourcePathIsAccepted()
    {
        var result = Read("<visualDocument xmlns=\"urn:weavesharp:visual:1\" schemaVersion=\"1\" source=\"src/Billing/InvoiceWriter.cs\" />");
        Assert.Equal("src/Billing/InvoiceWriter.cs", result.Document?.Source);
    }

    [Theory]
    [InlineData("<node syntaxKind=\"Block\"><anchor syntaxRole=\"r\" /></node>")]
    [InlineData("<node id=\"has space\" syntaxKind=\"Block\"><anchor syntaxRole=\"r\" /></node>")]
    [InlineData("<node id=\"n\"><anchor syntaxRole=\"r\" /></node>")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block;Drop\"><anchor syntaxRole=\"r\" /></node>")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\" />")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\"><anchor /></node>")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\"><anchor syntaxRole=\"r\" /><anchor syntaxRole=\"s\" /></node>")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\" collapsed=\"yes\"><anchor syntaxRole=\"r\" /></node>")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\" x=\"1.5\" y=\"0\"><anchor syntaxRole=\"r\" /></node>")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\" x=\"NaN\" y=\"0\"><anchor syntaxRole=\"r\" /></node>")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\" x=\"1e3\" y=\"0\"><anchor syntaxRole=\"r\" /></node>")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\" x=\"10\"><anchor syntaxRole=\"r\" /></node>")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\" x=\"2000000\" y=\"0\"><anchor syntaxRole=\"r\" /></node>")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\"><anchor syntaxRole=\"r\" /><extent along=\"0\" across=\"5\" /></node>")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\"><anchor syntaxRole=\"r\" /><extent along=\"-5\" across=\"5\" /></node>")]
    [InlineData("<node id=\"n\" syntaxKind=\"Block\">text<anchor syntaxRole=\"r\" /></node>")]
    [InlineData("<note id=\"n\" />")]
    [InlineData("<note id=\"n\"><text>a</text><text>b</text></note>")]
    [InlineData("<group id=\"g\"><member node=\"n\" /></group>")]
    [InlineData("<group id=\"g\" title=\"T\" />")]
    [InlineData("<group id=\"g\" title=\"T\"><member /></group>")]
    [InlineData("<edgeRoute id=\"e\" from=\"a\" fromPort=\"out\" to=\"b\" toPort=\"in\" />")]
    [InlineData("<edgeRoute id=\"e\" from=\"a\" to=\"b\" toPort=\"in\"><point x=\"0\" y=\"0\" /></edgeRoute>")]
    [InlineData("<edgeRoute id=\"e\" from=\"a\" fromPort=\"out\" to=\"b\" toPort=\"in\"><point x=\"0\" /></edgeRoute>")]
    public void AnInvalidRecordIsRefusedAndTheFileMarkedDamaged(string record)
    {
        var result = Read(Document(record + "<note id=\"kept\"><text>still read</text></note>"));

        AssertDamaged(result, LayoutProblemKind.InvalidRecord);
        var view = Assert.Single(result.Document!.Views);
        Assert.Equal("kept", Assert.Single(view.Notes).Id);
        Assert.Empty(view.Nodes);
        Assert.Empty(view.Groups);
        Assert.Empty(view.EdgeRoutes);
    }

    [Fact]
    public void AnUnknownFlowDirectionRefusesTheView()
    {
        var result = Read(Document(string.Empty, " flow=\"bottom-to-top\""));
        AssertDamaged(result, LayoutProblemKind.InvalidRecord);
        Assert.Empty(result.Document!.Views);
    }

    [Fact]
    public void ADuplicateIdentifierRefusesTheSecondRecord()
    {
        var result = Read(Document("<note id=\"n\"><text>first</text></note><note id=\"n\"><text>second</text></note>"));

        AssertDamaged(result, LayoutProblemKind.DuplicateId);
        Assert.Equal("first", Assert.Single(result.Document!.Views[0].Notes).Text);
    }

    [Fact]
    public void ADuplicateViewIdentifierRefusesTheSecondView()
    {
        var result = Read("<visualDocument xmlns=\"urn:weavesharp:visual:1\" schemaVersion=\"1\" source=\"A.cs\"><view id=\"v\" /><view id=\"v\" /></visualDocument>");
        AssertDamaged(result, LayoutProblemKind.DuplicateId);
        Assert.Single(result.Document!.Views);
    }

    [Fact]
    public void UnresolvedReferencesAreOrphansNotRefusals()
    {
        var result = Read(Document(
            "<group id=\"g\" title=\"T\"><member node=\"gone\" /></group>" +
            "<edgeRoute id=\"e\" from=\"gone\" fromPort=\"out\" to=\"also-gone\" toPort=\"in\"><point x=\"0\" y=\"0\" /></edgeRoute>"));

        Assert.Equal(LayoutReadStatus.Valid, result.Status);
        var view = result.Document!.Views[0];
        Assert.Single(view.Groups);
        Assert.Single(view.EdgeRoutes);
        Assert.Equal(
            new[] { new OrphanedReference("g", "gone"), new OrphanedReference("e", "gone"), new OrphanedReference("e", "also-gone") },
            view.Orphans);
    }

    [Fact]
    public void UnknownContentIsKeptAndNotInterpreted()
    {
        var result = Read("""
            <visualDocument xmlns="urn:weavesharp:visual:1" xmlns:ext="urn:example:extension" schemaVersion="1" source="A.cs" ext:origin="tool">
              <view id="v" futureSetting="on">
                <node id="n" syntaxKind="Block" ext:hint="h"><anchor syntaxRole="r" ext:weight="2" /><ext:badge level="1">B</ext:badge></node>
                <futureRecord id="f"><inner /></futureRecord>
              </view>
              <ext:toolState version="3" />
            </visualDocument>
            """);

        Assert.Equal(LayoutReadStatus.Valid, result.Status);
        var document = result.Document!;
        Assert.Equal(new UnknownAttribute("urn:example:extension", "origin", "tool"), Assert.Single(document.UnknownAttributes));
        Assert.Equal("toolState", Assert.Single(document.UnknownElements).LocalName);
        var view = document.Views[0];
        Assert.Equal("futureSetting", Assert.Single(view.UnknownAttributes).LocalName);
        Assert.Contains("<inner", Assert.Single(view.UnknownElements).Xml, StringComparison.Ordinal);
        var node = Assert.Single(view.Nodes);
        Assert.Equal("hint", Assert.Single(node.UnknownAttributes).LocalName);
        Assert.Equal("weight", Assert.Single(node.Anchor.UnknownAttributes).LocalName);
        Assert.Equal("badge", Assert.Single(node.UnknownElements).LocalName);
    }

    [Fact]
    public void TextIsDataNeverMarkup()
    {
        var result = Read(Document("<note id=\"n\"><text>&lt;script&gt;alert(1)&lt;/script&gt; &amp; more</text></note>"));
        Assert.Equal("<script>alert(1)</script> & more", result.Document!.Views[0].Notes[0].Text);
    }

    [Fact]
    public void CommentsAreIgnored()
    {
        var result = Read(Document("<!-- a comment --><note id=\"n\"><text>t</text></note>"));
        Assert.Equal(LayoutReadStatus.Valid, result.Status);
    }

    /// <summary>A stream that never ends, counting what the reader takes from it.</summary>
    private sealed class EndlessStream : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)'a', offset, count);
            BytesRead += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
