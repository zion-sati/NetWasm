using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Wit.Bindings.Tests.Conformance;

// Reads WIT through real wasm-tools and compiles generated bindings against
// CoreLib.
internal static class BindingCompiler
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() =>
        [MetadataReference.CreateFromFile(Path.Combine(
            RepositoryRoot(), "src", "NetWasm.CoreLib", "bin", "Release", "net10.0", "NetWasm.CoreLib.dll"))]);

    // The bindings' errors, apart from exports the application implements.
    internal static string[] Compile(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Bindings",
            [CSharpSyntaxTree.ParseText(source)],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable));
        return [.. compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id != "CS8795")
            .Select(static diagnostic => diagnostic.ToString())];
    }

    internal static WitDocumentReader Reader() => new(new ProcessWasmTools(
        new SystemExternalCommandRunner(),
        Environment.GetEnvironmentVariable("NETWASM_WASM_TOOLS_PATH") ?? "wasm-tools"));

    internal static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NetWasm.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("The NetWasm repository root was not found.");
    }
}
