using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TaskWaitAnyContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CompletedAndPollingWaitsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "TaskWaitAnyFixture", Source, "TaskWaitAnyFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace TaskWaitAnyFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var result = Portable(input);
                if (result != 42) return result;
                var tasks = new Task[] { new TaskCompletionSource<int>().Task };
                var caught = 0;
                try { Task.WaitAny(tasks); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("Task.WaitAny")) return 2; caught++; }
                try { Task.WaitAny(tasks, 1); return 3; } catch (PlatformNotSupportedException) { caught++; }
                try { Task.WaitAny(tasks, TimeSpan.FromMilliseconds(1)); return 4; } catch (PlatformNotSupportedException) { caught++; }
                try { Task.WaitAny(tasks, CancellationToken.None); return 5; } catch (PlatformNotSupportedException) { caught++; }
                return caught == 4 ? input + 1 : 6;
            }
            public static int Portable(int input)
            {
                var pending = new TaskCompletionSource<int>().Task;
                var canceled = new CancellationToken(true);
                if (Task.WaitAny(Array.Empty<Task>()) != -1) return 7;
                if (Task.WaitAny(new Task[] { pending }, 0) != -1) return 8;
                if (Task.WaitAny(new Task[] { pending }, TimeSpan.Zero) != -1) return 9;
                if (Task.WaitAny(new Task[] { pending, Task.CompletedTask, Task.CompletedTask }) != 1) return 10;
                if (Task.WaitAny(new Task[] { Task.FromException(new Exception()), pending }) != 0) return 11;
                if (Task.WaitAny(new Task[] { Task.FromCanceled(canceled) }, CancellationToken.None) != 0) return 12;
                try { Task.WaitAny(new Task[] { Task.CompletedTask, null! }, 0); return 13; } catch (ArgumentException ex) { if (ex.ParamName != "tasks") return 14; }
                try { Task.WaitAny(Array.Empty<Task>(), 0, canceled); return 15; } catch (OperationCanceledException ex) { if (ex.CancellationToken != canceled) return 16; }
                try { Task.WaitAny(new Task[] { pending }, 0, canceled); return 17; } catch (OperationCanceledException) { }
                try { Task.WaitAny(null!, -2, canceled); return 18; } catch (ArgumentNullException ex) { if (ex.ParamName != "tasks") return 19; }
                try { Task.WaitAny(Array.Empty<Task>(), -2, canceled); return 20; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "millisecondsTimeout") return 21; }
                try { Task.WaitAny(Array.Empty<Task>(), TimeSpan.FromMilliseconds(-2)); return 22; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "timeout") return 23; }
                return input + 1;
            }
        }
        """;
}
