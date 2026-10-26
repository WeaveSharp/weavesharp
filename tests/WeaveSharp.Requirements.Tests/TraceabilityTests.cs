using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace WeaveSharp.Requirements.Tests;

public sealed class TraceabilityTests
{
    private static readonly string RegisterText = File.ReadAllText(Locate("register.json"));
    private static readonly string SchemaText = File.ReadAllText(Locate("register.schema.json"));

    public static IEnumerable<object[]> RequirementIds =>
        DesignBaseline.Requirements.Select(requirement => new object[] { requirement.Id });

    [Fact]
    public void RegisterSatisfiesSchemaInventoryAndTraceability()
    {
        Assert.Empty(RegisterValidator.Validate(RegisterText, SchemaText));
    }

    [Theory]
    [MemberData(nameof(RequirementIds))]
    public void MissingGatesNameTheRequirement(string id)
    {
        JsonObject copy = Copy();
        Requirement(copy, id)["gates"] = new JsonArray();
        AssertFailure(copy, id, "gates");
    }

    [Theory]
    [MemberData(nameof(RequirementIds))]
    public void MissingCaseAndNonTestAssessmentNameTheRequirement(string id)
    {
        JsonObject copy = Copy();
        JsonObject requirement = Requirement(copy, id);
        requirement["acceptanceCases"] = new JsonArray();
        requirement["assessment"] = "executable";
        AssertFailure(copy, id, "needs an acceptance case");
    }

    [Theory]
    [InlineData("family")]
    [InlineData("title")]
    [InlineData("section")]
    [InlineData("acceptanceCases")]
    [InlineData("gates")]
    [InlineData("assessment")]
    public void MissingFieldsNameTheRequirement(string field)
    {
        JsonObject copy = Copy();
        Requirement(copy, "FND-01").Remove(field);
        AssertFailure(copy, "FND-01", $"missing field {field}");
    }

    [Theory]
    [InlineData("gates")]
    [InlineData("acceptanceCases")]
    [InlineData("requirements")]
    public void RootCollectionsAreRequired(string collection)
    {
        JsonObject copy = Copy();
        copy.Remove(collection);
        AssertFailure(copy, "missing field", collection);
    }

    [Theory]
    [InlineData("gates", "name")]
    [InlineData("gates", "id")]
    [InlineData("acceptanceCases", "title")]
    [InlineData("acceptanceCases", "id")]
    [InlineData("requirements", "id")]
    public void CatalogFieldsAreRequired(string collection, string field)
    {
        JsonObject copy = Copy();
        copy[collection]![0]!.AsObject().Remove(field);
        AssertFailure(copy, collection, $"missing field {field}");
    }

    [Theory]
    [InlineData("FND-04")]
    [InlineData("TRC-12")]
    [InlineData("QUAL-08")]
    public void RemovingAnInteriorOrFinalIdentifierFails(string id)
    {
        JsonObject copy = Copy();
        copy["requirements"]!.AsArray().Remove(Requirement(copy, id));
        AssertFailure(copy, id, "missing from requirements");
    }

    [Theory]
    [InlineData("gates", "G-01")]
    [InlineData("acceptanceCases", "AC-01")]
    [InlineData("requirements", "FND-01")]
    public void DuplicateIdentifiersFail(string collection, string id)
    {
        JsonObject copy = Copy();
        JsonArray entries = copy[collection]!.AsArray();
        entries.Add(entries[0]!.DeepClone());
        AssertFailure(copy, id, "repeated identifier");
    }

    [Fact]
    public void AdditionalRequirementsFail()
    {
        JsonObject copy = Copy();
        JsonObject extra = Requirement(copy, "FND-01").DeepClone().AsObject();
        extra["id"] = "FND-09";
        copy["requirements"]!.AsArray().Add(extra);
        AssertFailure(copy, "FND-09", "outside the design inventory");
    }

    [Theory]
    [InlineData("gates", "G-99", "unknown gate")]
    [InlineData("acceptanceCases", "AC-99", "unknown acceptance case")]
    public void UnknownReferencesNameTheRequirement(string field, string link, string diagnostic)
    {
        JsonObject copy = Copy();
        Requirement(copy, "FND-01")[field]![0] = link;
        AssertFailure(copy, "FND-01", diagnostic);
    }

    [Fact]
    public void AnUnreferencedCaseFails()
    {
        JsonObject copy = Copy();
        foreach (JsonNode? item in copy["requirements"]!.AsArray())
        {
            JsonArray cases = item!["acceptanceCases"]!.AsArray();
            JsonNode? link = cases.FirstOrDefault(value => value!.GetValue<string>() == "AC-01");
            if (link is not null)
            {
                cases.Remove(link);
            }
        }
        AssertFailure(copy, "AC-01", "no requirement references");
    }

    [Theory]
    [InlineData("gates", "G-02", "gate links differ")]
    [InlineData("acceptanceCases", "AC-03", "acceptance links differ")]
    public void ExistingButIncorrectReferencesFail(string field, string link, string diagnostic)
    {
        JsonObject copy = Copy();
        Requirement(copy, "FND-01")[field]![0] = link;
        AssertFailure(copy, "FND-01", diagnostic);
    }

    [Theory]
    [InlineData("family", "SRC", "family must")]
    [InlineData("section", "3", "defining section")]
    [InlineData("assessment", "unknown", "unknown value")]
    [InlineData("id", "FND-1", "invalid text")]
    [InlineData("title", "", "invalid text")]
    [InlineData("title", "   ", "invalid text")]
    [InlineData("title", "one two three four five six seven eight nine ten eleven twelve thirteen", "invalid text")]
    public void InvalidRequirementFieldsFail(string field, string value, string diagnostic)
    {
        JsonObject copy = Copy();
        Requirement(copy, "FND-01")[field] = value;
        AssertFailure(copy, field == "id" ? value : "FND-01", diagnostic);
    }

    [Fact]
    public void TwelveWordTitlesAreAccepted()
    {
        JsonObject copy = Copy();
        Requirement(copy, "FND-01")["title"] = "one two three four five six seven eight nine ten eleven twelve";
        Assert.Empty(RegisterValidator.Validate(copy.ToJsonString(), SchemaText));
    }

    [Theory]
    [InlineData("gates")]
    [InlineData("acceptanceCases")]
    public void NullAndDuplicateLinksFail(string field)
    {
        JsonObject copy = Copy();
        JsonArray links = Requirement(copy, "FND-01")[field]!.AsArray();
        links.Add(links[0]!.DeepClone());
        AssertFailure(copy, "FND-01", "duplicate item");
        Requirement(copy, "FND-01")[field] = null;
        AssertFailure(copy, "FND-01", "expected array");
    }

    [Fact]
    public void UnknownFieldsAndMalformedJsonFail()
    {
        JsonObject copy = Copy();
        Requirement(copy, "FND-01")["extra"] = true;
        AssertFailure(copy, "FND-01", "unexpected field extra");
        Assert.Contains(RegisterValidator.Validate("{", SchemaText), error => error.Contains("Invalid JSON", StringComparison.Ordinal));
    }

    [Fact]
    public void UnsupportedSchemaKeywordsFail()
    {
        JsonObject schema = JsonNode.Parse(SchemaText)!.AsObject();
        schema["maxProperties"] = 3;
        Assert.Contains(RegisterValidator.Validate(RegisterText, schema.ToJsonString()),
            error => error.Contains("unsupported keyword maxProperties", StringComparison.Ordinal));
    }

    private static JsonObject Copy() => JsonNode.Parse(RegisterText)!.AsObject();
    private static JsonObject Requirement(JsonObject copy, string id) => copy["requirements"]!.AsArray()
        .Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();

    private static void AssertFailure(JsonObject copy, string id, string diagnostic) =>
        Assert.Contains(RegisterValidator.Validate(copy.ToJsonString(), SchemaText),
            error => error.Contains(id, StringComparison.Ordinal) && error.Contains(diagnostic, StringComparison.Ordinal));

    private static string Locate(string file)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WeaveSharp.slnx")))
            {
                return Path.Combine(directory.FullName, "docs", "requirements", file);
            }
        }
        throw new DirectoryNotFoundException("The test output must be inside the WeaveSharp solution checkout.");
    }
}
