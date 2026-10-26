using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace WeaveSharp.Transformations.Tests;

public sealed class CommandContractTests
{
    private static readonly string[] FamilyIds =
    [
        "change-declaration", "change-presentation", "connect-execution", "connect-value",
        "delete-operation", "disconnect-value", "duplicate-source", "extract-reusable-code",
        "insert-operation", "move-execution-order"
    ];

    [Fact]
    public void EveryFamilyDefinesItsMeaningPrerequisitesChecksAndPreview()
    {
        using var document = Read("contracts.json");
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        var commands = document.RootElement.GetProperty("commands").EnumerateArray().ToArray();
        Assert.Equal(FamilyIds, commands.Select(c => c.GetProperty("id").GetString()).Order(StringComparer.Ordinal));

        foreach (var command in commands)
        {
            Assert.Matches("^[a-z]+(?:-[a-z]+)*$", command.GetProperty("id").GetString()!);
            RequireText(command.GetProperty("name"));
            RequireText(command.GetProperty("sourceMeaning"));
            RequireTextList(command.GetProperty("preconditions"));
            RequireTextList(command.GetProperty("requiredChecks"));
            var requirements = command.GetProperty("requirement").EnumerateArray().Select(r => r.GetString()).ToArray();
            Assert.Contains("GRF-18", requirements);
            Assert.Equal(requirements.Length, requirements.Distinct().Count());
            Assert.All(requirements, id => Assert.Matches("^[A-Z]+-[0-9]{2}$", id!));

            var behavior = command.GetProperty("behaviorChange");
            RequireText(behavior.GetProperty("previewRequirement"));
            bool mayChange = behavior.GetProperty("mayChange").GetBoolean();
            string? id = command.GetProperty("id").GetString();
            Assert.Equal(id is not ("change-presentation" or "extract-reusable-code"), mayChange);
        }
    }

    [Fact]
    public void SchemaRequiresEveryFamilyAndContractField()
    {
        using var schema = Read("contracts.schema.json");
        var commands = schema.RootElement.GetProperty("properties").GetProperty("commands");
        Assert.Equal(FamilyIds.Length, commands.GetProperty("minItems").GetInt32());
        Assert.Equal(FamilyIds.Length, commands.GetProperty("maxItems").GetInt32());
        var ids = commands.GetProperty("allOf").EnumerateArray()
            .Select(c => c.GetProperty("contains").GetProperty("properties").GetProperty("id").GetProperty("const").GetString());
        Assert.Equal(FamilyIds, ids.Order(StringComparer.Ordinal));
        var definition = schema.RootElement.GetProperty("$defs").GetProperty("command");
        Assert.False(definition.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["behaviorChange", "id", "name", "preconditions", "requiredChecks", "requirement", "sourceMeaning"],
            definition.GetProperty("required").EnumerateArray().Select(p => p.GetString()).Order(StringComparer.Ordinal));
    }

    private static JsonDocument Read(string file) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", file)));

    private static void RequireText(JsonElement value) =>
        Assert.False(string.IsNullOrWhiteSpace(value.GetString()));

    private static void RequireTextList(JsonElement value)
    {
        var entries = value.EnumerateArray().ToArray();
        Assert.NotEmpty(entries);
        Assert.All(entries, RequireText);
        Assert.Equal(entries.Length, entries.Select(e => e.GetString()).Distinct().Count());
    }
}
