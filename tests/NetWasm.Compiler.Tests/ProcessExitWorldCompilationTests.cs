using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// wasi:cli/exit ends a process with a non-zero code. A world without it leaves an int Main's
// result to wasi:cli/run's result (WasiAsyncImportBindingCompilationTests runs one), and a
// program that asks to exit there names the binding, the interface its world lacks and the import
// that adds it.
public sealed class ProcessExitWorldCompilationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnvironmentExitNamesTheExitBindingAWorldWithoutExitLacks(bool optimize)
    {
        using var assets = TestAssets.Create();
        const string source = """
            public static class Program
            {
                public static int Main()
                {
                    System.Environment.Exit(3);
                    return 0;
                }
            }
            """;
        var name = $"ProcessExitWorld{(optimize ? "Release" : "Debug")}";
        var assembly = optimize
            ? assets.CompileOptimizedSource(name, source)
            : assets.CompileSource(name, source);

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
        Assert.Contains("System.Runtime.InteropServices.CommandLine.WasiExitImports::", exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("imports 'wasi:cli/exit@0.3.2', which world 'wasi:clocks/imports@0.3.2' does not import; " +
            "add `import wasi:cli/exit@0.3.2;` to the world", exception.Message, StringComparison.Ordinal);
    }
}
