// From the compiler harness's SchedulerPairContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace SchedulerPairFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        if (Portable(input) != 42) return 20;
        // As upstream, each pair reads its underlying scheduler's concurrency once (the default pair reads
        // TaskScheduler.Default's); TaskSemanticsMatchDesktopInWasmtime runs tasks on a pair.
        var scheduler = new Scheduler();
        var pair = new ConcurrentExclusiveSchedulerPair();
        _ = new ConcurrentExclusiveSchedulerPair(scheduler);
        _ = new ConcurrentExclusiveSchedulerPair(scheduler, 1);
        _ = new ConcurrentExclusiveSchedulerPair(scheduler, -1, -1);
        if (pair.ExclusiveScheduler.MaximumConcurrencyLevel != 1 || scheduler.Calls != 3) return 22;
        return input + 1;
    }
    public static int Portable(int input)
    {
        var scheduler = new Scheduler();
        try { new ConcurrentExclusiveSchedulerPair(null!); return 1; } catch (ArgumentNullException e) { if (e.ParamName != "taskScheduler") return 2; }
        try { new ConcurrentExclusiveSchedulerPair(null!, 0, 0); return 3; } catch (ArgumentNullException) { }
        try { new ConcurrentExclusiveSchedulerPair(scheduler, 0); return 4; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "maxConcurrencyLevel") return 5; }
        try { new ConcurrentExclusiveSchedulerPair(scheduler, -2, 0); return 6; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "maxConcurrencyLevel") return 7; }
        try { new ConcurrentExclusiveSchedulerPair(scheduler, 1, 0); return 8; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "maxItemsPerTask") return 9; }
        try { new ConcurrentExclusiveSchedulerPair(scheduler, -1, -2); return 10; } catch (ArgumentOutOfRangeException) { }
        if (scheduler.Calls != 0) return 11;
        return input + 1;
    }
    private sealed class Scheduler : TaskScheduler
    {
        public int Calls;
        public override int MaximumConcurrencyLevel { get { Calls++; return 1; } }
        protected override void QueueTask(Task task) { Calls++; }
        protected override bool TryExecuteTaskInline(Task task, bool queued) { Calls++; return false; }
        protected override IEnumerable<Task>? GetScheduledTasks() { Calls++; return null; }
    }
}
