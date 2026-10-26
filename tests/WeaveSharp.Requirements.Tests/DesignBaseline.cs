using System;
using System.Collections.Generic;
using System.Linq;

namespace WeaveSharp.Requirements.Tests;

internal sealed record RequirementRule(string Id, string Family, string Section, string[] Cases, string[] Gates);

internal static class DesignBaseline
{
    // Section 17 order and ranges; section 16 refines the case sets below.
    private static readonly (string Family, int Count, string Section, string Cases, string Gates)[] Families =
    [
        ("FND", 8, "1", "01 02 04 20 23 29", "01 03 08"),
        ("SUP", 5, "3", "12 24 26 27 29", "01 02 08"),
        ("SRC", 10, "4", "01 02 03 04 11 12 20 24 31 33", "03 04"),
        ("GRF", 18, "5", "04 05 06 07 08 09 10 11 12 13 14 15 16 22 27 33", "01 04"),
        ("LIB", 7, "7", "05 06 08 15 22 29", "04 07"),
        ("META", 10, "8", "01 03 23 30 33", "03 07"),
        ("UX", 14, "9", "02 03 22 25 30 32 33", "04 05 08"),
        ("DBG", 12, "10", "17 18 20 21 26 32", "05"),
        ("CON", 10, "11", "13 14 15 16 17 18 21 25", "04 05 06"),
        ("TRC", 12, "B", "13 14 15 16 17 18 19 20 21 24 28 32", "06 07"),
        ("HOST", 12, "12", "03 12 24 25 26 27 28 31 33", "02 03 07"),
        ("ARC", 9, "13", "03 12 20 24 26 29 31 33", "02 03 06 07"),
        ("OPS", 8, "14", "19 23 24 26 28 30 33", "06 07"),
        ("QUAL", 8, "15", "", "01 02 03 04 05 06 07 08"),
    ];

    internal static string[] CaseIds { get; } = Numbered("AC", 33);
    internal static string[] GateIds { get; } = Numbered("G", 8);
    internal static IReadOnlyList<RequirementRule> Requirements { get; } = CreateRequirements();

    private static RequirementRule[] CreateRequirements() => Families.SelectMany(family =>
        Enumerable.Range(1, family.Count).Select(number =>
        {
            string id = $"{family.Family}-{number:00}";
            string section = family.Family == "GRF" && number is >= 14 and <= 17 ? "6" : family.Section;
            return new RequirementRule(id, family.Family, section,
                RefinedCases(family.Family, number, family.Cases), Expand("G", family.Gates));
        })).ToArray();

    private static string[] RefinedCases(string family, int number, string fallback) => (family, number) switch
    {
        ("UX", <= 4) => Expand("AC", "02 03 05 06 07 08 22"),
        ("UX", 5 or 6) => Expand("AC", "25 26"),
        ("UX", 7 or 8) => Expand("AC", "01 04 23"),
        ("UX", 9 or 10) => Expand("AC", "11 12 17 18 19 20 21 24 26 27"),
        ("UX", >= 11) => Expand("AC", "25 30 32 33"),
        ("OPS", 1 or 5) => Expand("AC", "23 26 27 28"),
        ("OPS", 2 or 3 or 6 or 8) => Expand("AC", "19 24 28"),
        ("OPS", 4) => Expand("AC", "13 14 15 16 17 18 19 20 21 28"),
        ("OPS", 7) => Expand("AC", "12 20 24 26"),
        ("QUAL", 1 or 5 or 8) => CaseIds,
        ("QUAL", 2 or 3) => Expand("AC", "01 02 03 04 05 06 07 08 09 10 11 12 13 14 15 16 22 27"),
        ("QUAL", 4) => [],
        ("QUAL", 6) => Expand("AC", "12 17 20 21 24 26 27"),
        ("QUAL", 7) => Expand("AC", "19 23 24 25 28"),
        _ => Expand("AC", fallback),
    };

    private static string[] Numbered(string prefix, int count) =>
        Enumerable.Range(1, count).Select(number => $"{prefix}-{number:00}").ToArray();

    private static string[] Expand(string prefix, string numbers) => numbers
        .Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(number => $"{prefix}-{number}").ToArray();
}
