// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NetWasm.Compiler.Tests;

// The desktop answer for a fixture whose EntryPoint.Results() returns one int per case: the source compiled against the
// test host's own framework and run in a collectible load context.
internal static class InProcessDesktopResults
{
    public static int[] Of(string source, string assemblyName, string entryType = "EntryPoint")
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;
        var context = new AssemblyLoadContext(assemblyName, isCollectible: true);
        try
        {
            return (int[])context.LoadFromStream(stream).GetType(entryType)!.GetMethod("Results")!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }
}
