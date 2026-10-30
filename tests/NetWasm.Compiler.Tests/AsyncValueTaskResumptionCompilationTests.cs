using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AsyncValueTaskResumptionCompilationTests
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
    public void CompilerResumesGenericValueTaskCallbacks(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "AsyncValueTaskResumptionFixture",
            Source,
            "AsyncValueTaskResumptionFixture.EntryPoint",
            optimize,
            target,
            41,
            [])
        {
            DrainReactor = true,
            ObserveExportName = "observe",
            DiagnosticTracePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"netwasm-valuetask-resumption-{target}-{optimize}.jsonl"),
            DiagnosticLogPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"netwasm-valuetask-resumption-{target}-{optimize}.log"),
            WitPath = Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/async-platform@1.0.0",
            Exports =
            [
                new RequestedExport(
                    "run",
                    "AsyncValueTaskResumptionFixture.EntryPoint",
                    "Run"),
                new RequestedExport(
                    "observe",
                    "AsyncValueTaskResumptionFixture.EntryPoint",
                    "Observe"),
            ],
        });

        Assert.Equal(42, result);
    }

    public static TheoryData<bool, WasmTarget, int> FilteredConfigurations => new()
    {
        { false, WasmTarget.Wasm32, 0 },
        { false, WasmTarget.Wasm32, 1 },
        { false, WasmTarget.Wasm32, 2 },
        { false, WasmTarget.Wasm32, 3 },
        { false, WasmTarget.Wasm32, 4 },
        { false, WasmTarget.Wasm64, 0 },
        { false, WasmTarget.Wasm64, 1 },
        { false, WasmTarget.Wasm64, 2 },
        { false, WasmTarget.Wasm64, 3 },
        { false, WasmTarget.Wasm64, 4 },
        { true, WasmTarget.Wasm32, 0 },
        { true, WasmTarget.Wasm32, 1 },
        { true, WasmTarget.Wasm32, 2 },
        { true, WasmTarget.Wasm32, 3 },
        { true, WasmTarget.Wasm32, 4 },
        { true, WasmTarget.Wasm64, 0 },
        { true, WasmTarget.Wasm64, 1 },
        { true, WasmTarget.Wasm64, 2 },
        { true, WasmTarget.Wasm64, 3 },
        { true, WasmTarget.Wasm64, 4 },
    };

    [Theory]
    [MemberData(nameof(FilteredConfigurations))]
    [Trait("Issue", "40")]
    public void FilteredTryPreservesValueTaskResults(bool optimize, WasmTarget target, int mode)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "FilteredValueTaskFixture", FilteredSource, "FilteredValueTaskFixture.EntryPoint",
            optimize, target, mode, [])
        {
            DrainReactor = true,
            ObserveExportName = "observe",
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/async-platform@1.0.0",
            Exports =
            [
                new RequestedExport("run", "FilteredValueTaskFixture.EntryPoint", "Run"),
                new RequestedExport("observe", "FilteredValueTaskFixture.EntryPoint", "Observe"),
            ],
        });
        Assert.Equal(42, result);
    }

    private const string FilteredSource = """
        using System;
        using System.Threading.Tasks;
        namespace FilteredValueTaskFixture;
        public static class EntryPoint
        {
            private static Task<int> _pending;
            public static int Run(int value) { _pending = Check(value); return 0; }
            public static int Observe() => _pending.IsCompleted ? _pending.GetAwaiter().GetResult() : -1;
            private static bool Accept(Exception exception) => exception is ArgumentException;
            private static async Task<int> Check(int value)
            {
                return value switch
                {
                    0 => await Integer(42, true),
                    1 => await Reference("ok", true) == "ok" ? 42 : -3,
                    2 => await Integer(42, false),
                    3 => await Reference("ok", false) == "ok" ? 42 : -5,
                    4 => await TaskControl(42),
                    _ => -6,
                };
            }
            private static async ValueTask<int> Integer(int value, bool suspend)
            {
                try { if (suspend) await Task.Yield(); return value; }
                catch (Exception e) when (value != 0 && Accept(e)) { return -7; }
            }
            private static async ValueTask<string> Reference(string value, bool suspend)
            {
                try { if (suspend) await Task.Yield(); return value; }
                catch (Exception e) when (value != null && Accept(e)) { return "failure"; }
            }
            private static async Task<int> TaskControl(int value)
            {
                try { await Task.Yield(); return value; }
                catch (Exception e) when (value != 0 && Accept(e)) { return -8; }
            }
        }
        """;

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;

        namespace AsyncValueTaskResumptionFixture;

        public static class EntryPoint
        {
            private static Task<int>? _pending;

            public static int Run(int input)
            {
                _pending = InvokeAsync(input, TransformAsync);
                return 0;
            }

            public static int Observe()
            {
                if (_pending is null || !_pending.IsCompleted)
                {
                    return -1;
                }

                try
                {
                    return _pending.GetAwaiter().GetResult();
                }
                catch
                {
                    return -2;
                }
            }

            private static async Task<int> InvokeAsync<T>(
                T value,
                Func<T, CancellationToken, ValueTask<int>> callback)
            {
                return await callback(value, default);
            }

            private static async ValueTask<int> TransformAsync(
                int value,
                CancellationToken cancellationToken)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                return value + 1;
            }
        }
        """;
}
