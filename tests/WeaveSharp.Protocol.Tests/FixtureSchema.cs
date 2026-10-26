using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WeaveSharp.Protocol.Tests;

// This deliberately small checker is only for repository fixtures.
// It is not part of the protocol library or an incoming-message boundary.
internal sealed class FixtureSchema
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "$schema", "$id", "$ref", "$defs", "type", "const", "enum", "oneOf",
        "properties", "required", "additionalProperties", "maxProperties", "propertyNames",
        "patternProperties", "items", "minItems", "maxItems", "uniqueItems",
        "minLength", "maxLength", "pattern", "minimum", "maximum"
    };
    private readonly Dictionary<string, JsonElement> documents;

    public FixtureSchema(string directory)
    {
        documents = Directory.GetFiles(directory, "*.schema.json").ToDictionary(file => Path.GetFileName(file),
            file => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(file)), StringComparer.Ordinal);
        foreach (var (file, schema) in documents)
            CheckVocabulary(schema, file);
    }

    public IReadOnlyList<string> Validate(string file, JsonElement value)
    {
        var errors = new List<string>();
        Check(documents[file], value, file, "$", errors);
        return errors;
    }

    public static void CheckVocabulary(JsonElement schema, string path)
    {
        foreach (var keyword in schema.EnumerateObject())
        {
            if (!Keywords.Contains(keyword.Name))
                throw new InvalidOperationException($"Unsupported fixture keyword {path}/{keyword.Name}");
            if (keyword.Name is "$defs" or "properties" or "patternProperties")
                foreach (var child in keyword.Value.EnumerateObject())
                    CheckVocabulary(child.Value, $"{path}/{keyword.Name}/{child.Name}");
            if (keyword.Name is "items" or "propertyNames")
                CheckVocabulary(keyword.Value, $"{path}/{keyword.Name}");
            if (keyword.Name == "oneOf")
                foreach (var child in keyword.Value.EnumerateArray())
                    CheckVocabulary(child, path + "/oneOf");
        }
        if (!schema.TryGetProperty("type", out var type))
            return;
        string? size = type.GetString() switch
        {
            "string" => "maxLength",
            "array" => "maxItems",
            "object" => "maxProperties",
            _ => null
        };
        if (size is not null && !schema.TryGetProperty(size, out _))
            throw new InvalidOperationException($"Missing {size} in {path}");
        if (type.GetString() == "object" && (!schema.TryGetProperty("additionalProperties", out var additional) ||
            additional.ValueKind != JsonValueKind.False))
            throw new InvalidOperationException($"Object shape must be closed in {path}");
    }

    private void Check(JsonElement schema, JsonElement value, string file, string path, List<string> errors)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var parts = reference.GetString()!.Split('#', 2);
            var targetFile = parts[0].Length == 0 ? file : parts[0];
            var target = documents[targetFile];
            if (parts.Length == 2)
                foreach (var segment in parts[1].Split('/', StringSplitOptions.RemoveEmptyEntries))
                    target = target.GetProperty(segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));
            Check(target, value, targetFile, path, errors);
        }
        if (schema.TryGetProperty("oneOf", out var alternatives))
        {
            int matches = 0;
            foreach (var alternative in alternatives.EnumerateArray())
            {
                var candidateErrors = new List<string>();
                Check(alternative, value, file, path, candidateErrors);
                if (candidateErrors.Count == 0)
                    matches++;
            }
            if (matches != 1)
                errors.Add($"{path}: oneOf matched {matches} branches");
        }
        if (schema.TryGetProperty("const", out var constant) && !JsonElement.DeepEquals(constant, value))
            errors.Add($"{path}: const differs");
        if (schema.TryGetProperty("enum", out var values) && !values.EnumerateArray().Any(v => JsonElement.DeepEquals(v, value)))
            errors.Add($"{path}: enum differs");
        if (schema.TryGetProperty("type", out var type) && !Matches(type.GetString()!, value))
        {
            errors.Add($"{path}: expected {type.GetString()}");
            return;
        }
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("maxProperties", out var maximum) && value.EnumerateObject().Count() > maximum.GetInt32())
                errors.Add($"{path}: maxProperties exceeded");
            if (schema.TryGetProperty("required", out var required))
                foreach (var name in required.EnumerateArray())
                    if (!value.TryGetProperty(name.GetString()!, out _))
                        errors.Add($"{path}: missing {name.GetString()}");
            foreach (var property in value.EnumerateObject())
            {
                bool covered = false;
                if (schema.TryGetProperty("properties", out var properties) && properties.TryGetProperty(property.Name, out var child))
                {
                    covered = true;
                    Check(child, property.Value, file, path + "/" + property.Name, errors);
                }
                if (schema.TryGetProperty("patternProperties", out var patterns))
                    foreach (var pattern in patterns.EnumerateObject())
                        if (Regex.IsMatch(property.Name, pattern.Name, RegexOptions.CultureInvariant))
                        {
                            covered = true;
                            Check(pattern.Value, property.Value, file, path + "/" + property.Name, errors);
                        }
                if (schema.TryGetProperty("propertyNames", out var names))
                    Check(names, JsonSerializer.SerializeToElement(property.Name), file, path + "/<name>", errors);
                if (!covered && schema.TryGetProperty("additionalProperties", out var additional) && !additional.GetBoolean())
                    errors.Add($"{path}: unexpected {property.Name}");
            }
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            int count = value.GetArrayLength();
            if (schema.TryGetProperty("minItems", out var minimum) && count < minimum.GetInt32())
                errors.Add($"{path}: minItems unmet");
            if (schema.TryGetProperty("maxItems", out var maximum) && count > maximum.GetInt32())
                errors.Add($"{path}: maxItems exceeded");
            if (schema.TryGetProperty("items", out var item))
                for (int i = 0; i < count; i++)
                    Check(item, value[i], file, $"{path}/{i}", errors);
            if (schema.TryGetProperty("uniqueItems", out var unique) && unique.GetBoolean())
                for (int i = 0; i < count; i++)
                    for (int j = i + 1; j < count; j++)
                        if (JsonElement.DeepEquals(value[i], value[j]))
                            errors.Add($"{path}: duplicate items");
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            var content = value.GetString()!;
            int length = content.EnumerateRunes().Count();
            if (schema.TryGetProperty("minLength", out var minimum) && length < minimum.GetInt32())
                errors.Add($"{path}: minLength unmet");
            if (schema.TryGetProperty("maxLength", out var maximum) && length > maximum.GetInt32())
                errors.Add($"{path}: maxLength exceeded");
            if (schema.TryGetProperty("pattern", out var pattern) && !Regex.IsMatch(content, pattern.GetString()!, RegexOptions.CultureInvariant))
                errors.Add($"{path}: pattern differs");
        }
        if (value.ValueKind == JsonValueKind.Number)
        {
            double number = value.GetDouble();
            if (schema.TryGetProperty("minimum", out var minimum) && number < minimum.GetDouble())
                errors.Add($"{path}: below minimum");
            if (schema.TryGetProperty("maximum", out var maximum) && number > maximum.GetDouble())
                errors.Add($"{path}: above maximum");
        }
    }

    private static bool Matches(string type, JsonElement value) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.GetDouble() % 1 == 0,
        _ => throw new InvalidOperationException($"Unsupported fixture type {type}")
    };
}
