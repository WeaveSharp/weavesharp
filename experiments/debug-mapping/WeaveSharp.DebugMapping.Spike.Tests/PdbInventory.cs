using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using WeaveSharp.DebugMapping.Spike;
using Xunit;

namespace WeaveSharp.DebugMapping.Spike.Tests;

// Independent PDB-table enumeration checks that the mapper neither drops nor invents points.
internal static class PdbInventory
{
    public sealed record Entry(int Token, int Offset, bool Hidden, string Document,
        int StartLine, int StartColumn, int EndLine, int EndColumn);

    public static IReadOnlyList<Entry> Read(string document, string source, BuildMode mode)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var tree = CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8),
            new CSharpParseOptions(LanguageVersion.CSharp14), document);
        var compiler = CSharpCompilation.Create("PointInventory", new[] { tree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: mode == BuildMode.Debug ? OptimizationLevel.Debug : OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        using var pe = new MemoryStream();
        using var symbols = new MemoryStream();
        var result = compiler.Emit(pe, symbols, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        symbols.Position = 0;
        using var provider = MetadataReaderProvider.FromPortablePdbStream(symbols);
        var reader = provider.GetMetadataReader();
        var entries = new List<Entry>();
        foreach (var handle in reader.MethodDebugInformation)
        {
            var method = reader.GetMethodDebugInformation(handle);
            int token = MetadataTokens.GetToken(MetadataTokens.MethodDefinitionHandle(MetadataTokens.GetRowNumber(handle)));
            foreach (var point in method.GetSequencePoints())
            {
                var file = point.Document.IsNil ? method.Document : point.Document;
                entries.Add(new(token, point.Offset, point.IsHidden, file.IsNil ? "" : reader.GetString(reader.GetDocument(file).Name),
                    point.StartLine, point.StartColumn, point.EndLine, point.EndColumn));
            }
        }
        return entries;
    }
}
