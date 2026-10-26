using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace WeaveSharp.Projection.Spike.Tests;

public sealed class StructureTests
{
    [Fact]
    public void BranchesJoinOnlyWhenTheyCanFinishNormally()
    {
        var graph = Project("if (Ready()) A(); else B(); C();");
        var region = One(graph, ElementKind.Conditional);
        var condition = Child(graph, region, "condition");
        var yes = Child(graph, region, "then");
        var no = Child(graph, region, "else");
        Edge(graph, condition.Id, "out", yes.Id, "in", RelationshipKind.Branch, "true");
        Edge(graph, condition.Id, "out", no.Id, "in", RelationshipKind.Branch, "false");
        Edge(graph, yes.Id, "out", region.Id, "out", RelationshipKind.Sequence);
        Edge(graph, no.Id, "out", region.Id, "out", RelationshipKind.Sequence);

        var terminal = Project("if (Ready()) return; else throw Error(); After();");
        var terminalRegion = One(terminal, ElementKind.Conditional);
        Assert.DoesNotContain(terminal.Relationships, edge => edge.From == new Endpoint(terminalRegion.Id, "out"));
        Assert.Equal(4, terminal.Elements.Count(element => element.OwnsStatement && element.Anchor.SyntaxKind != "Block"));
        Assert.Equal(2, terminal.Relationships.Count(edge => edge.Kind == RelationshipKind.Exit));
    }

    [Fact]
    public void WhileBreakAndContinueUseTheirLexicalLoop()
    {
        var graph = Project("while (Ready()) { if (Skip()) continue; if (Stop()) break; Tick(); }");
        var loop = One(graph, ElementKind.Loop);
        var test = Child(graph, loop, "condition");
        var body = Child(graph, loop, "body");
        Edge(graph, test.Id, "out", body.Id, "in", RelationshipKind.Branch, "true");
        Edge(graph, body.Id, "out", test.Id, "in", RelationshipKind.LoopBack);
        Assert.Equal(new Endpoint(test.Id, "in"), Transfer(graph, "continue").To);
        Assert.Equal(new Endpoint(loop.Id, "out"), Transfer(graph, "break").To);
    }

    [Fact]
    public void ForInitializesOnceAndContinuesThroughIncrementors()
    {
        var graph = Project("for (int i = Seed(); i < 6; i++, Tick()) { if (Skip()) continue; Write(i); }");
        var loop = One(graph, ElementKind.Loop);
        var initial = Child(graph, loop, "initialize");
        var test = Child(graph, loop, "condition");
        var incrementors = graph.Elements.Where(element => element.Parent == loop.Id && element.Role == "increment").ToArray();
        Assert.Equal(2, incrementors.Length);
        Edge(graph, loop.Id, "in", initial.Id, "in", RelationshipKind.Sequence);
        Edge(graph, initial.Id, "out", test.Id, "in", RelationshipKind.Sequence);
        Assert.Equal(new Endpoint(loop.Id, "increment"), Transfer(graph, "continue").To);
        Edge(graph, loop.Id, "increment", incrementors[0].Id, "in", RelationshipKind.Sequence);
        Edge(graph, incrementors[0].Id, "out", incrementors[1].Id, "in", RelationshipKind.Sequence);
        Edge(graph, incrementors[1].Id, "out", test.Id, "in", RelationshipKind.LoopBack);
        Assert.DoesNotContain(graph.Relationships, edge => edge.Kind == RelationshipKind.LoopBack && edge.To.Element == initial.Id);
    }

    [Fact]
    public void EndlessForHasNoInventedFalseBranchOrSuccessor()
    {
        var graph = Project("for (;;) Tick(); After();");
        var loop = One(graph, ElementKind.Loop);
        Assert.DoesNotContain(graph.Relationships, edge => edge.To == new Endpoint(loop.Id, "out"));
        Assert.DoesNotContain(graph.Relationships, edge => edge.From == new Endpoint(loop.Id, "out"));
        Assert.Contains(graph.Elements, element => element.Anchor.SyntaxKind == "ExpressionStatement" && element.Parent == "n$");
    }

    [Fact]
    public void DoRunsItsBodyBeforeTesting()
    {
        var graph = Project("do { Tick(); } while (Ready());");
        var loop = One(graph, ElementKind.Loop);
        var body = Child(graph, loop, "body");
        var condition = Child(graph, loop, "condition");
        Edge(graph, loop.Id, "in", body.Id, "in", RelationshipKind.Sequence);
        Edge(graph, condition.Id, "out", body.Id, "in", RelationshipKind.LoopBack, "true");
    }

    [Fact]
    public void DoWithAnUnconditionalReturnHasNoNormalSuccessor()
    {
        var graph = Project("do { return; } while (Ready()); After();");
        var loop = One(graph, ElementKind.Loop);
        Assert.DoesNotContain(graph.Relationships, edge => edge.From == new Endpoint(loop.Id, "out"));
        var continued = Project("do { continue; } while (Ready()); After();");
        var continuedLoop = One(continued, ElementKind.Loop);
        Assert.Contains(continued.Relationships, edge => edge.From == new Endpoint(continuedLoop.Id, "out"));
    }

    [Fact]
    public void ForeachSeparatesCollectionEvaluationFromIterationAndCleanup()
    {
        var graph = Project("foreach (var item in Items()) { if (Skip()) continue; break; }");
        var loop = One(graph, ElementKind.Loop);
        var collection = Child(graph, loop, "collection");
        Edge(graph, loop.Id, "in", collection.Id, "in", RelationshipKind.Sequence);
        Edge(graph, collection.Id, "out", loop.Id, "next", RelationshipKind.Sequence);
        Assert.Equal(new Endpoint(loop.Id, "next"), Transfer(graph, "continue").To);
        Assert.Empty(graph.Cleanups.Where(step => step.Transfer == Transfer(graph, "continue").From.Element));
        Assert.Equal(loop.Id, Assert.Single(graph.Cleanups.Where(step => step.Transfer == Transfer(graph, "break").From.Element)).Region);
        Assert.Contains(graph.Relationships, edge => edge.Kind == RelationshipKind.Cleanup && edge.From == new Endpoint(loop.Id, "cleanup"));
    }

    [Fact]
    public void SwitchLabelsShareSectionsAndBreakDoesNotLeaveAnOuterLoop()
    {
        var graph = Project("while (Ready()) { switch (Key()) { case 1: case 2: break; default: continue; } Tick(); }");
        var selection = One(graph, ElementKind.Switch);
        var loop = One(graph, ElementKind.Loop);
        var selector = Child(graph, selection, "selector");
        var branches = graph.Relationships.Where(edge => edge.From == new Endpoint(selector.Id, "out") && edge.Kind == RelationshipKind.Branch).ToArray();
        Assert.Equal(3, branches.Length);
        Assert.Equal(branches[0].To, branches[1].To);
        Assert.NotEqual(branches[0].To, branches[2].To);
        Assert.Equal(new Endpoint(selection.Id, "out"), Transfer(graph, "break").To);
        Assert.Equal(new Endpoint(Child(graph, loop, "condition").Id, "in"), Transfer(graph, "continue").To);
    }

    [Fact]
    public void TryKeepsHandlersFiltersAndFinallyDistinct()
    {
        var graph = Project("try { A(); } catch (E error) when (Keep(error)) { B(); } finally { C(); }");
        var region = One(graph, ElementKind.Try);
        var handler = One(graph, ElementKind.Catch);
        var final = One(graph, ElementKind.Finally);
        var filter = Child(graph, handler, "filter");
        Assert.Equal(region.Id, handler.Parent);
        Assert.Equal(region.Id, final.Parent);
        Edge(graph, region.Id, "exception", handler.Id, "in", RelationshipKind.Exception, "handler:0");
        Edge(graph, filter.Id, "out", Child(graph, handler, "body").Id, "in", RelationshipKind.Branch, "true");
        Assert.Equal(2, graph.Relationships.Count(edge => edge.Kind == RelationshipKind.Cleanup && edge.Label == "normal" && edge.To.Element == final.Id));
        Assert.Contains(graph.Relationships, edge => edge.Kind == RelationshipKind.Exception && edge.Label == "unhandled");
        Assert.Contains(graph.Cleanups, step => step.Transfer == region.Id && step.Region == final.Id);
    }

    [Fact]
    public void ReturnRecordsCleanupFromTheInsideOut()
    {
        var graph = Project("try { using (Open()) { lock (gate) { return 3; } } } finally { Close(); }");
        var exit = Transfer(graph, "return");
        Assert.Equal(new Endpoint("n$", "return"), exit.To);
        var route = graph.Cleanups.Where(step => step.Transfer == exit.From.Element).OrderBy(step => step.Order).ToArray();
        Assert.Equal(new[] { ElementKind.Lock, ElementKind.Using, ElementKind.Finally },
            route.Select(step => graph.Elements.Single(element => element.Id == step.Region).Kind));
        Assert.Equal(new[] { 0, 1, 2 }, route.Select(step => step.Order));
        Assert.DoesNotContain(graph.Relationships, edge => edge.From == new Endpoint(exit.From.Element, "out"));
    }

    [Fact]
    public void BreakAndContinueCleanOnlyTheScopesTheyLeave()
    {
        var graph = Project("using (Open()) { while (Ready()) { try { if (Skip()) continue; break; } finally { Tick(); } } }");
        foreach (string label in new[] { "break", "continue" })
        {
            var exit = Transfer(graph, label);
            var cleanup = Assert.Single(graph.Cleanups.Where(step => step.Transfer == exit.From.Element));
            Assert.Equal(One(graph, ElementKind.Finally).Id, cleanup.Region);
        }
    }

    [Fact]
    public void ThrowDispatchesBeforeTheTryFinallyRuns()
    {
        var graph = Project("try { using (Open()) { throw Error(); } } catch { Recover(); } finally { Close(); }");
        var exit = Transfer(graph, "throw");
        Assert.Equal(new Endpoint(One(graph, ElementKind.Try).Id, "exception"), exit.To);
        var cleanup = Assert.Single(graph.Cleanups.Where(step => step.Transfer == exit.From.Element));
        Assert.Equal(One(graph, ElementKind.Using).Id, cleanup.Region);
    }

    [Fact]
    public void AwaitKeepsTheCallAndContinuationSeparate()
    {
        var graph = Project("var pending = Fetch(); var answer = await pending; await Save(answer);");
        var awaits = graph.Elements.Where(element => element.Kind == ElementKind.Await).ToArray();
        Assert.Equal(2, awaits.Length);
        foreach (var awaited in awaits)
        {
            var operand = Child(graph, awaited, "operand");
            Edge(graph, operand.Id, "out", awaited.Id, "resume", RelationshipKind.Await);
            Edge(graph, awaited.Id, "resume", awaited.Id, "out", RelationshipKind.Resume);
            Edge(graph, operand.Id, "value", awaited.Id, "operand:0", RelationshipKind.Value);
        }
        Assert.Equal(2, graph.Elements.Count(element => element.Anchor.SyntaxKind == "InvocationExpression"));
    }

    [Fact]
    public void InvocationOperandsRetainTheirEvaluationOrder()
    {
        var graph = Project("Receiver().Send(First(), Second());");
        var invocation = graph.Elements.Single(element => element.Anchor.SyntaxKind == "InvocationExpression"
            && element.Parent == graph.Elements.Single(element => element.Anchor.SyntaxKind == "ExpressionStatement").Id);
        var operands = graph.Elements.Where(element => element.Parent == invocation.Id).ToArray();
        Assert.Equal(3, operands.Length);
        Edge(graph, invocation.Id, "in", operands[0].Id, "in", RelationshipKind.Sequence);
        Edge(graph, operands[0].Id, "out", operands[1].Id, "in", RelationshipKind.Sequence);
        Edge(graph, operands[1].Id, "out", operands[2].Id, "in", RelationshipKind.Sequence);
        Assert.Equal(3, graph.Relationships.Count(edge => edge.Kind == RelationshipKind.Value && edge.To.Element == invocation.Id));
    }

    [Fact]
    public void AssignmentEvaluatesTheDestinationBeforeTheValueWithoutReadingItAsAnOperand()
    {
        var graph = Project("Receiver().Value = Read();");
        var assignment = graph.Elements.Single(element => element.Anchor.SyntaxKind == "SimpleAssignmentExpression");
        var destination = Child(graph, assignment, "target");
        var value = Child(graph, assignment, "operand");
        Edge(graph, destination.Id, "out", value.Id, "in", RelationshipKind.Sequence);
        Edge(graph, destination.Id, "location", assignment.Id, "operand:0", RelationshipKind.Storage);
        Edge(graph, value.Id, "value", assignment.Id, "operand:1", RelationshipKind.Value);
        Assert.DoesNotContain(graph.Relationships, edge => edge.Kind == RelationshipKind.Value
            && edge.From.Element == destination.Id && edge.To.Element == assignment.Id);
    }

    [Theory]
    [InlineData("MissingSemicolon.cs")]
    [InlineData("UnclosedBrace.cs")]
    [InlineData("HalfExpression.cs")]
    [InlineData("SkippedToken.cs")]
    public void DamagedBodiesAreExactOpaqueSlices(string fixture)
    {
        var snapshot = PreservationTests.Load(fixture);
        var body = Assert.Single(snapshot.Bodies());
        Assert.True(body.ContainsDiagnostics || body.ContainsSkippedText || body.DescendantTokens().Any(token => token.IsMissing));
        var element = Assert.Single(snapshot.Project(body).Elements);
        Assert.Equal(ElementKind.CSharpRegion, element.Kind);
        Assert.Equal(snapshot.Text.ToString(body.Span), element.Source);
        Assert.Equal("$", element.Anchor.Path);
    }

    [Theory]
    [InlineData("goto finish;", "GotoStatement")]
    [InlineData("finish: Tick();", "LabeledStatement")]
    [InlineData("yield return 7;", "YieldReturnStatement")]
    [InlineData("using var item = Open();", "LocalDeclarationStatement")]
    [InlineData("var value = flag ? A() : B();", "LocalDeclarationStatement")]
    [InlineData("if (A() && B()) Tick();", "IfStatement")]
    [InlineData("if (item is int value) Tick();", "IfStatement")]
    [InlineData("await foreach (var item in Items()) Tick();", "ForEachStatement")]
    [InlineData("fixed (int* pointer = address) { Tick(); }", "FixedStatement")]
    public void UnmodelledStatementsRemainWhole(string statement, string syntaxKind)
    {
        var graph = Project(statement);
        var opaque = One(graph, ElementKind.CSharpRegion);
        Assert.Equal(statement, opaque.Source);
        Assert.Equal(syntaxKind, opaque.Anchor.SyntaxKind);
        Assert.DoesNotContain(graph.Elements, element => element.Parent == opaque.Id);
    }

    [Fact]
    public void SwitchExpressionsAndPatternsAreNotFlattened()
    {
        var snapshot = PreservationTests.Load("Switches.cs");
        var expression = snapshot.Bodies().OfType<SwitchExpressionSyntax>().Single();
        var opaque = Assert.Single(snapshot.Project(expression).Elements);
        Assert.Equal(ElementKind.CSharpRegion, opaque.Kind);
        Assert.Equal(snapshot.Text.ToString(expression.Span), opaque.Source);
        var patternBody = snapshot.Bodies().OfType<BlockSyntax>().Single(body => body.Parent is MethodDeclarationSyntax { Identifier.ValueText: "Pattern" });
        var graph = snapshot.Project(patternBody);
        Assert.Equal(2, graph.Elements.Count(element => element.Kind == ElementKind.CSharpRegion));
        Assert.DoesNotContain(graph.Elements, element => element.Kind is ElementKind.Conditional or ElementKind.Switch);
    }

    private static ProjectionGraph Project(string statements)
    {
        var snapshot = new ProjectionSnapshot("sample.cs", "one", "class C { void M() { " + statements + " } }");
        return snapshot.Project(snapshot.Bodies().Single());
    }

    private static GraphElement One(ProjectionGraph graph, ElementKind kind) =>
        Assert.Single(graph.Elements.Where(element => element.Kind == kind));

    private static GraphElement Child(ProjectionGraph graph, GraphElement parent, string role) =>
        Assert.Single(graph.Elements.Where(element => element.Parent == parent.Id && element.Role == role));

    private static Relationship Transfer(ProjectionGraph graph, string label) =>
        Assert.Single(graph.Relationships.Where(edge => edge.Kind == RelationshipKind.Exit && edge.Label == label));

    private static void Edge(ProjectionGraph graph, string from, string fromPort, string to, string toPort,
        RelationshipKind kind, string? label = null) =>
        Assert.Contains(new Relationship(new(from, fromPort), new(to, toPort), kind, label), graph.Relationships);
}
