using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace WeaveSharp.Coverage.Tests;

internal static class ManifestCheck
{
    // Section 6 family identifiers are independent of the data being checked.
    private static readonly Dictionary<string, string[]> FamilyMaturities = new(StringComparer.Ordinal)
    {
        ["compilation-and-names"] = ["structural"],
        ["type-declarations"] = ["structural"],
        ["members-and-modifiers"] = ["structural"],
        ["types-and-signatures"] = ["dedicated"],
        ["literals-and-expressions"] = ["dedicated"],
        ["calls-and-construction"] = ["dedicated"],
        ["variables-and-patterns"] = ["dedicated"],
        ["structured-statements"] = ["dedicated"],
        ["exceptions-and-lifetime"] = ["dedicated"],
        ["callables-and-closures"] = ["dedicated"],
        ["asynchrony-and-iteration"] = ["dedicated"],
        ["queries-and-dynamic"] = ["dedicated"],
        ["memory-and-interop"] = ["dedicated"],
        ["compile-time-metadata"] = ["structural", "dedicated"],
        ["generated-and-partial"] = ["context-dependent"],
        ["csharp-14-additions"] = ["structural", "dedicated", "context-dependent"],
    };

    private static readonly Dictionary<string, string> Additions = new(StringComparer.Ordinal)
    {
        ["extension-members"] = "structural",
        ["null-conditional-assignment"] = "dedicated",
        ["unbound-generics-in-nameof"] = "dedicated",
        ["implicit-span-conversions"] = "dedicated",
        ["simple-lambda-parameter-modifiers"] = "dedicated",
        ["field-backed-properties"] = "structural",
        ["partial-events-and-constructors"] = "structural",
        ["user-defined-compound-assignment"] = "dedicated",
        ["file-based-app-directives"] = "context-dependent",
    };

    public static List<string> Validate(
        JsonNode? manifest,
        JsonObject schema,
        IEnumerable<string>? syntaxKinds = null,
        IEnumerable<string>? languageVersions = null)
    {
        var errors = SchemaCheck.Validate(manifest, schema);
        if (errors.Count != 0)
            return errors;

        var root = manifest!.AsObject();
        var features = root["features"]!.AsArray().Select(node => node!.AsObject()).ToArray();
        var families = root["families"]!.AsArray().Select(node => node!.AsObject()).ToArray();
        CheckIds(features, "feature", errors);
        CheckIds(families, "family", errors);

        foreach (var (family, _) in FamilyMaturities)
        {
            if (!families.Any(entry => Text(entry, "id") == family))
                errors.Add($"Family {family}: inventory entry is missing.");
            if (!features.Any(feature => Text(feature, "family") == family))
                errors.Add($"Family {family}: no features remain.");
        }

        foreach (var feature in features)
        {
            string id = Text(feature, "id");
            string family = Text(feature, "family");
            if (!FamilyMaturities.TryGetValue(family, out var maturities))
                errors.Add($"Feature {id}: unknown family {family}.");
            else if (!maturities.Contains(Text(feature, "plannedMaturity"), StringComparer.Ordinal))
                errors.Add($"Feature {id}: planned maturity differs from family {family}.");
            if (!families.Any(entry => Text(entry, "id") == family))
                errors.Add($"Feature {id}: family {family} is undeclared.");
        }

        foreach (var (id, maturity) in Additions)
        {
            var feature = features.FirstOrDefault(feature => Text(feature, "id") == id);
            if (feature is null)
                errors.Add($"C# 14 addition {id}: feature is missing.");
            else if (Text(feature, "family") != "csharp-14-additions" || Text(feature, "languageVersion") != "14" || Text(feature, "plannedMaturity") != maturity)
                errors.Add($"C# 14 addition {id}: review its family, version and maturity.");
        }

        var known = (syntaxKinds ?? Enum.GetNames<SyntaxKind>()).ToHashSet(StringComparer.Ordinal);
        Dictionary<string, List<string>> owners = new(StringComparer.Ordinal);
        foreach (var feature in features)
            foreach (string kind in Strings(feature["syntaxKinds"]!))
            {
                if (!owners.TryGetValue(kind, out var names))
                    owners[kind] = names = [];
                names.Add(Text(feature, "id"));
            }

        Dictionary<string, int> classified = new(StringComparer.Ordinal);
        foreach (var entry in root["classifiedSyntaxKinds"]!.AsArray().Select(node => node!.AsObject()))
        {
            string kind = Text(entry, "syntaxKind");
            classified[kind] = classified.GetValueOrDefault(kind) + 1;
            if (Text(entry, "category") == "other" && string.IsNullOrWhiteSpace(entry["note"]?.GetValue<string>()))
                errors.Add($"SyntaxKind {kind}: other requires an explanatory note.");
        }

        var shared = Strings(root["sharedSyntaxKinds"]!).ToHashSet(StringComparer.Ordinal);
        var accounted = owners.Keys.Concat(classified.Keys).ToHashSet(StringComparer.Ordinal);
        foreach (string kind in known.Except(accounted).Order(StringComparer.Ordinal))
            errors.Add($"SyntaxKind {kind}: no feature or classification accounts for it.");
        foreach (string kind in accounted.Concat(shared).Distinct().Except(known).Order(StringComparer.Ordinal))
            errors.Add($"SyntaxKind {kind}: absent from the referenced Roslyn enum.");
        foreach (var (kind, names) in owners)
        {
            if (names.Count > 1 && !shared.Contains(kind))
                errors.Add($"SyntaxKind {kind}: multiple features require a shared declaration ({string.Join(", ", names)}).");
            if (classified.ContainsKey(kind))
                errors.Add($"SyntaxKind {kind}: both a feature and a classification claim it.");
        }
        foreach (var (kind, count) in classified)
            if (count != 1)
                errors.Add($"SyntaxKind {kind}: repeated classification.");
        foreach (string kind in shared)
            if (!owners.TryGetValue(kind, out var names) || names.Count < 2)
                errors.Add($"SyntaxKind {kind}: shared declaration needs at least two feature owners.");

        string newest = (languageVersions ?? Enum.GetNames<LanguageVersion>())
            .Where(name => Regex.IsMatch(name, "^CSharp[0-9]+(?:_[0-9]+)?$", RegexOptions.CultureInvariant))
            .OrderBy(name => Version.Parse(name[6..].Replace('_', '.') + (name.Contains('_') ? "" : ".0")))
            .Last();
        var baseline = root["baseline"]!.AsObject();
        if (newest != Text(baseline, "newestLanguageVersion"))
            errors.Add($"LanguageVersion {newest}: review the manifest baseline {Text(baseline, "newestLanguageVersion")}.");
        string actualRoslyn = typeof(SyntaxKind).Assembly.GetName().Version!.ToString(3);
        if (actualRoslyn != Text(baseline, "roslynVersion"))
            errors.Add($"Roslyn {actualRoslyn}: manifest records {Text(baseline, "roslynVersion")}.");

        return errors;
    }

    private static void CheckIds(IEnumerable<JsonObject> entries, string label, List<string> errors)
    {
        foreach (var group in entries.GroupBy(entry => Text(entry, "id"), StringComparer.Ordinal))
            if (group.Count() > 1)
                errors.Add($"Duplicate {label} id: {group.Key}.");
    }

    private static string Text(JsonObject entry, string key) => entry[key]!.GetValue<string>();

    private static IEnumerable<string> Strings(JsonNode node) => node.AsArray().Select(item => item!.GetValue<string>());
}
