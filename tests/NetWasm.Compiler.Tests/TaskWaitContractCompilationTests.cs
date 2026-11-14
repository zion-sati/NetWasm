using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TaskWaitContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CompletedAndPollingWaitsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "TaskWaitFixture", Source, "TaskWaitFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace TaskWaitFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input);
                if (result != 42) return result;
                var pending = new TaskCompletionSource<int>().Task;
                var caught = 0;
                try { pending.Wait(); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("Task.Wait")) return 2; caught++; }
                try { pending.Wait(1); return 3; } catch (PlatformNotSupportedException) { caught++; }
                try { pending.Wait(TimeSpan.FromMilliseconds(1)); return 4; } catch (PlatformNotSupportedException) { caught++; }
                try { pending.Wait(CancellationToken.None); return 5; } catch (PlatformNotSupportedException) { caught++; }
                return caught == 4 ? input + 1 : 6;
            }
            public static int Portable(int input)
            {
                var canceled = new CancellationToken(true);
                Task.CompletedTask.Wait(); Task.CompletedTask.Wait(canceled);
                if (!Task.CompletedTask.Wait(0, canceled) || !Task.CompletedTask.Wait(TimeSpan.Zero)) return 7;
                var completion = new TaskCompletionSource<int>(); var pending = completion.Task;
                if (pending.Wait(0) || pending.Wait(0, canceled) || pending.Wait(TimeSpan.Zero)) return 8;
                try { pending.Wait(1, canceled); return 9; } catch (OperationCanceledException ex) { if (ex.CancellationToken != canceled) return 10; }
                completion.SetResult(42);
                if (!pending.Wait(0) || pending.Result != 42) return 11;
                var original = new InvalidOperationException("failure");
                var failed = Task.FromException(original);
                try { failed.Wait(0, canceled); return 12; } catch (AggregateException ex) { if (!ReferenceEquals(ex.InnerException, original) || ex.InnerExceptions.Count != 1) return 13; }
                var taskCanceled = Task.FromCanceled(canceled);
                try { taskCanceled.Wait(0); return 14; }
                catch (AggregateException ex) { if (ex.InnerException is not TaskCanceledException inner || !ReferenceEquals(inner.Task, taskCanceled) || inner.CancellationToken != canceled) return 15; }
                try { taskCanceled.Wait(0, canceled); return 16; } catch (OperationCanceledException ex) { if (ex.CancellationToken != canceled) return 17; }
                try { Task.CompletedTask.Wait(-2); return 18; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "millisecondsTimeout") return 19; }
                try { Task.CompletedTask.Wait(TimeSpan.FromMilliseconds(-2)); return 20; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "timeout") return 21; }
                try { Task.CompletedTask.Wait(TimeSpan.FromMilliseconds((long)int.MaxValue + 1)); return 22; } catch (ArgumentOutOfRangeException) { }
                return input + 1;
            }
        }
        """;
}
