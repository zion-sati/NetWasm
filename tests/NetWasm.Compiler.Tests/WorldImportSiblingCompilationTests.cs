using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Siblings of issue 180, where a program reaching HttpClient failed with NW1009 naming only a
// generated binding: each .NET API that reaches a WASI service the selected world lacks names the
// interface it imports and the line that adds it to the world.
public sealed class WorldImportSiblingCompilationTests
{
    public static TheoryData<string, string, string> Cases => new()
    {
        { "Stdout", "System.Console.WriteLine(42);", "wasi:cli/std" },
        { "Stdin", "_ = System.Console.ReadLine();", "wasi:cli/std" },
        { "Files", "_ = System.IO.File.ReadAllText(\"/data/input.txt\");", "wasi:filesystem/" },
        { "Environment", "_ = System.Environment.GetEnvironmentVariable(\"HOME\");", "wasi:cli/environment" },
        { "Randomness", "_ = System.Security.Cryptography.RandomNumberGenerator.GetInt32(10);", "wasi:random/" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void AnApiItsWorldDoesNotImportNamesTheInterfaceAndTheImport(string name, string statement, string interfacePrefix)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("WorldImport" + name, $$"""
            public static class Program
            {
                public static int Main()
                {
                    {{statement}}
                    return 0;
                }
            }
            """);

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "Program",
            "Main",
            [],
            WasmTarget.Wasm32,
            WitPath: Path.Combine(assets.Root, "wit", "wasi-0.3.2", "clocks"),
            WitWorld: "wasi:clocks/imports@0.3.2",
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable)));

        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
        Assert.Contains($"imports '{interfacePrefix}", exception.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("which world 'wasi:clocks/imports@0.3.2' does not import; add `import " + interfacePrefix,
            exception.Diagnostic.Message, StringComparison.Ordinal);
    }
}
