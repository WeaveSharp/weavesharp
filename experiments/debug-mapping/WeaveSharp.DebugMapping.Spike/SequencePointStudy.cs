using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using WeaveSharp.Projection.Spike;

namespace WeaveSharp.DebugMapping.Spike;

public static class SequencePointStudy
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Runtime reference list is absent."))
        .Split(Path.PathSeparator).Order(StringComparer.Ordinal)
        .Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>());

    public static MappingStudy Analyze(string document, string revision, string source)
    {
        var snapshot = new ProjectionSnapshot(document, revision, SourceText.From(source, Encoding.UTF8));
        var syntaxBodies = snapshot.Bodies().ToArray();
        var bodies = syntaxBodies.Select((body, index) => new ProjectedBody(
            "b" + index.ToString(CultureInfo.InvariantCulture), Callable(body), body.SpanStart,
            body.Span.Length, snapshot.Project(body))).ToImmutableArray();
        var builds = new[] { BuildMode.Debug, BuildMode.Release }
            .Select(mode => ReadBuild(snapshot, bodies, mode)).ToArray();
        return new(document, revision, bodies, builds.Select((build, index) =>
        {
            var elements = Classify(bodies, build, builds[1 - index]);
            return build with
            {
                Elements = elements,
                Points = build.Points.Select(point => point.Quality == MappingQuality.Exact
                    && point.Targets.All(target => elements.Any(e => e.BodyId == point.BodyId
                        && e.ElementId == target && e.Rule == MappingRule.OneConfigurationOnly))
                    ? point with { Quality = MappingQuality.Approximate, Rule = MappingRule.OneConfigurationOnly }
                    : point).ToImmutableArray()
            };
        }).ToImmutableArray());
    }

    private static string Callable(SyntaxNode body)
    {
        var owner = body.Parent is ArrowExpressionClauseSyntax arrow ? arrow.Parent : body.Parent;
        return owner switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            LocalFunctionStatementSyntax local => "local:" + local.Identifier.ValueText,
            LambdaExpressionSyntax => "lambda@" + body.SpanStart.ToString(CultureInfo.InvariantCulture),
            AnonymousMethodExpressionSyntax => "anonymous@" + body.SpanStart.ToString(CultureInfo.InvariantCulture),
            _ => owner?.Kind().ToString() ?? "body"
        };
    }

    private static BuildMapping ReadBuild(ProjectionSnapshot snapshot,
        ImmutableArray<ProjectedBody> bodies, BuildMode mode)
    {
        var compilation = CSharpCompilation.Create("MappingFixture", new[] { snapshot.Tree }, References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: mode == BuildMode.Debug ? OptimizationLevel.Debug : OptimizationLevel.Release,
                deterministic: true, nullableContextOptions: NullableContextOptions.Enable));
        using var assembly = new MemoryStream();
        using var symbols = new MemoryStream();
        var emitted = compilation.Emit(assembly, symbols,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        var diagnostics = emitted.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).ToArray();
        if (!emitted.Success || diagnostics.Length != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString())));
        assembly.Position = symbols.Position = 0;
        using var pe = new PEReader(assembly);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(symbols);
        var metadata = pe.GetMetadataReader();
        var pdb = provider.GetMetadataReader();
        var methods = new List<MethodEvidence>();
        var points = new List<PointMapping>();
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            var debug = pdb.GetMethodDebugInformation(handle);
            var token = MetadataTokens.GetToken(handle);
            var name = metadata.GetString(method.Name);
            var declaringType = metadata.GetTypeDefinition(method.GetDeclaringType());
            var typeName = metadata.GetString(declaringType.Name);
            var kickoff = debug.GetStateMachineKickoffMethod();
            var kickoffName = kickoff.IsNil ? null : metadata.GetString(metadata.GetMethodDefinition(kickoff).Name);
            var rawPoints = debug.GetSequencePoints().ToArray();
            var spans = rawPoints.Where(p => !p.IsHidden && DocumentName(p, debug, pdb) == snapshot.Document)
                .Select(p => Span(p, snapshot.Text)).ToArray();
            // A method's entire visible range must belong to one selected body. Nested callables
            // are separate candidates, so their points cannot spill into the enclosing graph.
            bool helper = kickoff.IsNil && (typeName.Contains('<', StringComparison.Ordinal) || name.Contains('<', StringComparison.Ordinal))
                && !name.Contains("b__", StringComparison.Ordinal) && !name.Contains("g__", StringComparison.Ordinal);
            var candidates = spans.Length == 0 || helper ? Array.Empty<ProjectedBody>() : bodies
                .Where(body => spans.All(span => new TextSpan(body.Start, body.Length).Contains(span)))
                .OrderBy(body => body.Length).ToArray();
            bool ambiguous = candidates.Length > 1 && candidates[0].Length == candidates[1].Length;
            var selected = ambiguous ? null : candidates.FirstOrDefault();
            string bodyRule = helper ? "generated-helper" : ambiguous ? "ambiguous-source-range"
                : selected is null ? "no-contained-source-range" : !kickoff.IsNil ? "state-machine-kickoff-and-source"
                : name.Contains("b__", StringComparison.Ordinal) ? "lambda-method-and-source"
                : name.Contains("g__", StringComparison.Ordinal) ? "local-function-method-and-source" : "method-source-range";
            var extras = pdb.GetCustomDebugInformation(handle).Select(extraHandle =>
            {
                var extra = pdb.GetCustomDebugInformation(extraHandle);
                return new PdbExtra(pdb.GetGuid(extra.Kind).ToString(), Convert.ToHexString(pdb.GetBlobBytes(extra.Value)));
            }).ToImmutableArray();
            methods.Add(new(token, typeName, name, method.RelativeVirtualAddress == 0 ? 0
                : pe.GetMethodBody(method.RelativeVirtualAddress).GetILContent().Length,
                kickoff.IsNil ? null : MetadataTokens.GetToken(kickoff), kickoffName,
                selected?.Id, bodyRule, extras));
            foreach (var point in rawPoints)
            {
                string document = DocumentName(point, debug, pdb);
                TextSpan? span = !point.IsHidden && document == snapshot.Document ? Span(point, snapshot.Text) : null;
                var overlaps = selected is null || span is null ? Array.Empty<GraphElement>()
                    : selected.Graph.Elements.Where(e => AnchorSpan(e).OverlapsWith(span.Value)).ToArray();
                var equals = overlaps.Where(e => AnchorSpan(e) == span).ToArray();
                var rule = point.IsHidden ? MappingRule.HiddenSequencePoint
                    : document != snapshot.Document ? MappingRule.UnknownDocument
                    : helper ? MappingRule.GeneratedHelper : ambiguous ? MappingRule.AmbiguousBody
                    : overlaps.Length == 0 ? MappingRule.OutsideProjectedBody
                    : equals.Length != 0 ? MappingRule.AnchorEquality : MappingRule.SpanIntersection;
                var quality = rule == MappingRule.AnchorEquality ? MappingQuality.Exact
                    : rule == MappingRule.SpanIntersection ? MappingQuality.Approximate : MappingQuality.Unavailable;
                points.Add(new(token, point.Offset, point.IsHidden, document, point.StartLine, point.StartColumn,
                    point.EndLine, point.EndColumn, span?.Start, span?.Length, selected?.Id, quality, rule,
                    (equals.Length != 0 ? equals : overlaps).Select(e => e.Id).ToImmutableArray()));
            }
        }
        // Kickoff methods can have no points of their own; the PDB supplies the link back from MoveNext.
        foreach (var stateMachine in methods.Where(m => m.KickoffToken is not null && m.BodyId is not null).ToArray())
        {
            int index = methods.FindIndex(m => m.Token == stateMachine.KickoffToken);
            if (index >= 0 && methods[index].BodyId is null)
                methods[index] = methods[index] with { BodyId = stateMachine.BodyId, BodyRule = "kickoff-from-state-machine" };
        }
        return new(mode, methods.ToImmutableArray(), points.ToImmutableArray(), ImmutableArray<ElementMapping>.Empty);
    }

    private static string DocumentName(SequencePoint point, MethodDebugInformation method, MetadataReader pdb)
    {
        var handle = point.Document.IsNil ? method.Document : point.Document;
        return handle.IsNil ? "" : pdb.GetString(pdb.GetDocument(handle).Name);
    }

    private static TextSpan Span(SequencePoint point, SourceText text) => TextSpan.FromBounds(
        text.Lines[point.StartLine - 1].Start + point.StartColumn - 1,
        text.Lines[point.EndLine - 1].Start + point.EndColumn - 1);

    private static TextSpan AnchorSpan(GraphElement element) => new(element.Anchor.Start, element.Anchor.Length);
    private static TextSpan PointSpan(PointMapping point) => new(point.Start!.Value, point.Length!.Value);
    public static string PointId(PointMapping point) => $"{point.MethodToken:X8}:{point.Offset:X4}";

    private static ImmutableArray<ElementMapping> Classify(ImmutableArray<ProjectedBody> bodies,
        BuildMapping build, BuildMapping other)
    {
        var results = ImmutableArray.CreateBuilder<ElementMapping>();
        foreach (var body in bodies)
        foreach (var element in body.Graph.Elements)
        {
            var anchor = AnchorSpan(element);
            var hits = build.Points.Where(p => p.BodyId == body.Id && p.Quality != MappingQuality.Unavailable
                && PointSpan(p).OverlapsWith(anchor)).ToArray();
            bool otherHasHit = other.Points.Any(p => p.BodyId == body.Id && p.Quality != MappingQuality.Unavailable
                && PointSpan(p).OverlapsWith(anchor));
            var rule = hits.Length == 0
                ? build.Methods.Any(m => m.BodyId == body.Id) ? MappingRule.NoVisibleSequencePoint : MappingRule.NoAssociatedMethod
                : !otherHasHit ? MappingRule.OneConfigurationOnly
                : hits.Any(p => PointSpan(p) == anchor) ? MappingRule.AnchorEquality
                : hits.Any(p => PointSpan(p).Contains(anchor)) ? MappingRule.SharedSequencePoint : MappingRule.SpanIntersection;
            var quality = hits.Length == 0 ? MappingQuality.Unavailable
                : rule == MappingRule.AnchorEquality ? MappingQuality.Exact : MappingQuality.Approximate;
            results.Add(new(body.Id, element.Id, quality, rule, hits.Select(PointId).ToImmutableArray()));
        }
        return results.ToImmutable();
    }

    public static StopMapping MapOffset(BuildMapping build, int methodToken, int offset)
    {
        StopMapping Missing(MappingRule rule) => new(methodToken, offset, null, null,
            MappingQuality.Unavailable, rule, ImmutableArray<string>.Empty);
        var method = build.Methods.FirstOrDefault(m => m.Token == methodToken);
        if (method is null) return Missing(MappingRule.UnknownMethod);
        if (offset < 0 || offset >= method.CodeSize) return Missing(MappingRule.OffsetOutsideMethod);
        var points = build.Points.Where(p => p.MethodToken == methodToken).OrderBy(p => p.Offset).ToArray();
        if (points.Length == 0) return Missing(MappingRule.NoSequencePoints);
        var point = points.LastOrDefault(p => p.Offset <= offset);
        if (point is null) return Missing(MappingRule.BeforeFirstPoint);
        // Hidden intervals stay hidden. A nearby visible range cannot justify highlighting them.
        var quality = point.Quality == MappingQuality.Unavailable ? MappingQuality.Unavailable
            : offset == point.Offset ? point.Quality : MappingQuality.Approximate;
        var rule = point.Quality == MappingQuality.Unavailable || offset == point.Offset ? point.Rule : MappingRule.BetweenPoints;
        return new(methodToken, offset, point.Offset, point.BodyId, quality, rule, point.Targets);
    }
}
