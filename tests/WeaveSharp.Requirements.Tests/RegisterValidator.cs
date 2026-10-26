using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace WeaveSharp.Requirements.Tests;

internal static class RegisterValidator
{
    internal static IReadOnlyList<string> Validate(string registerJson, string schemaJson)
    {
        var errors = new List<string>();
        try
        {
            using JsonDocument register = JsonDocument.Parse(registerJson);
            using JsonDocument schema = JsonDocument.Parse(schemaJson);
            SchemaValidator.CheckVocabulary(schema.RootElement, schema.RootElement, errors);
            if (errors.Count == 0)
            {
                SchemaValidator.Check(register.RootElement, schema.RootElement, schema.RootElement, "$", errors);
            }
            if (errors.Count == 0)
            {
                CheckLinks(register.RootElement, errors);
            }
        }
        catch (JsonException exception)
        {
            errors.Add($"Invalid JSON at {exception.Path}: {exception.Message}");
        }
        return errors;
    }

    private static void CheckLinks(JsonElement register, List<string> errors)
    {
        JsonElement[] requirements = register.GetProperty("requirements").EnumerateArray().ToArray();
        string[] cases = Ids(register.GetProperty("acceptanceCases"));
        string[] gates = Ids(register.GetProperty("gates"));
        CheckInventory(cases, DesignBaseline.CaseIds, "acceptanceCases", errors);
        CheckInventory(gates, DesignBaseline.GateIds, "gates", errors);
        CheckInventory(requirements.Select(item => Text(item, "id")).ToArray(),
            DesignBaseline.Requirements.Select(item => item.Id).ToArray(), "requirements", errors);

        var rules = DesignBaseline.Requirements.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var referencedCases = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement requirement in requirements)
        {
            string id = Text(requirement, "id");
            string[] caseLinks = Strings(requirement.GetProperty("acceptanceCases"));
            string[] gateLinks = Strings(requirement.GetProperty("gates"));
            string assessment = Text(requirement, "assessment");
            if (gateLinks.Length == 0)
            {
                errors.Add($"{id}: no gate link.");
            }
            if (caseLinks.Length == 0 && assessment == "executable")
            {
                errors.Add($"{id}: needs an acceptance case or a non-executable assessment.");
            }
            foreach (string link in caseLinks)
            {
                referencedCases.Add(link);
                if (!cases.Contains(link, StringComparer.Ordinal))
                {
                    errors.Add($"{id}: unknown acceptance case {link}.");
                }
            }
            foreach (string link in gateLinks)
            {
                if (!gates.Contains(link, StringComparer.Ordinal))
                {
                    errors.Add($"{id}: unknown gate {link}.");
                }
            }
            if (rules.TryGetValue(id, out RequirementRule? rule))
            {
                if (Text(requirement, "family") != rule.Family)
                {
                    errors.Add($"{id}: family must be {rule.Family}.");
                }
                if (Text(requirement, "section") != rule.Section)
                {
                    errors.Add($"{id}: defining section must be {rule.Section}.");
                }
                if (!caseLinks.SequenceEqual(rule.Cases))
                {
                    errors.Add($"{id}: acceptance links differ from the section 16/17 mapping.");
                }
                if (!gateLinks.SequenceEqual(rule.Gates))
                {
                    errors.Add($"{id}: gate links differ from the section 17 mapping.");
                }
                if (id == "QUAL-04" && assessment != "measurement")
                {
                    errors.Add($"{id}: workload evidence requires measurement assessment.");
                }
            }
        }
        foreach (string id in cases.Except(referencedCases, StringComparer.Ordinal))
        {
            errors.Add($"{id}: no requirement references this acceptance case.");
        }
    }

    private static void CheckInventory(string[] actual, string[] expected, string collection, List<string> errors)
    {
        foreach (var group in actual.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            errors.Add($"{group.Key}: repeated identifier in {collection}.");
        }
        foreach (string id in expected.Except(actual, StringComparer.Ordinal))
        {
            errors.Add($"{id}: missing from {collection}.");
        }
        foreach (string id in actual.Except(expected, StringComparer.Ordinal))
        {
            errors.Add($"{id}: outside the design inventory for {collection}.");
        }
        if (!actual.SequenceEqual(expected))
        {
            errors.Add($"{collection}: expected the complete inventory in baseline order.");
        }
    }

    private static string Text(JsonElement item, string field) => item.GetProperty(field).GetString()!;
    private static string[] Ids(JsonElement items) => items.EnumerateArray().Select(item => Text(item, "id")).ToArray();
    private static string[] Strings(JsonElement items) => items.EnumerateArray().Select(item => item.GetString()!).ToArray();
}
