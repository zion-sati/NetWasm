using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class TaskFactoryFromAsyncResultCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CompletedResultAdaptersMatchDesktop(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("TaskFactoryApmResultFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "TaskFactoryApmResultFixture.EntryPoint",
            "Run", [new("run", "TaskFactoryApmResultFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        namespace TaskFactoryApmResultFixture;
        public sealed class QueueScheduler : TaskScheduler
        {
            public readonly Queue<Task> Tasks = new Queue<Task>();
            public int Queued;
            public bool Fail;
            public bool Inline;
            protected override void QueueTask(Task task) { if (Fail) throw new InvalidOperationException("queue"); Queued++; Tasks.Enqueue(task); }
            protected override bool TryExecuteTaskInline(Task task, bool queued) => Inline && TryExecuteTask(task);
            protected override IEnumerable<Task> GetScheduledTasks() => Tasks;
            public void Drain() { while (Tasks.Count != 0) TryExecuteTask(Tasks.Dequeue()); }
        }
        public sealed class Result : IAsyncResult
        {
            public object? AsyncState { get; set; }
            public WaitHandle AsyncWaitHandle => throw new Exception("Wait handle must not be read");
            public bool CompletedSynchronously => false;
            public bool IsCompleted { get; set; }
        }
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var portable = Portable(input);
                if (portable != 42) return portable;
                var pending = new Result();
                var caught = 0;
                var factory = new TaskFactory();
                var generic = new TaskFactory<int>();
                try { factory.FromAsync(pending, (Action<IAsyncResult>)(r => { })); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("FromAsync")) return 2; caught++; }
                try { factory.FromAsync(pending, (Action<IAsyncResult>)(r => { }), TaskCreationOptions.None); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("FromAsync")) return 2; caught++; }
                try { factory.FromAsync(pending, (Action<IAsyncResult>)(r => { }), TaskCreationOptions.None, TaskScheduler.Default); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("FromAsync")) return 2; caught++; }
                try { factory.FromAsync(pending, (Func<IAsyncResult, int>)(r => 42)); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("FromAsync")) return 2; caught++; }
                try { factory.FromAsync(pending, (Func<IAsyncResult, int>)(r => 42), TaskCreationOptions.None); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("FromAsync")) return 2; caught++; }
                try { factory.FromAsync(pending, (Func<IAsyncResult, int>)(r => 42), TaskCreationOptions.None, TaskScheduler.Default); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("FromAsync")) return 2; caught++; }
                try { generic.FromAsync(pending, (Func<IAsyncResult, int>)(r => 42)); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("FromAsync")) return 2; caught++; }
                try { generic.FromAsync(pending, (Func<IAsyncResult, int>)(r => 42), TaskCreationOptions.None); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("FromAsync")) return 2; caught++; }
                try { generic.FromAsync(pending, (Func<IAsyncResult, int>)(r => 42), TaskCreationOptions.None, TaskScheduler.Default); return 1; } catch (PlatformNotSupportedException ex) { if (!ex.Message.Contains("FromAsync")) return 2; caught++; }
                return caught == 9 ? input + 1 : 3;
            }
            public static int Portable(int input)
            {
                var scheduler = new QueueScheduler();
                var factory = new TaskFactory(new CancellationToken(true), TaskCreationOptions.None, TaskContinuationOptions.None, scheduler);
                var generic = new TaskFactory<int>(scheduler);
                var completed = new Result { IsCompleted = true, AsyncState = new object() };
                var calls = 0;
                Action<IAsyncResult> endAction = r => { if (ReferenceEquals(r, completed)) calls++; };
                Func<IAsyncResult, int> endFunction = r => ReferenceEquals(r, completed) ? 42 : 0;
                var task0 = factory.FromAsync(completed, endAction);
                var task1 = factory.FromAsync(completed, endAction, TaskCreationOptions.None);
                var task2 = factory.FromAsync(completed, endAction, TaskCreationOptions.None, scheduler);
                var task3 = factory.FromAsync(completed, endFunction);
                var task4 = factory.FromAsync(completed, endFunction, TaskCreationOptions.None);
                var task5 = factory.FromAsync(completed, endFunction, TaskCreationOptions.None, scheduler);
                var task6 = generic.FromAsync(completed, endFunction);
                var task7 = generic.FromAsync(completed, endFunction, TaskCreationOptions.None);
                var task8 = generic.FromAsync(completed, endFunction, TaskCreationOptions.None, scheduler);
                if (calls != 0 || scheduler.Queued != 9) return 4;
                scheduler.Drain();
                if (calls != 3) return 5;
                if (!task0.IsCompletedSuccessfully) return 6;
                if (task0.AsyncState != null) return 6;
                if (!task1.IsCompletedSuccessfully) return 6;
                if (task1.AsyncState != null) return 6;
                if (!task2.IsCompletedSuccessfully) return 6;
                if (task2.AsyncState != null) return 6;
                if (task3.Result != 42) return 6;
                if (task3.AsyncState != null) return 6;
                if (task4.Result != 42) return 6;
                if (task4.AsyncState != null) return 6;
                if (task5.Result != 42) return 6;
                if (task5.AsyncState != null) return 6;
                if (task6.Result != 42) return 6;
                if (task6.AsyncState != null) return 6;
                if (task7.Result != 42) return 6;
                if (task7.AsyncState != null) return 6;
                if (task8.Result != 42) return 6;
                if (task8.AsyncState != null) return 6;
                scheduler.Inline = true;
                var inline = generic.FromAsync(completed, endFunction);
                if (!inline.IsCompletedSuccessfully || inline.Result != 42 || scheduler.Queued != 9) return 7;
                var failure = new InvalidOperationException("end");
                var fault = generic.FromAsync(completed, r => throw failure);
                if (!ReferenceEquals(fault.Exception!.InnerException, failure)) return 8;
                var cancellation = new OperationCanceledException(new CancellationToken(true));
                var canceled = generic.FromAsync(completed, r => throw cancellation);
                try { canceled.GetAwaiter().GetResult(); return 9; } catch (OperationCanceledException ex) { if (!ReferenceEquals(ex, cancellation)) return 10; }
                var failedQueue = generic.FromAsync(completed, endFunction, TaskCreationOptions.None, new QueueScheduler { Fail = true });
                if (!failedQueue.IsFaulted || failedQueue.Exception!.InnerException is not TaskSchedulerException schedulerError || schedulerError.InnerException is not InvalidOperationException) return 11;
                var ambient = new AsyncLocal<int>(); ambient.Value = 42; scheduler.Inline = false;
                var captured = generic.FromAsync(completed, r => ambient.Value);
                ambient.Value = 7; scheduler.Drain();
                if (captured.Result != 42 || ambient.Value != 7) return 12;
                try { generic.FromAsync((IAsyncResult)null!, null!, (TaskCreationOptions)(-1), null!); return 13; } catch (ArgumentNullException ex) { if (ex.ParamName != "asyncResult") return 14; }
                try { generic.FromAsync(completed, null!, (TaskCreationOptions)(-1), null!); return 15; } catch (ArgumentNullException ex) { if (ex.ParamName != "endMethod") return 16; }
                try { generic.FromAsync(completed, endFunction, (TaskCreationOptions)(-1), null!); return 17; } catch (ArgumentNullException ex) { if (ex.ParamName != "scheduler") return 18; }
                foreach (var option in new[] { TaskCreationOptions.LongRunning, TaskCreationOptions.PreferFairness, TaskCreationOptions.DenyChildAttach, TaskCreationOptions.HideScheduler, TaskCreationOptions.RunContinuationsAsynchronously })
                {
                    try { generic.FromAsync(completed, endFunction, option); return 19; } catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "creationOptions") return 20; }
                }
                return input + 1;
            }
        }
        """;
}
