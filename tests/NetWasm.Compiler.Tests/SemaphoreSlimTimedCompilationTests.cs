using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class SemaphoreSlimTimedCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TimedWaitsReleaseAndCancelWithoutLosingPermits(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "SemaphoreTimedFixture", Source, "SemaphoreTimedFixture.EntryPoint", false, target, 41, [])
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
        using System.Threading.Tasks;
        namespace SemaphoreTimedFixture;
        public static class EntryPoint
        {
            private static Task<int>? _operation;
            public static int Run(int input) { _operation = ExerciseWithSetupFailure(input); return 0; }
            public static int Observe(int input) => _operation is { IsCompletedSuccessfully: true } ? _operation.Result : 99;
            private static async Task<int> ExerciseWithSetupFailure(int input)
            {
                var result = await Exercise(input);
                if (result != 42) return result;
                using var semaphore = new SemaphoreSlim(0, 1);
                try { await semaphore.WaitAsync(TimeSpan.FromMilliseconds(5000000000)); return 20; }
                catch (ArgumentOutOfRangeException) { }
                semaphore.Release();
                return semaphore.CurrentCount == 1 ? input + 1 : 21;
            }
            public static async Task<int> Exercise(int input)
            {
                using var semaphore = new SemaphoreSlim(0, 2);
                if (await semaphore.WaitAsync(0) || await semaphore.WaitAsync(TimeSpan.Zero) || await semaphore.WaitAsync(TimeSpan.Zero, CancellationToken.None)) return 1;
                if (await semaphore.WaitAsync(2) || semaphore.CurrentCount != 0) return 2;
                semaphore.Release(); if (!await semaphore.WaitAsync(0)) return 3;
                if (await semaphore.WaitAsync(TimeSpan.FromMilliseconds(2), CancellationToken.None)) return 4;
                semaphore.Release(); if (!await semaphore.WaitAsync(TimeSpan.Zero)) return 5;
                using (var cancellation = new CancellationTokenSource())
                {
                    var waiting = semaphore.WaitAsync(-1, cancellation.Token);
                    cancellation.Cancel();
                    try { await waiting; return 6; } catch (OperationCanceledException ex) { if (ex.CancellationToken != cancellation.Token) return 7; }
                    semaphore.Release(); if (semaphore.CurrentCount != 1 || !await semaphore.WaitAsync(0)) return 8;
                }
                using (var cancellation = new CancellationTokenSource())
                {
                    var waiting = semaphore.WaitAsync(TimeSpan.FromSeconds(1), cancellation.Token);
                    semaphore.Release(); cancellation.Cancel();
                    if (!await waiting || semaphore.CurrentCount != 0) return 9;
                }
                var first = semaphore.WaitAsync(-1); var second = semaphore.WaitAsync(Timeout.InfiniteTimeSpan);
                semaphore.Release(); if (!await first || second.IsCompleted) return 10;
                semaphore.Release(); if (!await second || semaphore.CurrentCount != 0) return 11;
                semaphore.Release(); var canceled = new CancellationToken(true);
                var canceledWait = semaphore.WaitAsync(0, canceled);
                if (!canceledWait.IsCanceled || semaphore.CurrentCount != 1) return 12;
                if (!await semaphore.WaitAsync(TimeSpan.Zero, CancellationToken.None)) return 13;
                try { semaphore.WaitAsync(-2); return 14; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "millisecondsTimeout") return 15; }
                try { semaphore.WaitAsync(TimeSpan.FromMilliseconds(-2)); return 16; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "timeout") return 17; }
                var disposed = new SemaphoreSlim(1); disposed.Dispose();
                try { disposed.WaitAsync(0); return 18; } catch (ObjectDisposedException) { }
                try { disposed.WaitAsync(-2); return 19; } catch (ArgumentOutOfRangeException) { }
                return input + 1;
            }
        }
        """;
}
