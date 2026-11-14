using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TimerContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TimersPreserveCallbacksValidationAndDisposal(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "TimerFixture", Source, "TimerFixture.EntryPoint", false, target, 41, [])
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
        namespace TimerFixture;
        public static class EntryPoint
        {
            private static Task<int>? _operation;
            public static int Run(int input) { _operation = Exercise(input); return 0; }
            public static int Observe(int input) => _operation is { IsCompletedSuccessfully: true } ? _operation.Result : 99;
            public static async Task<int> Exercise(int input)
            {
                var portable = await Portable(input);
                if (portable != 42) return portable;
                // As upstream: the timer is disposed before a handle without a native handle fails to signal.
                using var timer = new Timer(_ => { });
                using var handle = new DummyHandle();
                try { timer.Dispose(handle); return 30; } catch (InvalidOperationException) { }
                if (timer.Change(-1, -1)) return 32;
                using var notified = new Timer(_ => { });
                using var notify = new ManualResetEvent(false);
                if (!notified.Dispose(notify) || !notify.WaitOne(0) || notified.Dispose(new ManualResetEvent(false))) return 31;
                return input + 1;
            }
            public static async Task<int> Portable(int input)
            {
                using var intTimer = new Timer(_ => { }, null, -1, 0);
                using var longTimer = new Timer(_ => { }, null, -1L, 0L);
                using var uintTimer = new Timer(_ => { }, null, uint.MaxValue, 0U);
                using var spanTimer = new Timer(_ => { }, null, TimeSpan.FromTicks(-19999), TimeSpan.Zero);
                if (!intTimer.Change(-1, -1) || !longTimer.Change(-1L, 0L) || !uintTimer.Change(uint.MaxValue, 0U) || !spanTimer.Change(TimeSpan.FromTicks(-19999), TimeSpan.Zero)) return 1;
                try { new Timer(null!, null, -2, -2); return 2; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "dueTime") return 3; }
                try { new Timer(null!, null, 5000000000L, -2L); return 4; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "period") return 5; }
                try { new Timer(null!, null, TimeSpan.FromMilliseconds(5000000000), TimeSpan.FromMilliseconds(-2)); return 6; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "dueTime") return 7; }
                try { new Timer(null!); return 8; } catch (ArgumentNullException e) { if (e.ParamName != "callback") return 9; }
                try { intTimer.Dispose((WaitHandle)null!); return 10; } catch (ArgumentNullException e) { if (e.ParamName != "notifyObject") return 11; }
                intTimer.Dispose();
                if (intTimer.Change(0, 0) || intTimer.Change(0L, 0L) || intTimer.Change(0U, 0U) || intTimer.Change(TimeSpan.Zero, TimeSpan.Zero)) return 12;
                try { intTimer.Change(-2, 0); return 13; } catch (ArgumentOutOfRangeException) { }
                try { longTimer.Change(5000000000L, 0L); return 14; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "dueTime") return 15; }
                try { spanTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(5000000000)); return 16; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "period") return 17; }
                await intTimer.DisposeAsync();
                var local = new AsyncLocal<int>(); local.Value = 7;
                var completed = new TaskCompletionSource<int>();
                using var callbackOnly = new Timer(state => completed.SetResult(state is Timer && local.Value == 7 ? 42 : 18));
                local.Value = 9;
                callbackOnly.Change(0, 0);
                if (await completed.Task != 42 || local.Value != 9) return 19;
                var periodicDone = new TaskCompletionSource<int>();
                var count = 0;
                Timer? periodic = null;
                var marker = new object();
                periodic = new Timer(state =>
                {
                    if (!ReferenceEquals(state, marker)) { periodic!.Dispose(); periodicDone.SetResult(20); return; }
                    count++;
                    if (count == 1) periodic!.Change(1L, 1L);
                    else { periodic!.Dispose(); periodicDone.SetResult(count); }
                }, marker, -1L, 1L);
                periodic.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
                if (await periodicDone.Task != 2) return 21;
                await periodic.DisposeAsync();
                var suppressedDone = new TaskCompletionSource<int>();
                Timer suppressed;
                using (ExecutionContext.SuppressFlow())
                    suppressed = new Timer(_ => suppressedDone.SetResult(42));
                suppressed.Change(0U, 0U);
                if (await suppressedDone.Task != 42) return 22;
                await suppressed.DisposeAsync();
                var callbackFinished = new TaskCompletionSource<bool>();
                var disposal = default(ValueTask);
                var earlyCompletion = true;
                Timer? selfDisposing = null;
                selfDisposing = new Timer(_ =>
                {
                    disposal = selfDisposing!.DisposeAsync();
                    earlyCompletion = disposal.IsCompleted;
                    callbackFinished.SetResult(true);
                });
                selfDisposing.Change(0, 1);
                await callbackFinished.Task;
                await disposal;
                if (earlyCompletion) return 23;
                return input + 1;
            }
        }
        public sealed class DummyHandle : WaitHandle { }
        """;
}
