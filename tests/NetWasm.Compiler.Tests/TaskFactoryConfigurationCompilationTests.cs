using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TaskFactoryConfigurationCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void FactoryConfigurationMatchesDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = (ICompilationScenarioExecutor)new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "TaskFactoryFixture", Source, "TaskFactoryFixture.EntryPoint", false, target, 41, [])
        { Optimization = FinalWasmOptimization.None }));
    }

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace TaskFactoryFixture;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var canceled = new CancellationToken(true);
                if (!ReferenceEquals(Task.Factory, Task.Factory) || !ReferenceEquals(Task<int>.Factory, Task<int>.Factory)) return 1;
                if (ReferenceEquals(Task<int>.Factory, Task<string>.Factory)) return 2;
                if (Task.Factory.Scheduler != null || Task<int>.Factory.Scheduler != null) return 3;
                if (new TaskFactory().CreationOptions != TaskCreationOptions.None || new TaskFactory<int>().ContinuationOptions != TaskContinuationOptions.None) return 4;
                if (new TaskFactory(canceled).CancellationToken != canceled || new TaskFactory<int>(canceled).CancellationToken != canceled) return 5;
                var scheduler = TaskScheduler.Default;
                if (new TaskFactory(scheduler).Scheduler != scheduler || new TaskFactory<int>(scheduler).Scheduler != scheduler) return 6;
                var factory = new TaskFactory(canceled, TaskCreationOptions.DenyChildAttach, TaskContinuationOptions.ExecuteSynchronously, scheduler);
                var generic = new TaskFactory<int>(canceled, TaskCreationOptions.DenyChildAttach, TaskContinuationOptions.ExecuteSynchronously, scheduler);
                if (factory.CancellationToken != canceled || factory.CreationOptions != TaskCreationOptions.DenyChildAttach || factory.ContinuationOptions != TaskContinuationOptions.ExecuteSynchronously || factory.Scheduler != scheduler) return 7;
                if (generic.CancellationToken != canceled || generic.CreationOptions != TaskCreationOptions.DenyChildAttach || generic.ContinuationOptions != TaskContinuationOptions.ExecuteSynchronously || generic.Scheduler != scheduler) return 8;
                var invalid = new TaskContinuationOptions[] { TaskContinuationOptions.NotOnCanceled, TaskContinuationOptions.NotOnFaulted, TaskContinuationOptions.NotOnRanToCompletion, TaskContinuationOptions.LongRunning | TaskContinuationOptions.ExecuteSynchronously, TaskContinuationOptions.RunContinuationsAsynchronously, (TaskContinuationOptions)(-1) };
                foreach (var option in invalid)
                {
                    try { new TaskFactory((TaskCreationOptions)(-1), option); return 9; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "continuationOptions") return 10; }
                    try { new TaskFactory<int>((TaskCreationOptions)(-1), option); return 11; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "continuationOptions") return 12; }
                }
                try { new TaskFactory((TaskCreationOptions)(-1), TaskContinuationOptions.None); return 13; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "creationOptions") return 14; }
                try { new TaskFactory<int>((TaskCreationOptions)(-1), TaskContinuationOptions.None); return 15; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "creationOptions") return 16; }
                var validCreation = TaskCreationOptions.PreferFairness | TaskCreationOptions.LongRunning | TaskCreationOptions.AttachedToParent | TaskCreationOptions.DenyChildAttach | TaskCreationOptions.HideScheduler | TaskCreationOptions.RunContinuationsAsynchronously;
                var validContinuation = TaskContinuationOptions.PreferFairness | TaskContinuationOptions.LongRunning | TaskContinuationOptions.AttachedToParent | TaskContinuationOptions.DenyChildAttach | TaskContinuationOptions.HideScheduler | TaskContinuationOptions.LazyCancellation;
                if (new TaskFactory(validCreation, validContinuation).CreationOptions != validCreation || new TaskFactory<int>(validCreation, validContinuation).ContinuationOptions != validContinuation) return 17;
                return input + 1;
            }
        }
        """;
}
