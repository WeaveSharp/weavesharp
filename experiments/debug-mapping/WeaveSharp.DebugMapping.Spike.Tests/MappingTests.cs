using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using WeaveSharp.DebugMapping.Spike;
using WeaveSharp.Projection.Spike;
using Xunit;

namespace WeaveSharp.DebugMapping.Spike.Tests;

public sealed class MappingTests
{
    public static IEnumerable<object[]> Fixtures() => Directory.GetFiles(
        Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.cs")
        .Order(StringComparer.Ordinal).Select(path => new object[] { Path.GetFileName(path) });

    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    private static MappingStudy Study(string name) => SequencePointStudy.Analyze(name, "fixture-v1", Read(name));

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_element_and_point_is_accounted_for_and_classifications_are_pinned(string name)
    {
        var study = Study(name);
        StudyReport.Write(study, Path.Combine(AppContext.BaseDirectory, "mapping-reports"));
        var projection = new ProjectionSnapshot(name, "fixture-v1", Read(name));
        Assert.Equal(projection.Bodies().Count(), study.Bodies.Length);
        Assert.Equal(2, study.Builds.Length);
        foreach (var build in study.Builds)
        {
            Assert.Equal(PdbInventory.Read(name, Read(name), build.Mode), build.Points.Select(p => new PdbInventory.Entry(
                p.MethodToken, p.Offset, p.Hidden, p.Document, p.StartLine, p.StartColumn, p.EndLine, p.EndColumn)));
            Assert.Equal(study.Bodies.Sum(b => b.Graph.Elements.Length), build.Elements.Length);
            Assert.NotEmpty(build.Points);
            Assert.Equal(build.Points.Length, build.Points.Select(SequencePointStudy.PointId).Distinct().Count());
            foreach (var point in build.Points)
            {
                Assert.Contains(build.Methods, m => m.Token == point.MethodToken);
                var lookup = SequencePointStudy.MapOffset(build, point.MethodToken, point.Offset);
                Assert.Equal(point.Quality, lookup.Quality);
                Assert.Equal(point.Rule, lookup.Rule);
                Assert.Equal(point.Targets, lookup.Targets);
                if (point.Quality == MappingQuality.Unavailable)
                {
                    Assert.Empty(point.Targets);
                    Assert.NotEqual(MappingRule.AnchorEquality, point.Rule);
                    continue;
                }
                var body = Assert.Single(study.Bodies, b => b.Id == point.BodyId);
                Assert.NotEmpty(point.Targets);
                Assert.All(point.Targets, target => Assert.Contains(body.Graph.Elements, e => e.Id == target));
            }
            foreach (var entry in build.Elements)
            {
                Assert.True(Enum.IsDefined(entry.Rule));
                Assert.Equal(entry.Quality == MappingQuality.Unavailable, entry.Points.IsEmpty);
                Assert.All(entry.Points, id => Assert.Contains(build.Points,
                    p => SequencePointStudy.PointId(p) == id && p.BodyId == entry.BodyId && !p.Hidden));
            }
        }
        string expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expectations",
            Path.GetFileNameWithoutExtension(name) + ".txt")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, StudyReport.Classifications(study));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Repeated_compilation_and_mapping_is_deterministic(string name)
    {
        Assert.Equal(Study(name).ToJson(), Study(name).ToJson());
    }

    [Theory]
    [InlineData("Async.cs", "Collect")]
    [InlineData("Iterator.cs", "Count")]
    public void State_machine_metadata_recovers_the_source_body(string name, string kickoff)
    {
        var study = Study(name);
        foreach (var build in study.Builds)
        {
            var moveNext = Assert.Single(build.Methods, m => m.KickoffName == kickoff);
            Assert.Equal("MoveNext", moveNext.Name);
            Assert.NotNull(moveNext.BodyId);
            Assert.Equal("state-machine-kickoff-and-source", moveNext.BodyRule);
            Assert.Equal(moveNext.BodyId, Assert.Single(build.Methods, m => m.Token == moveNext.KickoffToken).BodyId);
            Assert.Contains(build.Points, p => p.MethodToken == moveNext.Token && !p.Hidden && p.Targets.Length > 0);
            Assert.Contains(build.Points, p => p.MethodToken == moveNext.Token && p.Hidden);
        }
    }

    [Fact]
    public void Nested_callables_keep_their_own_methods_and_graphs()
    {
        var study = Study("Callables.cs");
        foreach (var build in study.Builds)
        {
            Assert.Equal(2, build.Methods.Count(m => m.BodyRule == "lambda-method-and-source"));
            Assert.Contains(build.Methods, m => m.BodyRule == "local-function-method-and-source");
            var nestedAsync = Assert.Single(build.Methods, m => m.KickoffName?.Contains("g__Later", StringComparison.Ordinal) == true);
            Assert.Equal("local:Later", Assert.Single(study.Bodies, b => b.Id == nestedAsync.BodyId).Callable);
            foreach (var method in build.Methods.Where(m => m.BodyRule == "lambda-method-and-source"))
            {
                var body = Assert.Single(study.Bodies, b => b.Id == method.BodyId);
                Assert.StartsWith("lambda@", body.Callable);
                Assert.All(build.Points.Where(p => p.MethodToken == method.Token), p => Assert.Equal(body.Id, p.BodyId));
            }
        }
        // Roslyn emits the EnC lambda/closure map in the non-optimized PDB.
        Assert.Contains(study.Builds[0].Methods.SelectMany(m => m.CustomDebugInformation),
            info => info.Kind == "a643004c-0240-496f-a783-30d64f4979de" && info.Bytes.Length > 0);
    }

    [Fact]
    public void Optimized_away_points_do_not_make_debug_only_nodes_exact()
    {
        var study = Study("Optimization.cs");
        var emptyStatements = study.Bodies.SelectMany(b => b.Graph.Elements
            .Where(e => e.Anchor.SyntaxKind == nameof(SyntaxKind.EmptyStatement)).Select(e => (b.Id, e.Id)));
        foreach (var (body, element) in emptyStatements)
        {
            var debug = Assert.Single(study.Builds[0].Elements, e => e.BodyId == body && e.ElementId == element);
            Assert.Equal(MappingQuality.Approximate, debug.Quality);
            Assert.Equal(MappingRule.OneConfigurationOnly, debug.Rule);
            Assert.Equal(MappingQuality.Unavailable,
                Assert.Single(study.Builds[1].Elements, e => e.BodyId == body && e.ElementId == element).Quality);
        }
    }

    [Fact]
    public void Offset_lookup_keeps_hidden_intervals_and_marks_in_between_locations_approximate()
    {
        foreach (var build in Study("Async.cs").Builds)
        {
            bool sawHidden = false, sawBetween = false;
            foreach (var method in build.Methods)
            for (int offset = 0; offset < method.CodeSize; offset++)
            {
                var found = SequencePointStudy.MapOffset(build, method.Token, offset);
                if (found.Rule == MappingRule.HiddenSequencePoint)
                {
                    sawHidden = true;
                    Assert.Equal(MappingQuality.Unavailable, found.Quality);
                }
                if (found.Rule == MappingRule.BetweenPoints)
                {
                    sawBetween = true;
                    Assert.Equal(MappingQuality.Approximate, found.Quality);
                }
            }
            Assert.True(sawHidden);
            Assert.True(sawBetween);
            Assert.Equal(MappingRule.UnknownMethod, SequencePointStudy.MapOffset(build, -1, 0).Rule);
            Assert.Equal(MappingRule.OffsetOutsideMethod, SequencePointStudy.MapOffset(build, build.Methods[0].Token, -1).Rule);
        }
    }

    [Fact]
    public void Source_columns_use_utf16_and_newlines_do_not_change_qualities()
    {
        const string source = "static class Accent { static string Text() { string value = \"🪁\";\nreturn value; } }";
        var lf = SequencePointStudy.Analyze("Accent.cs", "lf", source);
        var crlf = SequencePointStudy.Analyze("Accent.cs", "crlf", source.Replace("\n", "\r\n", StringComparison.Ordinal));
        foreach (var build in lf.Builds)
        {
            var other = crlf.Builds.Single(b => b.Mode == build.Mode);
            Assert.Equal(build.Elements.Select(e => (e.Quality, e.Rule)), other.Elements.Select(e => (e.Quality, e.Rule)));
            var declaration = Assert.Single(build.Points, p => p.Start == source.IndexOf("string value", StringComparison.Ordinal));
            Assert.Equal("string value = \"🪁\";".Length, declaration.Length);
        }
    }

    [Fact]
    public void Hidden_only_and_redirected_documents_are_explicit_gaps()
    {
        const string source = """
            static class Directives
            {
            #line hidden
                static int Silent(int value) { return value + 2; }
            #line 50 "elsewhere.cs"
                static int Elsewhere(int value) { return value + 3; }
            #line default
            }
            """;
        var study = SequencePointStudy.Analyze("Directives.cs", "directives-v1", source);
        foreach (var build in study.Builds)
        {
            Assert.All(build.Elements, e =>
            {
                Assert.Equal(MappingQuality.Unavailable, e.Quality);
                Assert.Equal(MappingRule.NoAssociatedMethod, e.Rule);
            });
            Assert.Contains(build.Points, p => p.Rule == MappingRule.UnknownDocument);
            Assert.All(build.Points, p => Assert.Equal(MappingQuality.Unavailable, p.Quality));
            var silent = Assert.Single(build.Methods, m => m.Name == "Silent");
            Assert.Equal(MappingRule.NoSequencePoints, SequencePointStudy.MapOffset(build, silent.Token, 0).Rule);
        }
    }
}
