using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class PlatformTimerReentrancyCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CallbackCanDisableItsOwnTimer(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "TimerReentrancyFixture", Source, "TimerReentrancyFixture.EntryPoint", false, target, 41, [])
        {
            Optimization = FinalWasmOptimization.None,
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/platform@1.0.0",
            ObserveMethodName = "Observe",
        }));
    }

    private const string Source = """
        using System;
        using System.Threading;
        namespace TimerReentrancyFixture;
        public static class EntryPoint
        {
            private static ITimer? _timer;
            private static int _calls;
            public static int Run(int input)
            {
                _calls = 0;
                _timer = TimeProvider.System.CreateTimer(_ =>
                {
                    _calls++;
                    if (_calls == 1)
                        _timer!.Change(Timeout.InfiniteTimeSpan, TimeSpan.FromMilliseconds(1));
                    else
                        _timer!.Dispose();
                }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
                return 0;
            }
            public static int Observe(int input)
            {
                _timer!.Dispose();
                return _calls == 1 ? input + 1 : 1;
            }
        }
        """;
}
