using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace WeaveSharp.Projection.Spike.Tests;

public sealed class PreservationTests
{
    public static IEnumerable<object[]> Fixtures() => Directory.GetFiles(
        Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.cs")
        .OrderBy(path => path, StringComparer.Ordinal).Select(path => new object[] { Path.GetFileName(path) });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ProjectionPreservesTheFileAndSnapshot(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        byte[] before = File.ReadAllBytes(path);
        string original = File.ReadAllText(path);
        using var stream = new MemoryStream(before, writable: false);
        var text = SourceText.From(stream, Encoding.UTF8);
        var snapshot = new ProjectionSnapshot(name, "fixture-1", text);
        var bodies = snapshot.Bodies().ToArray();
        Assert.NotEmpty(bodies);
        foreach (var body in bodies)
        {
            var graph = snapshot.Project(body);
            var again = snapshot.Project(body);
            Assert.Equal(graph, again);
            Assert.Equal(graph.GetHashCode(), again.GetHashCode());
            Assert.Equal(graph.ToJson(), again.ToJson());
            Assert.Equal(graph, JsonSerializer.Deserialize<ProjectionGraph>(graph.ToJson()));
            VerifyAnchorsAndOwnership(snapshot, body, graph);
        }
        Assert.Same(text, snapshot.Text);
        Assert.Equal(original, snapshot.Text.ToString());
        Assert.Equal(original, snapshot.Tree.GetRoot(TestContext.Current.CancellationToken).ToFullString());
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(before, stream.ToArray());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EncodingAndLineEndingsDoNotMoveAnchors(bool utf16, bool crlf)
    {
        string source = "// compass 🧭\nclass C { string M() { /* café */ return \"雪\"; } }\n";
        if (crlf) source = source.Replace("\n", "\r\n", StringComparison.Ordinal);
        Encoding encoding = utf16 ? new UnicodeEncoding(false, true) : new UTF8Encoding(true);
        byte[] bytes = encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray();
        byte[] before = bytes.ToArray();
        using var stream = new MemoryStream(bytes, writable: false);
        var snapshot = new ProjectionSnapshot("unicode.cs", "draft", SourceText.From(stream, encoding));
        var body = Assert.Single(snapshot.Bodies());
        var graph = snapshot.Project(body);
        VerifyAnchorsAndOwnership(snapshot, body, graph);
        var result = Assert.Single(graph.Elements.Where(element => element.Anchor.SyntaxKind == "ReturnStatement"));
        Assert.Equal(source.IndexOf("return", StringComparison.Ordinal), result.Anchor.Start);
        Assert.Equal(source, snapshot.Tree.GetRoot(TestContext.Current.CancellationToken).ToFullString());
        Assert.Equal(source, snapshot.Text.ToString());
        Assert.Equal(before, bytes);
        Assert.Equal(graph, snapshot.Project(body));
    }

    [Fact]
    public void DocumentAndRevisionArePartOfGraphIdentity()
    {
        const string source = "class C { int M() => 4; }";
        var first = new ProjectionSnapshot("first.cs", "one", source);
        var otherDocument = new ProjectionSnapshot("second.cs", "one", source);
        var otherRevision = new ProjectionSnapshot("first.cs", "two", source);
        Assert.NotEqual(first.Project(first.Bodies().Single()), otherDocument.Project(otherDocument.Bodies().Single()));
        Assert.NotEqual(first.Project(first.Bodies().Single()), otherRevision.Project(otherRevision.Bodies().Single()));
        Assert.Throws<ArgumentException>(() => first.Project(otherDocument.Bodies().Single()));
    }

    [Fact]
    public void ParseSymbolsSelectSourceWithoutEditingDirectives()
    {
        const string source = "class C { int M() {\n#if LEFT\nreturn 1;\n#else\nreturn 2;\n#endif\n} }";
        var left = new ProjectionSnapshot("symbols.cs", "one", source,
            new CSharpParseOptions(LanguageVersion.CSharp14, preprocessorSymbols: new[] { "LEFT" }));
        var right = new ProjectionSnapshot("symbols.cs", "one", source);
        foreach (var snapshot in new[] { left, right })
        {
            var body = snapshot.Bodies().Single();
            VerifyAnchorsAndOwnership(snapshot, body, snapshot.Project(body));
            Assert.Equal(source, snapshot.Tree.GetRoot(TestContext.Current.CancellationToken).ToFullString());
        }
        Assert.NotEqual(left.Project(left.Bodies().Single()), right.Project(right.Bodies().Single()));
    }

    [Fact]
    public void CallableBodiesCanBeSelectedSeparately()
    {
        var snapshot = Load("Bodies.cs");
        var bodies = snapshot.Bodies().ToArray();
        Assert.Equal(12, bodies.Length);
        Assert.Contains(bodies, body => body.Parent is ConstructorDeclarationSyntax);
        Assert.Contains(bodies, body => body.Parent is AccessorDeclarationSyntax);
        Assert.Contains(bodies, body => body.Parent is LocalFunctionStatementSyntax);
        Assert.Contains(bodies, body => body.Parent is SimpleLambdaExpressionSyntax);
        Assert.Contains(bodies, body => body.Parent is ParenthesizedLambdaExpressionSyntax);
        Assert.Contains(bodies, body => body.Parent is AnonymousMethodExpressionSyntax);
        var outer = bodies.OfType<BlockSyntax>().Single(body => body.Parent is MethodDeclarationSyntax { Identifier.ValueText: "Outer" });
        var graph = snapshot.Project(outer);
        var locals = graph.Elements.Where(element => element.Anchor.SyntaxKind == "LocalFunctionStatement").ToArray();
        Assert.Equal(2, locals.Length);
        Assert.All(locals, local => Assert.Equal(ElementKind.CSharpRegion, local.Kind));
        Assert.DoesNotContain(graph.Elements, element => locals.Any(local => element.Parent == local.Id));
    }

    internal static ProjectionSnapshot Load(string name) => new(name, "fixture-1",
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    internal static SyntaxNode Resolve(SyntaxNode body, SourceAnchor anchor)
    {
        var segments = anchor.Path.Split('/');
        Assert.Equal("$", segments[0]);
        var node = body;
        foreach (string segment in segments.Skip(1))
            node = node.ChildNodes().ElementAt(int.Parse(segment, CultureInfo.InvariantCulture));
        return node;
    }

    private static void VerifyAnchorsAndOwnership(ProjectionSnapshot snapshot, SyntaxNode body, ProjectionGraph graph)
    {
        Assert.NotEmpty(graph.Elements);
        Assert.Equal(graph.Elements.Length, graph.Elements.Select(element => element.Id).Distinct().Count());
        var resolved = graph.Elements.ToDictionary(element => element.Id, element => Resolve(body, element.Anchor));
        var root = Assert.Single(graph.Elements.Where(element => element.Parent is null));
        Assert.Same(body, resolved[root.Id]);
        foreach (var element in graph.Elements)
        {
            var syntax = resolved[element.Id];
            Assert.Equal(snapshot.Document, element.Anchor.Document);
            Assert.Equal(snapshot.Revision, element.Anchor.Revision);
            Assert.Equal(syntax.SpanStart, element.Anchor.Start);
            Assert.Equal(syntax.Span.Length, element.Anchor.Length);
            Assert.Equal(syntax.Kind().ToString(), element.Anchor.SyntaxKind);
            Assert.Equal(syntax is StatementSyntax, element.OwnsStatement);
            if (element.Parent is { } parent)
                Assert.Contains(resolved[parent], syntax.Ancestors());
            if (element.Kind == ElementKind.CSharpRegion)
            {
                Assert.Equal(snapshot.Text.ToString(syntax.Span), element.Source);
                Assert.NotEmpty(element.FallbackReason!);
                Assert.DoesNotContain(graph.Elements, child => child.Parent == element.Id);
            }
        }
        foreach (var statement in body.DescendantNodesAndSelf().OfType<StatementSyntax>())
        {
            var owners = graph.Elements.Where(element =>
                element.OwnsStatement && ReferenceEquals(resolved[element.Id], statement)
                || element.Kind == ElementKind.CSharpRegion && statement.Ancestors().Contains(resolved[element.Id]));
            Assert.Single(owners);
        }
        foreach (var blockElement in graph.Elements.Where(element => element.Kind == ElementKind.Block))
        {
            var block = Assert.IsType<BlockSyntax>(resolved[blockElement.Id]);
            var children = graph.Elements.Where(element => element.Parent == blockElement.Id && element.OwnsStatement).ToArray();
            Assert.Equal(block.Statements.Count, children.Length);
            Assert.Equal(block.Statements.Select(statement => statement.Span),
                children.Select(child => resolved[child.Id].Span));
            for (int index = 1; index < children.Length; index++)
                Assert.True(children[index - 1].Anchor.Start + children[index - 1].Anchor.Length <= children[index].Anchor.Start);
        }
        foreach (var relationship in graph.Relationships)
        {
            Assert.Contains(relationship.From.Element, resolved.Keys);
            Assert.Contains(relationship.To.Element, resolved.Keys);
        }
        foreach (var cleanup in graph.Cleanups)
        {
            Assert.Contains(cleanup.Transfer, resolved.Keys);
            Assert.Contains(cleanup.Region, resolved.Keys);
        }
    }
}
