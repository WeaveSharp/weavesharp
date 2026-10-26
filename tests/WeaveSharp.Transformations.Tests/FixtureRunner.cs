using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace WeaveSharp.Transformations.Tests;

internal static class FixtureRunner
{
    private static readonly Lazy<MetadataReference[]> References = new(() =>
    {
        string paths = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Runtime assembly paths are unavailable.");
        return paths.Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray<MetadataReference>();
    });

    public static async Task<string[]> Run(string relativePath, string scenario)
    {
        string path = Path.Combine(AppContext.BaseDirectory, relativePath);
        var syntax = CSharpSyntaxTree.ParseText(
            File.ReadAllText(path),
            new CSharpParseOptions(LanguageVersion.CSharp14),
            path: relativePath);
        var compilation = CSharpCompilation.Create(
            "ObservedFixture",
            [syntax],
            References.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable,
                warningLevel: 4));

        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        var failures = emitted.Diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(emitted.Success && failures.Length == 0,
            $"{relativePath} must compile without diagnostics:{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures.Select(d => d.ToString())));

        image.Position = 0;
        var context = new AssemblyLoadContext("Fixture execution", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var entry = assembly.GetType("Scenario", throwOnError: true)!
                .GetMethod("Run", BindingFlags.Public | BindingFlags.Static,
                    [typeof(string)])
                ?? throw new InvalidOperationException($"{relativePath} lacks Scenario.Run(string).");
            object? result = entry.Invoke(null, [scenario]);
            string[] effects = result switch
            {
                string[] synchronous => synchronous,
                Task<string[]> asynchronous => await asynchronous,
                _ => throw new InvalidOperationException("The entry point must return an effect array or its task.")
            };
            return effects.ToArray();
        }
        finally
        {
            context.Unload();
        }
    }

    public static bool SameEffects(IEnumerable<string> before, IEnumerable<string> after) =>
        before.SequenceEqual(after, StringComparer.Ordinal);
}
