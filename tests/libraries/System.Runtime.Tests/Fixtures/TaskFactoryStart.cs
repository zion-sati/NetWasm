// From the compiler harness's TaskFactoryStartCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace TaskFactoryStartFixture;
public sealed class QueueScheduler : TaskScheduler
{
    public readonly Queue<Task> Tasks = new Queue<Task>();
    public int Queued;
    public bool Fail;
    protected override void QueueTask(Task task) { if (Fail) throw new InvalidOperationException("queue"); Queued++; Tasks.Enqueue(task); }
    protected override bool TryExecuteTaskInline(Task task, bool queued) => false;
    protected override IEnumerable<Task> GetScheduledTasks() => Tasks;
    public void Drain() { while (Tasks.Count != 0) TryExecuteTask(Tasks.Dequeue()); }
}
public static class EntryPoint
{
    public static int Run(int input)
    {
        var scheduler = new QueueScheduler();
        var factory = new TaskFactory(scheduler);
        var generic = new TaskFactory<int>(scheduler);
        var calls = 0;
        var marker = new object();
        var plain = factory.StartNew(() => calls++);
        var state = factory.StartNew(o => { if (ReferenceEquals(o, marker)) calls++; }, marker);
        var result = factory.StartNew(() => 42);
        var stateResult = generic.StartNew(o => ReferenceEquals(o, marker) ? 42 : 0, marker);
        if (calls != 0 || scheduler.Queued != 4 || plain.IsCompleted || state.AsyncState != marker) return 1;
        scheduler.Drain();
        if (calls != 2 || !plain.IsCompletedSuccessfully || result.Result != 42 || stateResult.Result != 42) return 2;
        var cancel = new CancellationToken(true);
        var canceled = factory.StartNew(() => calls++, cancel);
        if (scheduler.Queued != 5 || canceled.IsCompleted) return 3;
        scheduler.Drain();
        if (!canceled.IsCanceled || calls != 2) return 4;
        var original = new Exception("delegate");
        var fault = generic.StartNew(() => throw original);
        scheduler.Drain();
        if (!fault.IsFaulted || !ReferenceEquals(fault.Exception!.InnerException, original)) return 5;
        var nestedScheduler = false;
        var nested = factory.StartNew(() => { Task.Factory.StartNew(() => nestedScheduler = TaskScheduler.Current == scheduler); });
        scheduler.Drain();
        if (!nestedScheduler || !nested.IsCompletedSuccessfully) return 6;
        var hidden = false;
        factory.StartNew(() => hidden = TaskScheduler.Current == TaskScheduler.Default, TaskCreationOptions.HideScheduler);
        scheduler.Drain();
        if (!hidden) return 7;
        using (var cts = new CancellationTokenSource())
        {
            var later = generic.StartNew(() => ++calls, cts.Token);
            cts.Cancel();
            if (later.IsCompleted) return 8;
            scheduler.Drain();
            if (!later.IsCanceled || calls != 2) return 9;
        }
        var bad = new QueueScheduler { Fail = true };
        try { Task.Factory.StartNew(() => { }, CancellationToken.None, TaskCreationOptions.None, bad); return 10; }
        catch (TaskSchedulerException ex) { if (ex.InnerException is not InvalidOperationException) return 11; }
        try { factory.StartNew((Action)null!, CancellationToken.None, TaskCreationOptions.None, null!); return 12; }
        catch (ArgumentNullException ex) { if (ex.ParamName != "scheduler") return 13; }
        try { generic.StartNew((Func<int>)null!, CancellationToken.None, TaskCreationOptions.None, scheduler); return 14; }
        catch (ArgumentNullException ex) { if (ex.ParamName != "function") return 15; }
        if (!Overloads()) return 16;
        var ambient = new AsyncLocal<int>();
        ambient.Value = 42;
        var captured = generic.StartNew(() => ambient.Value);
        ambient.Value = 7;
        scheduler.Drain();
        if (captured.Result != 42 || ambient.Value != 7) return 17;
        return input + 1;
    }
    private static bool Overloads()
    {
        var scheduler = new QueueScheduler();
        var alternate = new QueueScheduler();
        var factory = new TaskFactory(CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskContinuationOptions.None, scheduler);
        var generic = new TaskFactory<int>(CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskContinuationOptions.None, scheduler);
        var marker = new object();
        var calls = 0;
        Action action = () => { calls++; };
        Action<object?> stateAction = o => { if (ReferenceEquals(o, marker)) calls++; };
        Func<int> function = () => 42;
        Func<object?, int> stateFunction = o => ReferenceEquals(o, marker) ? 42 : 0;
        var task0 = factory.StartNew(action);
        var task1 = factory.StartNew(action, CancellationToken.None);
        var task2 = factory.StartNew(action, TaskCreationOptions.HideScheduler);
        var task3 = factory.StartNew(action, CancellationToken.None, TaskCreationOptions.HideScheduler, alternate);
        var task4 = factory.StartNew(stateAction, marker);
        var task5 = factory.StartNew(stateAction, marker, CancellationToken.None);
        var task6 = factory.StartNew(stateAction, marker, TaskCreationOptions.HideScheduler);
        var task7 = factory.StartNew(stateAction, marker, CancellationToken.None, TaskCreationOptions.HideScheduler, alternate);
        var task8 = factory.StartNew(function);
        var task9 = factory.StartNew(function, CancellationToken.None);
        var task10 = factory.StartNew(function, TaskCreationOptions.HideScheduler);
        var task11 = factory.StartNew(function, CancellationToken.None, TaskCreationOptions.HideScheduler, alternate);
        var task12 = factory.StartNew(stateFunction, marker);
        var task13 = factory.StartNew(stateFunction, marker, CancellationToken.None);
        var task14 = factory.StartNew(stateFunction, marker, TaskCreationOptions.HideScheduler);
        var task15 = factory.StartNew(stateFunction, marker, CancellationToken.None, TaskCreationOptions.HideScheduler, alternate);
        var task16 = generic.StartNew(function);
        var task17 = generic.StartNew(function, CancellationToken.None);
        var task18 = generic.StartNew(function, TaskCreationOptions.HideScheduler);
        var task19 = generic.StartNew(function, CancellationToken.None, TaskCreationOptions.HideScheduler, alternate);
        var task20 = generic.StartNew(stateFunction, marker);
        var task21 = generic.StartNew(stateFunction, marker, CancellationToken.None);
        var task22 = generic.StartNew(stateFunction, marker, TaskCreationOptions.HideScheduler);
        var task23 = generic.StartNew(stateFunction, marker, CancellationToken.None, TaskCreationOptions.HideScheduler, alternate);
        if (scheduler.Queued != 18 || alternate.Queued != 6 || calls != 0) return false;
        scheduler.Drain(); alternate.Drain();
        if (calls != 8) return false;
        if (task0.CreationOptions != TaskCreationOptions.DenyChildAttach) return false;
        if (!ReferenceEquals(task0.AsyncState, null)) return false;
        if (!task0.IsCompletedSuccessfully) return false;
        if (task1.CreationOptions != TaskCreationOptions.DenyChildAttach) return false;
        if (!ReferenceEquals(task1.AsyncState, null)) return false;
        if (!task1.IsCompletedSuccessfully) return false;
        if (task2.CreationOptions != TaskCreationOptions.HideScheduler) return false;
        if (!ReferenceEquals(task2.AsyncState, null)) return false;
        if (!task2.IsCompletedSuccessfully) return false;
        if (task3.CreationOptions != TaskCreationOptions.HideScheduler) return false;
        if (!ReferenceEquals(task3.AsyncState, null)) return false;
        if (!task3.IsCompletedSuccessfully) return false;
        if (task4.CreationOptions != TaskCreationOptions.DenyChildAttach) return false;
        if (!ReferenceEquals(task4.AsyncState, marker)) return false;
        if (!task4.IsCompletedSuccessfully) return false;
        if (task5.CreationOptions != TaskCreationOptions.DenyChildAttach) return false;
        if (!ReferenceEquals(task5.AsyncState, marker)) return false;
        if (!task5.IsCompletedSuccessfully) return false;
        if (task6.CreationOptions != TaskCreationOptions.HideScheduler) return false;
        if (!ReferenceEquals(task6.AsyncState, marker)) return false;
        if (!task6.IsCompletedSuccessfully) return false;
        if (task7.CreationOptions != TaskCreationOptions.HideScheduler) return false;
        if (!ReferenceEquals(task7.AsyncState, marker)) return false;
        if (!task7.IsCompletedSuccessfully) return false;
        if (task8.CreationOptions != TaskCreationOptions.DenyChildAttach) return false;
        if (!ReferenceEquals(task8.AsyncState, null)) return false;
        if (task8.Result != 42) return false;
        if (task9.CreationOptions != TaskCreationOptions.DenyChildAttach) return false;
        if (!ReferenceEquals(task9.AsyncState, null)) return false;
        if (task9.Result != 42) return false;
        if (task10.CreationOptions != TaskCreationOptions.HideScheduler) return false;
        if (!ReferenceEquals(task10.AsyncState, null)) return false;
        if (task10.Result != 42) return false;
        if (task11.CreationOptions != TaskCreationOptions.HideScheduler) return false;
        if (!ReferenceEquals(task11.AsyncState, null)) return false;
        if (task11.Result != 42) return false;
        if (task12.CreationOptions != TaskCreationOptions.DenyChildAttach) return false;
        if (!ReferenceEquals(task12.AsyncState, marker)) return false;
        if (task12.Result != 42) return false;
        if (task13.CreationOptions != TaskCreationOptions.DenyChildAttach) return false;
        if (!ReferenceEquals(task13.AsyncState, marker)) return false;
        if (task13.Result != 42) return false;
        if (task14.CreationOptions != TaskCreationOptions.HideScheduler) return false;
        if (!ReferenceEquals(task14.AsyncState, marker)) return false;
        if (task14.Result != 42) return false;
        if (task15.CreationOptions != TaskCreationOptions.HideScheduler) return false;
        if (!ReferenceEquals(task15.AsyncState, marker)) return false;
        if (task15.Result != 42) return false;
        if (task16.CreationOptions != TaskCreationOptions.DenyChildAttach) return false;
        if (!ReferenceEquals(task16.AsyncState, null)) return false;
        if (task16.Result != 42) return false;
        if (task17.CreationOptions != TaskCreationOptions.DenyChildAttach) return false;
        if (!ReferenceEquals(task17.AsyncState, null)) return false;
        if (task17.Result != 42) return false;
        if (task18.CreationOptions != TaskCreationOptions.HideScheduler) return false;
        if (!ReferenceEquals(task18.AsyncState, null)) return false;
        if (task18.Result != 42) return false;
        if (task19.CreationOptions != TaskCreationOptions.HideScheduler) return false;
        if (!ReferenceEquals(task19.AsyncState, null)) return false;
        if (task19.Result != 42) return false;
        if (task20.CreationOptions != TaskCreationOptions.DenyChildAttach) return false;
        if (!ReferenceEquals(task20.AsyncState, marker)) return false;
        if (task20.Result != 42) return false;
        if (task21.CreationOptions != TaskCreationOptions.DenyChildAttach) return false;
        if (!ReferenceEquals(task21.AsyncState, marker)) return false;
        if (task21.Result != 42) return false;
        if (task22.CreationOptions != TaskCreationOptions.HideScheduler) return false;
        if (!ReferenceEquals(task22.AsyncState, marker)) return false;
        if (task22.Result != 42) return false;
        if (task23.CreationOptions != TaskCreationOptions.HideScheduler) return false;
        if (!ReferenceEquals(task23.AsyncState, marker)) return false;
        if (task23.Result != 42) return false;
        var canceled = new CancellationToken(true);
        var canceledFactory = new TaskFactory(canceled, TaskCreationOptions.None, TaskContinuationOptions.None, scheduler);
        var canceledGeneric = new TaskFactory<int>(canceled, TaskCreationOptions.None, TaskContinuationOptions.None, scheduler);
        var inherited = canceledFactory.StartNew(action);
        var overridden = canceledFactory.StartNew(action, CancellationToken.None);
        var inheritedResult = canceledGeneric.StartNew(function);
        var overriddenResult = canceledGeneric.StartNew(function, CancellationToken.None);
        scheduler.Drain();
        return inherited.IsCanceled && inheritedResult.IsCanceled && overridden.IsCompletedSuccessfully && overriddenResult.Result == 42 && calls == 9;
    }

}
