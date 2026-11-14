using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TaskWaitAllContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CompletedAndPollingWaitsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "TaskWaitAllFixture", Source, "TaskWaitAllFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace TaskWaitAllFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input);
                if (result != 42) return result;
                var tasks = new Task[] { new TaskCompletionSource<int>().Task };
                var caught = 0;
                try { Task.WaitAll(tasks); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("Task.WaitAll")) return 2; caught++; }
                try { Task.WaitAll(tasks, 1); return 3; } catch (PlatformNotSupportedException) { caught++; }
                try { Task.WaitAll(tasks, TimeSpan.FromMilliseconds(1)); return 4; } catch (PlatformNotSupportedException) { caught++; }
                try { Task.WaitAll(tasks, CancellationToken.None); return 5; } catch (PlatformNotSupportedException) { caught++; }
                return caught == 4 ? input + 1 : 6;
            }
            public static int Portable(int input)
            {
                var pending = new TaskCompletionSource<int>().Task;
                var canceled = new CancellationToken(true);
                Task.WaitAll(Array.Empty<Task>());
                Task.WaitAll(new Task[] { Task.CompletedTask }, CancellationToken.None);
                if (!Task.WaitAll(new Task[] { Task.CompletedTask }, TimeSpan.Zero)) return 7;
                if (Task.WaitAll(new Task[] { pending }, 0)) return 8;
                var first = new Exception("first");
                var nested = new AggregateException(new Exception("nested"));
                var fault = Task.FromException(first);
                var canceledTask = Task.FromCanceled(canceled);
                if (Task.WaitAll(new Task[] { fault, pending }, 0)) return 9;
                try { Task.WaitAll(new Task[] { fault, Task.FromException(nested), canceledTask, fault }); return 10; }
                catch (AggregateException ex)
                {
                    if (ex.InnerExceptions.Count != 4 || !ReferenceEquals(ex.InnerExceptions[0], first) || !ReferenceEquals(ex.InnerExceptions[1], nested) || !ReferenceEquals(ex.InnerExceptions[3], first)) return 11;
                    if (ex.InnerExceptions[2] is not TaskCanceledException inner || !ReferenceEquals(inner.Task, canceledTask) || inner.CancellationToken != canceled) return 12;
                }
                try { Task.WaitAll(new Task[] { pending, null! }, 0); return 13; } catch (ArgumentException ex) { if (ex.ParamName != "tasks") return 14; }
                try { Task.WaitAll(Array.Empty<Task>(), 0, canceled); return 15; } catch (OperationCanceledException ex) { if (ex.CancellationToken != canceled) return 16; }
                try { Task.WaitAll(new Task[] { fault }, 0, canceled); return 17; } catch (OperationCanceledException) { }
                try { Task.WaitAll(null!, -2, canceled); return 18; } catch (ArgumentNullException ex) { if (ex.ParamName != "tasks") return 19; }
                try { Task.WaitAll(Array.Empty<Task>(), -2, canceled); return 20; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "millisecondsTimeout") return 21; }
                try { Task.WaitAll(Array.Empty<Task>(), TimeSpan.FromMilliseconds(-2)); return 22; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "timeout") return 23; }
                return input + 1;
            }
        }
        """;
}
