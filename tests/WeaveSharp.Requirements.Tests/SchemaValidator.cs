using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WeaveSharp.Requirements.Tests;

// Implements only the vocabulary used by register.schema.json, without fetching resources.
internal static class SchemaValidator
{
    internal static void CheckVocabulary(JsonElement shape, JsonElement root, List<string> errors)
    {
        string[] supported = ["$schema", "title", "$defs", "$ref", "type", "additionalProperties",
            "required", "properties", "items", "minItems", "uniqueItems", "pattern", "enum"];
        foreach (JsonProperty property in shape.EnumerateObject())
        {
            if (!supported.Contains(property.Name, StringComparer.Ordinal))
            {
                errors.Add($"Schema: unsupported keyword {property.Name}.");
            }
        }

        if (shape.TryGetProperty("$ref", out JsonElement reference))
        {
            string target = reference.GetString()!;
            if (!target.StartsWith("#/$defs/", StringComparison.Ordinal) ||
                !root.GetProperty("$defs").TryGetProperty(target[8..], out _))
            {
                errors.Add($"Schema: unresolved local reference {target}.");
            }
            if (shape.EnumerateObject().Count() != 1)
            {
                errors.Add("Schema: reference siblings are outside this checker's vocabulary.");
            }
        }

        if (shape.TryGetProperty("type", out JsonElement type) &&
            type.GetString() is not ("object" or "array" or "string"))
        {
            errors.Add($"Schema: unsupported type {type}.");
        }
        if (shape.TryGetProperty("additionalProperties", out JsonElement additional) &&
            additional.ValueKind != JsonValueKind.False)
        {
            errors.Add("Schema: additionalProperties must be false in this vocabulary.");
        }
        foreach (string container in new[] { "$defs", "properties" })
        {
            if (shape.TryGetProperty(container, out JsonElement children))
            {
                foreach (JsonProperty child in children.EnumerateObject())
                {
                    CheckVocabulary(child.Value, root, errors);
                }
            }
        }
        if (shape.TryGetProperty("items", out JsonElement items))
        {
            CheckVocabulary(items, root, errors);
        }
    }

    internal static void Check(JsonElement value, JsonElement shape, JsonElement root, string path, List<string> errors)
    {
        if (shape.TryGetProperty("$ref", out JsonElement reference))
        {
            Check(value, root.GetProperty("$defs").GetProperty(reference.GetString()![8..]), root, path, errors);
            return;
        }

        string type = shape.GetProperty("type").GetString()!;
        JsonValueKind kind = type switch
        {
            "object" => JsonValueKind.Object,
            "array" => JsonValueKind.Array,
            _ => JsonValueKind.String,
        };
        if (value.ValueKind != kind)
        {
            errors.Add($"{path}: expected {type}.");
            return;
        }

        if (kind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String)
            {
                path += $"({id.GetString()})";
            }
            foreach (JsonElement field in shape.GetProperty("required").EnumerateArray())
            {
                if (!value.TryGetProperty(field.GetString()!, out _))
                {
                    errors.Add($"{path}: missing field {field.GetString()}.");
                }
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    errors.Add($"{path}: duplicate field {property.Name}.");
                }
                if (shape.GetProperty("properties").TryGetProperty(property.Name, out JsonElement child))
                {
                    Check(property.Value, child, root, $"{path}.{property.Name}", errors);
                }
                else
                {
                    errors.Add($"{path}: unexpected field {property.Name}.");
                }
            }
        }
        else if (kind == JsonValueKind.Array)
        {
            if (shape.TryGetProperty("minItems", out JsonElement minimum) && value.GetArrayLength() < minimum.GetInt32())
            {
                errors.Add($"{path}: needs at least {minimum.GetInt32()} item(s).");
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (shape.TryGetProperty("uniqueItems", out JsonElement unique) && unique.GetBoolean() &&
                    !seen.Add(item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText()))
                {
                    errors.Add($"{path}: duplicate item {item}.");
                }
                Check(item, shape.GetProperty("items"), root, $"{path}[{index++}]", errors);
            }
        }
        else
        {
            string text = value.GetString()!;
            if (shape.TryGetProperty("pattern", out JsonElement pattern) &&
                !Regex.IsMatch(text, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                errors.Add($"{path}: invalid text or identifier format.");
            }
            if (shape.TryGetProperty("enum", out JsonElement choices) &&
                !choices.EnumerateArray().Any(choice => choice.GetString() == text))
            {
                errors.Add($"{path}: unknown value {text}.");
            }
        }
    }
}
