using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class WitImportAliasCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CompilerExecutesCoreLibImportsAlongsideGeneratedAliases(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System.Diagnostics;
            using System.Runtime.InteropServices.WebAssembly;
            using System.Threading;
            using System.Threading.Tasks;

            namespace WitImportAliasFixture;

            internal static class GeneratedClockBindings
            {
                [WitImport("wasi:clocks/monotonic-clock@0.3.2", "now")]
                internal static extern long Now();

                [WitImport("wasi:clocks/monotonic-clock@0.3.2", "get-resolution")]
                internal static extern long Resolution();
            }

            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    var before = Stopwatch.GetTimestamp();
                    using var source = new CancellationTokenSource();
                    var pending = Task.Delay(1, source.Token);
                    source.Cancel();
                    var after = Stopwatch.GetTimestamp();

                    return pending.IsCanceled && after >= before
                        ? value
                        : -value;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            optimize ? "WitImportAliasOptimized" : "WitImportAliasDebug",
            source,
            "WitImportAliasFixture.EntryPoint",
            optimize,
            target,
            42,
            [])
        {
            Exports =
            [
                new RequestedExport(
                    "run",
                    "WitImportAliasFixture.EntryPoint",
                    "Run"),
            ],
            WitPath = Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/platform@1.0.0",
        });

        Assert.Equal(42, observed);
    }
}
