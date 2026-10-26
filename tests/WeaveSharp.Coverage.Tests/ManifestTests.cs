using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace WeaveSharp.Coverage.Tests;

public sealed class ManifestTests
{
    private static JsonObject Load(string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "coverage", file)))!.AsObject();
    private static JsonObject Manifest() => Load("csharp-14.json");
    private static JsonObject Schema() => Load("csharp-14.schema.json");
    private static JsonObject Feature(JsonObject manifest, string id) => manifest["features"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == id)!.AsObject();
    private static List<string> Check(JsonObject manifest) => ManifestCheck.Validate(manifest, Schema());

    private static void NamesError(IEnumerable<string> errors, string name) => Assert.Contains(errors, error => error.Contains(name, StringComparison.Ordinal));

    [Fact]
    public void ManifestMeetsSchemaAndRoslynInventory()
    {
        Assert.Empty(Check(Manifest()));
    }

    [Fact]
    public void RemovingAMappedKindReportsItsName()
    {
        var manifest = Manifest();
        Feature(manifest, "compilation-units")["syntaxKinds"]!.AsArray().Clear();
        NamesError(Check(manifest), nameof(SyntaxKind.CompilationUnit));
    }

    [Fact]
    public void NewlyIntroducedRoslynKindsAreReportedIndividually()
    {
        var kinds = Enum.GetNames<SyntaxKind>().Concat(["FutureSyntaxOne", "FutureSyntaxTwo"]);
        var errors = ManifestCheck.Validate(Manifest(), Schema(), kinds);
        NamesError(errors, "FutureSyntaxOne");
        NamesError(errors, "FutureSyntaxTwo");
    }

    [Fact]
    public void RemovingAClassifiedKindReportsItsName()
    {
        var manifest = Manifest();
        var entries = manifest["classifiedSyntaxKinds"]!.AsArray();
        var removed = entries.Single(node => node!["syntaxKind"]!.GetValue<string>() == nameof(SyntaxKind.PlusToken));
        entries.Remove(removed);
        NamesError(Check(manifest), nameof(SyntaxKind.PlusToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownKindsAreRejectedInEitherLocation(bool classification)
    {
        var manifest = Manifest();
        if (classification)
            manifest["classifiedSyntaxKinds"]!.AsArray().Add(new JsonObject { ["syntaxKind"] = "ImaginaryKind", ["category"] = "token" });
        else
            Feature(manifest, "compilation-units")["syntaxKinds"]!.AsArray().Add("ImaginaryKind");
        NamesError(Check(manifest), "ImaginaryKind");
    }

    [Theory]
    [InlineData("preserve")]
    [InlineData("render")]
    [InlineData("dedicatedAuthoring")]
    [InlineData("semanticValidation")]
    [InlineData("debugMapping")]
    [InlineData("traceObservation")]
    public void EachDimensionIsRequiredAndUnverified(string dimension)
    {
        var missing = Manifest();
        Feature(missing, "compilation-units")["dimensions"]!.AsObject().Remove(dimension);
        var missingErrors = Check(missing);
        NamesError(missingErrors, dimension);
        NamesError(missingErrors, "compilation-units");

        var changed = Manifest();
        Feature(changed, "compilation-units")["dimensions"]![dimension]!["status"] = "supported";
        NamesError(Check(changed), dimension);
    }

    [Fact]
    public void EveryFamilyMustKeepAFeature()
    {
        foreach (string family in Manifest()["families"]!.AsArray().Select(node => node!["id"]!.GetValue<string>()))
        {
            var manifest = Manifest();
            var features = manifest["features"]!.AsArray();
            foreach (var feature in features.Where(node => node!["family"]!.GetValue<string>() == family).ToArray())
                features.Remove(feature);
            NamesError(Check(manifest), family);
        }
    }

    [Fact]
    public void EachCSharp14AdditionIsRequired()
    {
        foreach (string id in Manifest()["features"]!.AsArray().Where(node => node!["family"]!.GetValue<string>() == "csharp-14-additions").Select(node => node!["id"]!.GetValue<string>()))
        {
            var manifest = Manifest();
            manifest["features"]!.AsArray().Remove(Feature(manifest, id));
            NamesError(Check(manifest), id);
        }
    }

    [Fact]
    public void CSharp14AdditionsKeepTheirVersionAndTreatment()
    {
        var manifest = Manifest();
        Feature(manifest, "extension-members")["languageVersion"] = "13";
        NamesError(Check(manifest), "extension-members");
    }

    [Fact]
    public void FamilyMaturityCannotBeReplacedWithFallback()
    {
        var manifest = Manifest();
        Feature(manifest, "compilation-units")["plannedMaturity"] = "source-region";
        NamesError(Check(manifest), "compilation-units");
    }

    [Fact]
    public void AccidentalFeatureOverlapIsRejected()
    {
        var manifest = Manifest();
        Feature(manifest, "namespace-declarations")["syntaxKinds"]!.AsArray().Add(nameof(SyntaxKind.CompilationUnit));
        NamesError(Check(manifest), nameof(SyntaxKind.CompilationUnit));
    }

    [Fact]
    public void ExplicitSharingAllowsMultipleFeatures()
    {
        var manifest = Manifest();
        Feature(manifest, "namespace-declarations")["syntaxKinds"]!.AsArray().Add(nameof(SyntaxKind.CompilationUnit));
        manifest["sharedSyntaxKinds"]!.AsArray().Add(nameof(SyntaxKind.CompilationUnit));
        Assert.Empty(Check(manifest));
    }

    [Fact]
    public void SharedMarkerAloneDoesNotAccountForAKind()
    {
        var manifest = Manifest();
        Feature(manifest, "compilation-units")["syntaxKinds"]!.AsArray().Clear();
        manifest["sharedSyntaxKinds"]!.AsArray().Add(nameof(SyntaxKind.CompilationUnit));
        NamesError(Check(manifest), nameof(SyntaxKind.CompilationUnit));
    }

    [Fact]
    public void ClassificationCannotOverlapAFeature()
    {
        var manifest = Manifest();
        manifest["classifiedSyntaxKinds"]!.AsArray().Add(new JsonObject { ["syntaxKind"] = nameof(SyntaxKind.CompilationUnit), ["category"] = "other", ["note"] = "Intentional invalid overlap for this test." });
        NamesError(Check(manifest), nameof(SyntaxKind.CompilationUnit));
    }

    [Fact]
    public void ClassificationsCannotBeRepeatedWithDifferentReasons()
    {
        var manifest = Manifest();
        manifest["classifiedSyntaxKinds"]!.AsArray().Add(new JsonObject { ["syntaxKind"] = nameof(SyntaxKind.PlusToken), ["category"] = "other", ["note"] = "Intentional invalid second classification." });
        NamesError(Check(manifest), nameof(SyntaxKind.PlusToken));
    }

    [Fact]
    public void OtherClassificationRequiresAnExplanation()
    {
        var manifest = Manifest();
        var entry = manifest["classifiedSyntaxKinds"]!.AsArray().First(node => node!["category"]!.GetValue<string>() == "other")!.AsObject();
        string kind = entry["syntaxKind"]!.GetValue<string>();
        entry.Remove("note");
        NamesError(Check(manifest), kind);
    }

    [Fact]
    public void ANewNamedLanguageVersionRequiresBaselineReview()
    {
        var versions = Enum.GetNames<LanguageVersion>().Append("CSharp15");
        NamesError(ManifestCheck.Validate(Manifest(), Schema(), languageVersions: versions), "CSharp15");
    }

    [Fact]
    public void AliasesDoNotCountAsNewLanguageVersions()
    {
        var versions = Enum.GetNames<LanguageVersion>().Concat(["Preview", "Latest", "LatestMajor", "Default"]);
        Assert.Empty(ManifestCheck.Validate(Manifest(), Schema(), languageVersions: versions));
    }

    [Fact]
    public void DuplicateFeatureIdsAreRejected()
    {
        var manifest = Manifest();
        Feature(manifest, "namespace-declarations")["id"] = "compilation-units";
        NamesError(Check(manifest), "compilation-units");
    }

    [Theory]
    [InlineData("id")]
    [InlineData("family")]
    [InlineData("name")]
    [InlineData("languageVersion")]
    [InlineData("plannedMaturity")]
    [InlineData("syntaxKinds")]
    [InlineData("dimensions")]
    public void FeatureFieldsCannotBeOmitted(string field)
    {
        var manifest = Manifest();
        Feature(manifest, "compilation-units").Remove(field);
        NamesError(Check(manifest), field);
    }

    [Fact]
    public void MisspelledFieldsAndInvalidShapesAreReported()
    {
        var manifest = Manifest();
        Feature(manifest, "compilation-units")["dimensions"]!["rendr"] = new JsonObject { ["status"] = "unverified" };
        Feature(manifest, "namespace-declarations")["syntaxKinds"] = "NamespaceDeclaration";
        var errors = Check(manifest);
        NamesError(errors, "rendr");
        NamesError(errors, "namespace-declarations");
    }

    [Fact]
    public void SchemaChangesCannotIntroduceSilentlyIgnoredRules()
    {
        var schema = Schema();
        schema["unsupportedRule"] = true;
        Assert.Throws<InvalidOperationException>(() => ManifestCheck.Validate(Manifest(), schema));
    }
}
