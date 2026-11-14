using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TaskDisposalContractCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DerivedDisposalAndCompletionSemanticsMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "TaskDisposalFixture", Source, "TaskDisposalFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace TaskDisposalFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var ran = 0;
                var pending = new ObservedTask(() => { ran++; });
                try { pending.Dispose(); return 1; } catch (InvalidOperationException) { }
                if (pending.TrueCalls != 1 || ran != 0) return 2;
                pending.Cleanup(false);
                if (pending.FalseCalls != 1) return 3;
                try { pending.Dispose(); return 4; } catch (InvalidOperationException) { }
                var completed = new ObservedTask(() => { ran++; });
                completed.RunSynchronously();
                if (!completed.IsCompleted || ran != 1) return 5;
                completed.Dispose(); completed.Dispose(); ((IDisposable)completed).Dispose();
                if (completed.TrueCalls != 3 || completed.FalseCalls != 0) return 6;
                var result = Task.FromResult(42); result.Dispose(); result.Dispose();
                if (result.GetAwaiter().GetResult() != 42) return 7;
                var error = new Exception("failure");
                var faulted = Task.FromException(error); faulted.Dispose();
                if (!faulted.IsFaulted || faulted.Exception.InnerException != error) return 8;
                var canceled = Task.FromCanceled(new CancellationToken(true)); canceled.Dispose();
                if (!canceled.IsCanceled) return 9;
                Task.CompletedTask.Dispose(); Task.CompletedTask.GetAwaiter().GetResult();
                return input + 1;
            }
        }
        public sealed class ObservedTask : Task
        {
            public int TrueCalls;
            public int FalseCalls;
            public ObservedTask(Action action) : base(action) { }
            public void Cleanup(bool disposing) { Dispose(disposing); }
            protected override void Dispose(bool disposing)
            {
                if (disposing) TrueCalls++; else FalseCalls++;
                base.Dispose(disposing);
            }
        }
        """;
}
