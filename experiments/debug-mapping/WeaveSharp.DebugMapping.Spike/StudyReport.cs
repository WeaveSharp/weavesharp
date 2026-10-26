using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace WeaveSharp.DebugMapping.Spike;

public static class StudyReport
{
    // Callers pass a folder below their test binary directory. The study itself writes nothing.
    public static void Write(MappingStudy study, string directory)
    {
        Directory.CreateDirectory(directory);
        string name = Path.GetFileNameWithoutExtension(study.Document);
        File.WriteAllText(Path.Combine(directory, name + ".json"), study.ToJson());
        File.WriteAllText(Path.Combine(directory, name + ".classifications.txt"), Classifications(study));
        var output = new StringBuilder();
        output.AppendLine($"# {name}");
        output.AppendLine();
        foreach (var build in study.Builds)
        {
            output.AppendLine($"## {build.Mode}");
            output.AppendLine();
            output.AppendLine("| Body | Element | Kind / syntax | Quality | Rule | Points |");
            output.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var mapping in build.Elements)
            {
                var body = study.Bodies.Single(b => b.Id == mapping.BodyId);
                var element = body.Graph.Elements.Single(e => e.Id == mapping.ElementId);
                output.AppendLine($"| {body.Id} {body.Callable} | {element.Id} | {element.Kind} / {element.Anchor.SyntaxKind} | {mapping.Quality} | {mapping.Rule} | {string.Join(", ", mapping.Points)} |");
            }
            output.AppendLine();
            output.AppendLine("| Method | IL | Source range | Body | Quality | Rule | Targets |");
            output.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            foreach (var point in build.Points)
            {
                string range = point.Hidden ? "hidden" : $"{point.Document}:{point.StartLine}:{point.StartColumn}–{point.EndLine}:{point.EndColumn}";
                output.AppendLine($"| {point.MethodToken:X8} | {point.Offset:X4} | {range} | {point.BodyId} | {point.Quality} | {point.Rule} | {string.Join(", ", point.Targets)} |");
            }
            output.AppendLine();
            output.AppendLine("| Method | Name | Kickoff | Body | Association |");
            output.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var method in build.Methods)
                output.AppendLine($"| {method.Token:X8} | {Cell(method.DeclaringType + "." + method.Name)} | {Cell(method.KickoffName)} | {method.BodyId} | {method.BodyRule} |");
            output.AppendLine();
        }
        File.WriteAllText(Path.Combine(directory, name + ".md"), output.ToString());
    }

    private static string Cell(string? value) => (value ?? "")
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("|", "&#124;", StringComparison.Ordinal);

    // Stable, readable expectations include every graph element in both compilations.
    public static string Classifications(MappingStudy study)
    {
        var text = new StringBuilder();
        foreach (var body in study.Bodies)
        foreach (var element in body.Graph.Elements)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"{body.Id} {body.Callable} {element.Id} {element.Kind}/{element.Anchor.SyntaxKind} {element.Role} @{element.Anchor.Start}+{element.Anchor.Length}");
            foreach (var build in study.Builds)
            {
                var entry = build.Elements.Single(e => e.BodyId == body.Id && e.ElementId == element.Id);
                text.Append($" | {build.Mode}:{entry.Quality}/{entry.Rule}");
            }
            text.Append('\n');
        }
        return text.ToString();
    }
}
