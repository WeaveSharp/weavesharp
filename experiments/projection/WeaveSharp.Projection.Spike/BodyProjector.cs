using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace WeaveSharp.Projection.Spike;

internal sealed class BodyProjector
{
    private readonly ProjectionSnapshot snapshot;
    private readonly SyntaxNode body;
    private readonly Dictionary<SyntaxNode, string> paths = new();
    private readonly List<GraphElement> elements = new();
    private readonly List<Relationship> relationships = new();
    private readonly List<CleanupStep> cleanups = new();

    private sealed record Destination(Endpoint Endpoint, int CleanupDepth);
    private sealed record Context(Destination Return, Destination Exception,
        Destination? Break, Destination? Continue, ImmutableArray<string> CleanupRegions);
    private sealed record Flow(string Id, bool MayComplete = true)
    {
        public Endpoint In => new(Id, "in");
        public Endpoint Out => new(Id, "out");
    }

    public BodyProjector(ProjectionSnapshot snapshot, SyntaxNode body)
    {
        this.snapshot = snapshot;
        this.body = body;
        Index(body, "$");
    }

    private void Index(SyntaxNode syntax, string path)
    {
        paths.Add(syntax, path);
        int index = 0;
        foreach (var child in syntax.ChildNodes())
            Index(child, path + "/" + (index++).ToString(CultureInfo.InvariantCulture));
    }

    private string Id(SyntaxNode node) => "n" + paths[node];
    private static Endpoint Port(string id, string port) => new(id, port);
    private string Text(SyntaxNode node) => snapshot.Text.ToString(node.Span);

    public ProjectionGraph Build()
    {
        var context = new Context(new(Port(Id(body), "return"), 0),
            new(Port(Id(body), "throw"), 0), null, null, ImmutableArray<string>.Empty);
        if (body is StatementSyntax statement)
            Statement(statement, null, "body", context);
        else
            Expression((ExpressionSyntax)body, null, "body");
        return new(elements.ToImmutableArray(), relationships.ToImmutableArray(), cleanups.ToImmutableArray());
    }

    private Flow Add(SyntaxNode syntax, string? parent, string role, ElementKind kind,
        string? reason = null)
    {
        var anchor = new SourceAnchor(snapshot.Document, snapshot.Revision,
            syntax.SpanStart, syntax.Span.Length, syntax.Kind().ToString(), paths[syntax]);
        elements.Add(new(Id(syntax), parent, role, kind, anchor, syntax is StatementSyntax,
            kind == ElementKind.CSharpRegion ? Text(syntax) : null, reason));
        return new(Id(syntax));
    }

    private Flow Opaque(SyntaxNode syntax, string? parent, string role, string reason) =>
        Add(syntax, parent, role, ElementKind.CSharpRegion, reason);

    private void Link(Endpoint from, Endpoint to, RelationshipKind kind = RelationshipKind.Sequence,
        string? label = null) => relationships.Add(new(from, to, kind, label));

    private static bool Damaged(SyntaxNode syntax) => syntax.ContainsSkippedText
        || syntax.DescendantTokens(descendIntoTrivia: true).Any(token => token.IsMissing)
        || syntax.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private Flow Statement(StatementSyntax syntax, string? parent, string role, Context context)
    {
        if (Damaged(syntax)) return Opaque(syntax, parent, role, "Incomplete or erroneous syntax");
        switch (syntax)
        {
            case BlockSyntax block:
            {
                var region = Add(block, parent, role, ElementKind.Block);
                return region with { MayComplete = Sequence(region, block.Statements, context) };
            }
            case ExpressionStatementSyntax expression when Supported(expression.Expression):
                return Simple(syntax, parent, role, new[] { expression.Expression }, context);
            case LocalDeclarationStatementSyntax declaration when declaration.UsingKeyword.RawKind == 0
                && Initializers(declaration.Declaration).All(Supported):
                return Simple(syntax, parent, role, Initializers(declaration.Declaration), context);
            case EmptyStatementSyntax:
                return Simple(syntax, parent, role, Array.Empty<ExpressionSyntax>(), context);
            case ReturnStatementSyntax result when result.Expression is null || Supported(result.Expression):
                return Simple(syntax, parent, role, Optional(result.Expression), context, context.Return, "return");
            case ThrowStatementSyntax thrown when thrown.Expression is null || Supported(thrown.Expression):
                return Simple(syntax, parent, role, Optional(thrown.Expression), context, context.Exception, "throw");
            case BreakStatementSyntax when context.Break is not null:
                return Simple(syntax, parent, role, Array.Empty<ExpressionSyntax>(), context, context.Break, "break");
            case ContinueStatementSyntax when context.Continue is not null:
                return Simple(syntax, parent, role, Array.Empty<ExpressionSyntax>(), context, context.Continue, "continue");
            case IfStatementSyntax conditional when Supported(conditional.Condition):
                return Conditional(conditional, parent, role, context);
            case WhileStatementSyntax loop when Supported(loop.Condition):
                return While(loop, parent, role, context);
            case DoStatementSyntax loop when Supported(loop.Condition):
                return Do(loop, parent, role, context);
            case ForStatementSyntax loop when ForExpressions(loop).All(Supported):
                return For(loop, parent, role, context);
            case ForEachStatementSyntax loop when loop.AwaitKeyword.RawKind == 0 && Supported(loop.Expression):
                return ForEach(loop, parent, role, context);
            case SwitchStatementSyntax selection when Supported(selection.Expression)
                && selection.Sections.SelectMany(section => section.Labels).All(label =>
                    label is DefaultSwitchLabelSyntax || label is CaseSwitchLabelSyntax @case && Supported(@case.Value)):
                return Switch(selection, parent, role, context);
            case TryStatementSyntax guarded when guarded.Catches.All(@catch =>
                @catch.Filter is null || Supported(@catch.Filter.FilterExpression)):
                return Try(guarded, parent, role, context);
            case UsingStatementSyntax resource when UsingExpressions(resource).All(Supported):
                return Scope(resource, resource.Statement, UsingExpressions(resource), parent, role,
                    context, ElementKind.Using, resource.AwaitKeyword.RawKind == 0 ? "dispose" : "await dispose");
            case LockStatementSyntax locked when Supported(locked.Expression):
                return Scope(locked, locked.Statement, new[] { locked.Expression }, parent, role,
                    context, ElementKind.Lock, "release");
            default:
                return Opaque(syntax, parent, role, "Outside the experiment's syntax subset");
        }
    }

    private bool Sequence(Flow region, IEnumerable<StatementSyntax> statements, Context context)
    {
        Endpoint previous = region.In;
        bool reachable = true;
        foreach (var statement in statements)
        {
            var child = Statement(statement, region.Id, "statement", context);
            if (reachable) Link(previous, child.In);
            reachable &= child.MayComplete;
            previous = child.Out;
        }
        if (reachable) Link(previous, region.Out);
        return reachable;
    }

    private Flow Simple(StatementSyntax syntax, string? parent, string role,
        IEnumerable<ExpressionSyntax> expressions, Context context, Destination? exit = null, string? label = null)
    {
        var node = Add(syntax, parent, role, ElementKind.Statement);
        var end = Evaluate(expressions, node, "operand", node.In);
        if (exit is null)
            Link(end, node.Out);
        else
        {
            Link(end, Port(node.Id, "transfer"));
            Link(Port(node.Id, "transfer"), exit.Endpoint, RelationshipKind.Exit, label);
            RecordCleanup(node.Id, context, exit);
        }
        return node with { MayComplete = exit is null };
    }

    private void RecordCleanup(string transfer, Context context, Destination destination)
    {
        int order = 0;
        for (int index = context.CleanupRegions.Length - 1; index >= destination.CleanupDepth; index--)
            cleanups.Add(new(transfer, order++, context.CleanupRegions[index]));
    }

    private Endpoint Evaluate(IEnumerable<ExpressionSyntax> expressions, Flow owner, string role, Endpoint start,
        bool firstIsLocation = false)
    {
        var previous = start;
        int index = 0;
        foreach (var expression in expressions)
        {
            bool location = firstIsLocation && index == 0;
            var child = Expression(expression, owner.Id, location ? "target" : role);
            Link(previous, child.In);
            Link(Port(child.Id, location ? "location" : "value"), Port(owner.Id, role + ":" + index++),
                location ? RelationshipKind.Storage : RelationshipKind.Value);
            previous = child.Out;
        }
        return previous;
    }

    private Flow Conditional(IfStatementSyntax syntax, string? parent, string role, Context context)
    {
        var region = Add(syntax, parent, role, ElementKind.Conditional);
        var condition = Expression(syntax.Condition, region.Id, "condition");
        Link(region.In, condition.In);
        var yes = Statement(syntax.Statement, region.Id, "then", context);
        Link(condition.Out, yes.In, RelationshipKind.Branch, "true");
        if (yes.MayComplete) Link(yes.Out, region.Out);
        bool alternativeCompletes = true;
        if (syntax.Else is { } alternative)
        {
            var no = Statement(alternative.Statement, region.Id, "else", context);
            Link(condition.Out, no.In, RelationshipKind.Branch, "false");
            if (no.MayComplete) Link(no.Out, region.Out);
            alternativeCompletes = no.MayComplete;
        }
        else Link(condition.Out, region.Out, RelationshipKind.Branch, "false");
        return region with { MayComplete = yes.MayComplete || alternativeCompletes };
    }

    private Context LoopContext(Flow region, Endpoint continuation, Context context) => context with
    {
        Break = new(region.Out, context.CleanupRegions.Length),
        Continue = new(continuation, context.CleanupRegions.Length)
    };

    private Flow While(WhileStatementSyntax syntax, string? parent, string role, Context context)
    {
        var region = Add(syntax, parent, role, ElementKind.Loop);
        var test = Expression(syntax.Condition, region.Id, "condition");
        var loopBody = Statement(syntax.Statement, region.Id, "body", LoopContext(region, test.In, context));
        Link(region.In, test.In);
        Link(test.Out, loopBody.In, RelationshipKind.Branch, "true");
        Link(test.Out, region.Out, RelationshipKind.Branch, "false");
        if (loopBody.MayComplete) Link(loopBody.Out, test.In, RelationshipKind.LoopBack);
        return region;
    }

    private Flow Do(DoStatementSyntax syntax, string? parent, string role, Context context)
    {
        var region = Add(syntax, parent, role, ElementKind.Loop);
        var test = Expression(syntax.Condition, region.Id, "condition");
        var loopBody = Statement(syntax.Statement, region.Id, "body", LoopContext(region, test.In, context));
        Link(region.In, loopBody.In);
        if (loopBody.MayComplete) Link(loopBody.Out, test.In);
        Link(test.Out, loopBody.In, RelationshipKind.LoopBack, "true");
        Link(test.Out, region.Out, RelationshipKind.Branch, "false");
        return region with { MayComplete = loopBody.MayComplete || HasBreak(region)
            || relationships.Any(edge => edge.Kind == RelationshipKind.Exit && edge.Label == "continue" && edge.To == test.In) };
    }

    private Flow For(ForStatementSyntax syntax, string? parent, string role, Context context)
    {
        var region = Add(syntax, parent, role, ElementKind.Loop);
        var initialized = Evaluate(syntax.Declaration is null ? syntax.Initializers : Initializers(syntax.Declaration),
            region, "initialize", region.In);
        var test = syntax.Condition is null ? null : Expression(syntax.Condition, region.Id, "condition");
        var testEntry = test?.In ?? Port(region.Id, "test");
        var testExit = test?.Out ?? testEntry;
        Link(initialized, testEntry);
        Endpoint continuation = syntax.Incrementors.Count == 0 ? testEntry : Port(region.Id, "increment");
        if (syntax.Incrementors.Count > 0)
        {
            var end = Evaluate(syntax.Incrementors, region, "increment", Port(region.Id, "increment"));
            Link(end, testEntry, RelationshipKind.LoopBack);
        }
        var loopBody = Statement(syntax.Statement, region.Id, "body", LoopContext(region, continuation, context));
        Link(testExit, loopBody.In, RelationshipKind.Branch, syntax.Condition is null ? "always" : "true");
        if (test is not null) Link(testExit, region.Out, RelationshipKind.Branch, "false");
        if (loopBody.MayComplete) Link(loopBody.Out, continuation, RelationshipKind.LoopBack);
        return region with { MayComplete = test is not null || HasBreak(region) };
    }

    private Flow ForEach(ForEachStatementSyntax syntax, string? parent, string role, Context context)
    {
        var region = Add(syntax, parent, role, ElementKind.Loop);
        var collection = Expression(syntax.Expression, region.Id, "collection");
        var next = Port(region.Id, "next");
        var iterationContext = context with { CleanupRegions = context.CleanupRegions.Add(region.Id) };
        var loopBody = Statement(syntax.Statement, region.Id, "body", iterationContext with
        {
            Break = new(region.Out, context.CleanupRegions.Length),
            Continue = new(next, iterationContext.CleanupRegions.Length)
        });
        Link(region.In, collection.In);
        Link(collection.Out, next);
        Link(next, loopBody.In, RelationshipKind.Branch, "item");
        Link(next, Port(region.Id, "cleanup"), RelationshipKind.Branch, "finished");
        Link(Port(region.Id, "cleanup"), region.Out, RelationshipKind.Cleanup, "dispose enumerator if needed");
        Link(Port(region.Id, "unwind"), Port(region.Id, "cleanup"), RelationshipKind.Cleanup, "pending exit");
        if (loopBody.MayComplete) Link(loopBody.Out, next, RelationshipKind.LoopBack);
        return region;
    }

    private bool HasBreak(Flow region) => relationships.Any(edge => edge.Kind == RelationshipKind.Exit
        && edge.Label == "break" && edge.To == region.Out);

    private Flow Switch(SwitchStatementSyntax syntax, string? parent, string role, Context context)
    {
        var region = Add(syntax, parent, role, ElementKind.Switch);
        var value = Expression(syntax.Expression, region.Id, "selector");
        Link(region.In, value.In);
        bool completes = false;
        foreach (var section in syntax.Sections)
        {
            var arm = Add(section, region.Id, "section", ElementKind.SwitchSection);
            foreach (var label in section.Labels)
                Link(value.Out, arm.In, RelationshipKind.Branch, Text(label));
            bool normal = Sequence(arm, section.Statements,
                context with { Break = new(region.Out, context.CleanupRegions.Length) });
            if (normal) Link(arm.Out, region.Out);
            completes |= normal;
        }
        if (!syntax.Sections.SelectMany(section => section.Labels).Any(label => label is DefaultSwitchLabelSyntax))
        {
            Link(value.Out, region.Out, RelationshipKind.Branch, "unmatched");
            completes = true;
        }
        return region with { MayComplete = completes || HasBreak(region) };
    }

    private Flow Try(TryStatementSyntax syntax, string? parent, string role, Context context)
    {
        var region = Add(syntax, parent, role, ElementKind.Try);
        var scoped = syntax.Finally is null ? context : context with
        {
            CleanupRegions = context.CleanupRegions.Add(Id(syntax.Finally))
        };
        var guarded = Statement(syntax.Block, region.Id, "protected", scoped with
        {
            Exception = new(Port(region.Id, "exception"), scoped.CleanupRegions.Length)
        });
        Link(region.In, guarded.In);
        var completions = new List<Endpoint>();
        if (guarded.MayComplete) completions.Add(guarded.Out);
        int index = 0;
        foreach (var handler in syntax.Catches)
        {
            var caught = Add(handler, region.Id, "handler", ElementKind.Catch);
            Link(Port(region.Id, "exception"), caught.In, RelationshipKind.Exception,
                "handler:" + (index++).ToString(CultureInfo.InvariantCulture));
            var handlerBody = Statement(handler.Block, caught.Id, "body", scoped);
            if (handler.Filter is { } filter)
            {
                var predicate = Expression(filter.FilterExpression, caught.Id, "filter");
                Link(caught.In, predicate.In);
                Link(predicate.Out, handlerBody.In, RelationshipKind.Branch, "true");
                Link(predicate.Out, Port(region.Id, "exception"), RelationshipKind.Exception, "next handler");
            }
            else Link(caught.In, handlerBody.In);
            if (handlerBody.MayComplete)
            {
                Link(handlerBody.Out, caught.Out);
                completions.Add(caught.Out);
            }
        }
        // Selection depends on runtime types; this edge records propagation, not a guessed match.
        Link(Port(region.Id, "exception"), context.Exception.Endpoint, RelationshipKind.Exception, "unhandled");
        RecordCleanup(region.Id, scoped, context.Exception);
        if (syntax.Finally is { } final)
        {
            var cleanup = Add(final, region.Id, "cleanup", ElementKind.Finally);
            var cleanupBody = Statement(final.Block, cleanup.Id, "body", context);
            Link(cleanup.In, cleanupBody.In);
            if (cleanupBody.MayComplete) Link(cleanupBody.Out, cleanup.Out);
            foreach (var completion in completions) Link(completion, cleanup.In, RelationshipKind.Cleanup, "normal");
            Link(Port(region.Id, "unwind"), cleanup.In, RelationshipKind.Cleanup, "pending exit");
            if (completions.Count > 0 && cleanupBody.MayComplete) Link(cleanup.Out, region.Out);
            return region with { MayComplete = completions.Count > 0 && cleanupBody.MayComplete };
        }
        foreach (var completion in completions) Link(completion, region.Out);
        return region with { MayComplete = completions.Count > 0 };
    }

    private Flow Scope(StatementSyntax syntax, StatementSyntax scopedBody,
        IEnumerable<ExpressionSyntax> resources, string? parent, string role, Context context,
        ElementKind kind, string cleanupLabel)
    {
        var region = Add(syntax, parent, role, kind);
        var acquired = Evaluate(resources, region, "resource", region.In);
        var nested = Statement(scopedBody, region.Id, "body", context with
        {
            CleanupRegions = context.CleanupRegions.Add(region.Id)
        });
        Link(acquired, nested.In);
        if (nested.MayComplete)
        {
            Link(nested.Out, Port(region.Id, "cleanup"), RelationshipKind.Cleanup, cleanupLabel);
            Link(Port(region.Id, "cleanup"), region.Out);
        }
        Link(Port(region.Id, "unwind"), Port(region.Id, "cleanup"), RelationshipKind.Cleanup, cleanupLabel);
        return region with { MayComplete = nested.MayComplete };
    }

    private Flow Expression(ExpressionSyntax syntax, string? parent, string role)
    {
        if (Damaged(syntax)) return Opaque(syntax, parent, role, "Incomplete or erroneous syntax");
        if (!Supported(syntax)) return Opaque(syntax, parent, role, "Outside the experiment's syntax subset");
        var node = Add(syntax, parent, role, syntax is AwaitExpressionSyntax ? ElementKind.Await : ElementKind.Operation);
        bool writesLocation = syntax is AssignmentExpressionSyntax || syntax.Kind() is
            SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression
            or SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression;
        var end = Evaluate(Operands(syntax), node, "operand", node.In, writesLocation);
        if (syntax is AwaitExpressionSyntax)
        {
            Link(end, Port(node.Id, "resume"), RelationshipKind.Await);
            Link(Port(node.Id, "resume"), node.Out, RelationshipKind.Resume);
        }
        else Link(end, node.Out);
        return node;
    }

    private static bool Supported(ExpressionSyntax syntax)
    {
        bool known = syntax switch
        {
            LiteralExpressionSyntax or IdentifierNameSyntax or ThisExpressionSyntax or BaseExpressionSyntax => true,
            ParenthesizedExpressionSyntax or MemberAccessExpressionSyntax or AwaitExpressionSyntax => true,
            InvocationExpressionSyntax invocation => invocation.ArgumentList.Arguments.All(argument => argument.RefKindKeyword.RawKind == 0),
            ElementAccessExpressionSyntax access => access.ArgumentList.Arguments.All(argument => argument.RefKindKeyword.RawKind == 0),
            PrefixUnaryExpressionSyntax prefix => prefix.Kind() is SyntaxKind.UnaryPlusExpression
                or SyntaxKind.UnaryMinusExpression or SyntaxKind.LogicalNotExpression
                or SyntaxKind.BitwiseNotExpression or SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression,
            PostfixUnaryExpressionSyntax postfix => postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression,
            BinaryExpressionSyntax binary => !binary.IsKind(SyntaxKind.LogicalAndExpression)
                && !binary.IsKind(SyntaxKind.LogicalOrExpression) && !binary.IsKind(SyntaxKind.CoalesceExpression)
                && !binary.IsKind(SyntaxKind.IsExpression) && !binary.IsKind(SyntaxKind.AsExpression),
            AssignmentExpressionSyntax assignment => !assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression),
            _ => false
        };
        return known && Operands(syntax).All(Supported);
    }

    private static IEnumerable<ExpressionSyntax> Operands(ExpressionSyntax syntax) => syntax switch
    {
        ParenthesizedExpressionSyntax grouped => new[] { grouped.Expression },
        MemberAccessExpressionSyntax member => new[] { member.Expression },
        AwaitExpressionSyntax awaited => new[] { awaited.Expression },
        InvocationExpressionSyntax invocation => new[] { invocation.Expression }
            .Concat(invocation.ArgumentList.Arguments.Select(argument => argument.Expression)),
        ElementAccessExpressionSyntax access => new[] { access.Expression }
            .Concat(access.ArgumentList.Arguments.Select(argument => argument.Expression)),
        PrefixUnaryExpressionSyntax prefix => new[] { prefix.Operand },
        PostfixUnaryExpressionSyntax postfix => new[] { postfix.Operand },
        BinaryExpressionSyntax binary => new[] { binary.Left, binary.Right },
        AssignmentExpressionSyntax assignment => new[] { assignment.Left, assignment.Right },
        _ => Array.Empty<ExpressionSyntax>()
    };

    private static IEnumerable<ExpressionSyntax> Optional(ExpressionSyntax? expression) =>
        expression is null ? Array.Empty<ExpressionSyntax>() : new[] { expression };

    private static IEnumerable<ExpressionSyntax> Initializers(VariableDeclarationSyntax declaration) =>
        declaration.Variables.Where(variable => variable.Initializer is not null)
            .Select(variable => variable.Initializer!.Value);

    private static IEnumerable<ExpressionSyntax> ForExpressions(ForStatementSyntax loop) =>
        (loop.Declaration is null ? loop.Initializers : Initializers(loop.Declaration))
        .Concat(Optional(loop.Condition)).Concat(loop.Incrementors);

    private static IEnumerable<ExpressionSyntax> UsingExpressions(UsingStatementSyntax resource) =>
        resource.Declaration is null ? Optional(resource.Expression) : Initializers(resource.Declaration);
}
