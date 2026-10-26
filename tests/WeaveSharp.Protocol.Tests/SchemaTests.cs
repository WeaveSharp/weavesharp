using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace WeaveSharp.Protocol.Tests;

public sealed class SchemaTests
{
    private static readonly Dictionary<string, Type> Families = new(StringComparer.Ordinal)
    {
        ["initialize"] = typeof(InitializePayload),
        ["set-compilation-context"] = typeof(CompilationContextPayload),
        ["publish-source-snapshot"] = typeof(SourceSnapshotPayload),
        ["request-graph"] = typeof(RequestGraphPayload),
        ["publish-graph"] = typeof(PublishGraphPayload),
        ["prepare-edit"] = typeof(PrepareEditPayload),
        ["edit-result"] = typeof(EditResultPayload),
        ["publish-execution-state"] = typeof(ExecutionStatePayload),
        ["read-or-save-layout"] = typeof(LayoutPayload),
        ["cancel-or-dispose"] = typeof(CancelOrDisposePayload)
    };
    private static string Examples => Path.Combine(AppContext.BaseDirectory, "Examples");
    private static string Schemas => Path.Combine(AppContext.BaseDirectory, "Schemas");
    private static FixtureSchema Checker => new(Schemas);

    public static IEnumerable<object[]> Fixtures() => Directory.GetFiles(Examples, "*.json", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => new object[] { Path.GetRelativePath(Examples, path) });

    [Fact]
    public void EveryArc06FamilyHasItsOwnSchemaAndBothKindsOfExample()
    {
        Assert.Equal(10, Families.Count);
        Assert.Equal(Families.Keys.Order(), Directory.GetDirectories(Examples).Select(Path.GetFileName).Order());
        Assert.Equal(Families.Keys.Append("envelope").Select(f => f + ".schema.json").Order(),
            Directory.GetFiles(Schemas, "*.json").Select(Path.GetFileName).Order());
        foreach (var family in Families.Keys)
        {
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(Examples, family), "valid-*.json"));
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(Examples, family), "invalid-*.json"));
        }
        _ = Checker; // Audits the complete vocabulary, including unused definitions.
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ExamplesMatchTheirDeclaredExpectation(string relativePath)
    {
        var value = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(Examples, relativePath)));
        var family = Path.GetDirectoryName(relativePath)!;
        bool valid = Path.GetFileName(relativePath).StartsWith("valid-", StringComparison.Ordinal);
        foreach (var schema in new[] { family + ".schema.json", "envelope.schema.json" })
        {
            var errors = Checker.Validate(schema, value);
            Assert.True(valid == (errors.Count == 0), $"{relativePath} against {schema}: {string.Join("; ", errors)}");
        }
        if (!valid)
            return;
        var options = ProtocolJson.CreateOptions();
        var payload = JsonSerializer.Deserialize(value.GetProperty("payload"), Families[family], options)!;
        var original = JsonSerializer.Deserialize<Envelope<JsonElement>>(value, options)!;
        var roundTrip = new Envelope<object>(original.Version, original.Family, original.RequestId,
            original.Kind, payload, original.Extensions);
        var serialized = JsonSerializer.SerializeToElement(roundTrip, options);
        Assert.Empty(Checker.Validate(family + ".schema.json", serialized));
        Assert.True(JsonElement.DeepEquals(value, serialized), $"Record serialization changed {relativePath}");
    }

    [Theory]
    [InlineData("{\"type\":\"string\",\"maxLength\":8,\"format\":\"uri\"}")]
    [InlineData("{\"type\":\"string\"}")]
    [InlineData("{\"type\":\"array\",\"items\":{\"type\":\"string\",\"maxLength\":8}}")]
    [InlineData("{\"type\":\"object\",\"maxProperties\":2,\"additionalProperties\":true}")]
    [InlineData("{\"$defs\":{\"unused\":{\"format\":\"uri\"}}}")]
    public void UnsupportedKeywordsAndUnboundedShapesStopTheFixtureChecker(string schema)
    {
        Assert.Throws<InvalidOperationException>(() => FixtureSchema.CheckVocabulary(JsonSerializer.Deserialize<JsonElement>(schema), "test"));
    }

    [Theory]
    [InlineData("requestId", "")]
    [InlineData("requestId", "oversized")]
    [InlineData("family", "new-family")]
    [InlineData("kind", "mutation")]
    public void EnvelopeDiscriminatorsAndStringBoundsAreChecked(string property, string value)
    {
        var message = Read("request-graph");
        message[property] = value == "oversized" ? new string('x', 129) : value;
        AssertInvalid(message);
    }

    [Theory]
    [InlineData("/workspace/Counter.cs")]
    [InlineData("C:\\project\\Counter.cs")]
    [InlineData("workspace://bad name")]
    public void DocumentReferencesRequireUriNotation(string uri)
    {
        var message = Read("request-graph");
        message["payload"]!["context"]!["snapshot"]!["documentUri"] = uri;
        AssertInvalid(message);
    }

    [Fact]
    public void NestedTypesBoundsAndClosedPropertiesAreChecked()
    {
        var message = Read("request-graph");
        message["payload"]!["body"]!["start"] = -1;
        AssertInvalid(message);
        message["payload"]!["body"]!["start"] = 1.5;
        AssertInvalid(message);
        message["payload"]!["body"]!["start"] = 2147483648L;
        AssertInvalid(message);
        message["payload"]!["body"]!["start"] = "0";
        AssertInvalid(message);
        message = Read("request-graph");
        message["payload"]!["body"]!["coordinates"] = "byte-offset";
        AssertInvalid(message);
        message = Read("request-graph");
        message["payload"]!["body"]!["extra"] = true;
        AssertInvalid(message);
        message = Read("request-graph");
        message["extra"] = false;
        AssertInvalid(message);
    }

    [Fact]
    public void ArrayBoundsAndUniquenessAreChecked()
    {
        var message = Read("initialize");
        message["payload"]!["versions"] = new JsonArray();
        AssertInvalid(message);
        message = Read("initialize");
        message["payload"]!["capabilities"] = new JsonArray("same", "same");
        AssertInvalid(message);
        message["payload"]!["capabilities"] = new JsonArray(Enumerable.Range(0, 129).Select(i => JsonValue.Create(i.ToString())).ToArray<JsonNode?>());
        AssertInvalid(message);
    }

    [Fact]
    public void ExtensionsAreBoundedAndMustDeclareWhetherTheyAreRequired()
    {
        var message = Read("initialize");
        message["extensions"] = new JsonObject { [new string('a', 65)] = new JsonObject { ["mandatory"] = false, ["value"] = "x" } };
        AssertInvalid(message);
        var extensions = new JsonObject();
        for (int i = 0; i < 33; i++)
            extensions["item-" + i] = new JsonObject { ["mandatory"] = false, ["value"] = "x" };
        message["extensions"] = extensions;
        AssertInvalid(message);
        message["extensions"] = new JsonObject { ["future"] = new JsonObject { ["mandatory"] = "false", ["value"] = "x" } };
        AssertInvalid(message);
    }

    [Fact]
    public void StringLengthsCountUnicodeScalars()
    {
        var message = Read("request-graph");
        message["requestId"] = string.Concat(Enumerable.Repeat("\U0001F642", 128));
        Assert.Empty(Checker.Validate("envelope.schema.json", JsonSerializer.SerializeToElement(message)));
        message["requestId"] = string.Concat(Enumerable.Repeat("\U0001F642", 129));
        AssertInvalid(message);
    }

    private static JsonNode Read(string family) => JsonNode.Parse(File.ReadAllText(Path.Combine(Examples, family, "valid-basic.json")))!;
    private static void AssertInvalid(JsonNode message) => Assert.NotEmpty(Checker.Validate("envelope.schema.json", JsonSerializer.SerializeToElement(message)));
}
