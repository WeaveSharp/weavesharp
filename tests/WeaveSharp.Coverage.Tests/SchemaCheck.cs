using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WeaveSharp.Coverage.Tests;

// Evaluates the schema vocabulary used by this inventory without fetching references.
internal static class SchemaCheck
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "$schema", "$defs", "$ref", "title", "type", "const", "enum", "properties",
        "additionalProperties", "required", "items", "minItems", "uniqueItems", "minLength", "pattern",
    };

    public static List<string> Validate(JsonNode? value, JsonObject schema)
    {
        List<string> errors = [];
        Visit(value, schema, schema, "$", errors);
        return errors;
    }

    private static void Visit(JsonNode? value, JsonObject rule, JsonObject root, string path, List<string> errors)
    {
        foreach (var keyword in rule.Select(property => property.Key))
        {
            if (!Keywords.Contains(keyword))
                throw new InvalidOperationException($"Schema keyword requires an evaluator: {keyword}");
        }

        if (rule["$ref"] is JsonValue reference)
        {
            string target = reference.GetValue<string>();
            const string prefix = "#/$defs/";
            if (!target.StartsWith(prefix, StringComparison.Ordinal) || root["$defs"]?[target[prefix.Length..]] is not JsonObject definition)
                throw new InvalidOperationException($"Schema reference cannot be resolved locally: {target}");
            Visit(value, definition, root, path, errors);
        }

        if (rule.ContainsKey("const") && !JsonNode.DeepEquals(value, rule["const"]))
            errors.Add($"{path}: expected {rule["const"]}.");
        if (rule["enum"] is JsonArray choices && !choices.Any(choice => JsonNode.DeepEquals(value, choice)))
            errors.Add($"{path}: value is outside the allowed set.");

        string? type = rule["type"]?.GetValue<string>();
        bool typeMatches = type switch
        {
            null => true,
            "object" => value is JsonObject,
            "array" => value is JsonArray,
            "string" => value is JsonValue scalar && scalar.TryGetValue<string>(out _),
            _ => throw new InvalidOperationException($"Schema type requires an evaluator: {type}"),
        };
        if (!typeMatches)
        {
            errors.Add($"{path}: expected {type}.");
            return;
        }

        if (value is JsonObject obj)
        {
            if (rule["required"] is JsonArray required)
                foreach (string key in required.Select(item => item!.GetValue<string>()))
                    if (!obj.ContainsKey(key))
                        errors.Add($"{path}.{key}: required property is missing.");

            var properties = rule["properties"] as JsonObject;
            foreach (var (key, child) in obj)
            {
                if (properties?[key] is JsonObject childRule)
                    Visit(child, childRule, root, $"{path}.{key}", errors);
                else if (rule["additionalProperties"]?.GetValue<bool>() == false)
                    errors.Add($"{path}.{key}: unexpected property.");
            }
        }

        if (value is JsonArray array)
        {
            if (rule["minItems"] is JsonValue minimum && array.Count < minimum.GetValue<int>())
                errors.Add($"{path}: too few entries.");
            for (int i = 0; i < array.Count; i++)
            {
                string itemPath = $"{path}[{i}]";
                if (array[i] is JsonObject item && item["id"] is JsonValue id && id.TryGetValue<string>(out string? label))
                    itemPath += $"({label})";
                if (rule["items"] is JsonObject itemRule)
                    Visit(array[i], itemRule, root, itemPath, errors);
                if (rule["uniqueItems"]?.GetValue<bool>() == true && array.Take(i).Any(previous => JsonNode.DeepEquals(previous, array[i])))
                    errors.Add($"{itemPath}: duplicate entry {array[i]}.");
            }
        }

        if (value is JsonValue text && text.TryGetValue<string>(out string? content))
        {
            if (rule["minLength"] is JsonValue minimum && content.Length < minimum.GetValue<int>())
                errors.Add($"{path}: text is empty.");
            if (rule["pattern"] is JsonValue pattern && !Regex.IsMatch(content, pattern.GetValue<string>(), RegexOptions.CultureInvariant))
                errors.Add($"{path}: text does not match {pattern}.");
        }
    }
}
