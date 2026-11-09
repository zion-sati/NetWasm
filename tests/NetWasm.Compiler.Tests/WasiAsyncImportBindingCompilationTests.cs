using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A WIT async func binds to a Task: the generated method lowers its call with `async`, awaits
// the subtask on the event loop and returns once the host completes it. The binding of
// wait-for is the same async-lowered built-in CoreLib's Task.Delay imports.
public sealed class WasiAsyncImportBindingCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void AnAsyncImportBindingAwaitsItsSubtask(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var witPath = Path.Combine(assets.Root, "wit", "wasi-0.3.2", "clocks");
        using var services = new ServiceCollection()
            .AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var document = services.GetRequiredService<IWitDocumentReader>().Read(witPath);
        var bindings = NetWasm.Wit.Bindings.WitBindingCompositionRoot.Create()
            .Generate(document, document.SelectWorld("wasi:clocks/imports@0.3.2"));
        Assert.Contains("[ComponentBuiltIn(\"wasi:clocks/monotonic-clock@0.3.2\", \"[async-lower]wait-for\")]",
            bindings, StringComparison.Ordinal);
        var source = bindings + """

            public static class Program
            {
                public static async System.Threading.Tasks.Task<int> Main()
                {
                    var before = MonotonicClockImports.Now();
                    await MonotonicClockImports.WaitFor(5_000_000);
                    var after = MonotonicClockImports.Now();
                    await System.Threading.Tasks.Task.Delay(1);
                    return after - before >= 5_000_000 ? 42 : 1;
                }
            }
            """;
        var name = $"WasiAsyncImport{(optimize ? "Release" : "Debug")}{target}";
        var assembly = optimize
            ? assets.CompileOptimizedSource(name, source)
            : assets.CompileSource(name, source);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NetWasm.Wit.Wasi.Clocks._0._3._2.Program",
            "Main",
            [],
            target,
            WitPath: witPath,
            WitWorld: "wasi:clocks/imports@0.3.2",
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable));

        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd,
            callbackEntry: true, entryExportName: "run"));
    }
}
