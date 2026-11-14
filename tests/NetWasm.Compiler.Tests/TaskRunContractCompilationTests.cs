using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Task.Run runs its delegate as a task on the one thread (Tasks.cs); the fixture runs unchanged on desktop .NET 11.
public sealed class TaskRunContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void OverloadsValidateAndHonourCancellation(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "TaskRunFixture", Source, "TaskRunFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    internal const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace TaskRunFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var invoked = 0;
                Action action = () => { invoked++; };
                Func<Task> asyncAction = () => { invoked++; return Task.CompletedTask; };
                Func<int> function = () => { invoked++; return 42; };
                Func<Task<int>> asyncFunction = () => { invoked++; return Task.FromResult(42); };

                // A token canceled before the call never invokes the delegate. The task is queued and becomes canceled
                // when the scheduler dispatches it, as upstream's QueuedByRuntime tasks do, so its status is not
                // checked here (on desktop the pool may already have dispatched it).
                var canceled = new CancellationToken(true);
                Task[] skipped = [Task.Run(action, canceled), Task.Run(asyncAction, canceled), Task.Run(function, canceled), Task.Run(asyncFunction, canceled)];
                foreach (var task in skipped)
                {
                    if (task is null || task.IsFaulted || task.Status == TaskStatus.RanToCompletion) return 1;
                }
                if (invoked != 0) return 2;

                // Null delegates are rejected before anything is queued.
                if (Rejects(() => Task.Run((Action)null!)) != "action" || Rejects(() => Task.Run((Func<Task>)null!)) != "function" ||
                    Rejects(() => Task.Run((Func<int>)null!)) != "function" || Rejects(() => Task.Run((Func<Task<int>>)null!)) != "function") return 3;

                // Otherwise the delegate runs as a queued task (TaskSemanticsMatchDesktopInWasmtime checks it running).
                Task[] started = [Task.Run(action), Task.Run(asyncAction, CancellationToken.None), Task.Run(function), Task.Run(asyncFunction, CancellationToken.None)];
                foreach (var task in started)
                {
                    if (task is null || task.IsFaulted || task.IsCanceled) return 4;
                }
                var completed = Task.FromResult(42);
                if (!completed.IsCompleted || completed.GetAwaiter().GetResult() != 42) return 5;
                return input + 1;
            }

            static string Rejects(Action call)
            {
                try { call(); return "none"; }
                catch (ArgumentNullException ex) { return ex.ParamName ?? "?"; }
            }
        }
        """;
}
