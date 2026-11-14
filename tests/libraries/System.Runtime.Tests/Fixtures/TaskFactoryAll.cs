// From the compiler harness's TaskFactoryAllCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace TaskFactoryAllFixture;
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
        var factory = new TaskFactory(CancellationToken.None, TaskCreationOptions.None, TaskContinuationOptions.ExecuteSynchronously, scheduler);
        var generic = new TaskFactory<int>(CancellationToken.None, TaskCreationOptions.None, TaskContinuationOptions.ExecuteSynchronously, scheduler);
        var first = new TaskCompletionSource<int>();
        var second = new TaskCompletionSource<int>();
        var tasks = new Task[] { first.Task, second.Task, first.Task };
        Task[]? received = null;
        var all = factory.ContinueWhenAll(tasks, a => { received = a; });
        tasks[0] = Task.CompletedTask;
        first.SetException(new Exception("unobserved"));
        if (all.IsCompleted || scheduler.Queued != 0) return 1;
        second.SetCanceled();
        scheduler.Drain();
        if (!all.IsCompletedSuccessfully || received == null || ReferenceEquals(received, tasks) || received.Length != 3 || received[0] != first.Task || received[1] != second.Task || received[2] != first.Task) return 2;
        var cancel = new CancellationToken(true);
        var pending = new TaskCompletionSource<int>();
        var eager = generic.ContinueWhenAll(new Task[] { pending.Task }, a => 42, cancel);
        if (!eager.IsCanceled) return 3;
        var lazy = generic.ContinueWhenAll(new Task[] { pending.Task }, a => 42, cancel, TaskContinuationOptions.LazyCancellation, scheduler);
        if (lazy.IsCompleted) return 4;
        pending.SetResult(1); scheduler.Drain();
        if (!lazy.IsCanceled) return 5;
        var original = new Exception("continuation");
        var failed = generic.ContinueWhenAll(new Task[] { Task.CompletedTask }, a => throw original);
        scheduler.Drain();
        if (!ReferenceEquals(failed.Exception!.InnerException, original)) return 6;
        try { factory.ContinueWhenAll((Task[])null!, (Action<Task[]>)null!, cancel, (TaskContinuationOptions)(-1), null!); return 7; }
        catch (ArgumentNullException ex) { if (ex.ParamName != "continuationAction") return 8; }
        try { factory.ContinueWhenAll((Task[])null!, a => { }, cancel, (TaskContinuationOptions)(-1), null!); return 9; }
        catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "continuationOptions") return 10; }
        try { factory.ContinueWhenAll(Array.Empty<Task>(), a => { }); return 11; }
        catch (ArgumentException ex) { if (ex.ParamName != "tasks") return 12; }
        try { factory.ContinueWhenAll(new Task[] { null! }, a => { }); return 13; }
        catch (ArgumentException ex) { if (ex.ParamName != "tasks") return 14; }
        return Overloads(scheduler, factory, generic) ? input + 1 : 15;
    }
    private static bool Overloads(QueueScheduler scheduler, TaskFactory factory, TaskFactory<int> generic)
    {
        var calls = 0;
        var tasks = new Task[] { Task.CompletedTask };
        var typed = new Task<int>[] { Task.FromResult(42) };
        Action<Task[]> action = a => { if (a.Length == 1 && a[0] == tasks[0]) calls++; };
        Action<Task<int>[]> typedAction = a => { if (a.Length == 1 && a[0].Result == 42) calls++; };
        Func<Task[], int> function = a => a.Length == 1 && a[0] == tasks[0] ? 42 : 0;
        Func<Task<int>[], int> typedFunction = a => a.Length == 1 ? a[0].Result : 0;
        var result0 = factory.ContinueWhenAll(tasks, action);
        var result1 = factory.ContinueWhenAll(tasks, action, CancellationToken.None);
        var result2 = factory.ContinueWhenAll(tasks, action, TaskContinuationOptions.None);
        var result3 = factory.ContinueWhenAll(tasks, action, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        var result4 = factory.ContinueWhenAll(typed, typedAction);
        var result5 = factory.ContinueWhenAll(typed, typedAction, CancellationToken.None);
        var result6 = factory.ContinueWhenAll(typed, typedAction, TaskContinuationOptions.None);
        var result7 = factory.ContinueWhenAll(typed, typedAction, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        var result8 = factory.ContinueWhenAll(tasks, function);
        var result9 = factory.ContinueWhenAll(tasks, function, CancellationToken.None);
        var result10 = factory.ContinueWhenAll(tasks, function, TaskContinuationOptions.None);
        var result11 = factory.ContinueWhenAll(tasks, function, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        var result12 = factory.ContinueWhenAll(typed, typedFunction);
        var result13 = factory.ContinueWhenAll(typed, typedFunction, CancellationToken.None);
        var result14 = factory.ContinueWhenAll(typed, typedFunction, TaskContinuationOptions.None);
        var result15 = factory.ContinueWhenAll(typed, typedFunction, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        var result16 = generic.ContinueWhenAll(tasks, function);
        var result17 = generic.ContinueWhenAll(tasks, function, CancellationToken.None);
        var result18 = generic.ContinueWhenAll(tasks, function, TaskContinuationOptions.None);
        var result19 = generic.ContinueWhenAll(tasks, function, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        var result20 = generic.ContinueWhenAll(typed, typedFunction);
        var result21 = generic.ContinueWhenAll(typed, typedFunction, CancellationToken.None);
        var result22 = generic.ContinueWhenAll(typed, typedFunction, TaskContinuationOptions.None);
        var result23 = generic.ContinueWhenAll(typed, typedFunction, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        scheduler.Drain();
        if (calls != 8) return false;
        if (!result0.IsCompletedSuccessfully) return false;
        if (!result1.IsCompletedSuccessfully) return false;
        if (!result2.IsCompletedSuccessfully) return false;
        if (!result3.IsCompletedSuccessfully) return false;
        if (!result4.IsCompletedSuccessfully) return false;
        if (!result5.IsCompletedSuccessfully) return false;
        if (!result6.IsCompletedSuccessfully) return false;
        if (!result7.IsCompletedSuccessfully) return false;
        if (result8.Result != 42) return false;
        if (result9.Result != 42) return false;
        if (result10.Result != 42) return false;
        if (result11.Result != 42) return false;
        if (result12.Result != 42) return false;
        if (result13.Result != 42) return false;
        if (result14.Result != 42) return false;
        if (result15.Result != 42) return false;
        if (result16.Result != 42) return false;
        if (result17.Result != 42) return false;
        if (result18.Result != 42) return false;
        if (result19.Result != 42) return false;
        if (result20.Result != 42) return false;
        if (result21.Result != 42) return false;
        if (result22.Result != 42) return false;
        if (result23.Result != 42) return false;
        return true;
    }
}
