// From the compiler harness's TaskFactoryAnyCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace TaskFactoryAnyFixture;
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
        Task? winner = null;
        var calls = 0;
        var any = factory.ContinueWhenAny(tasks, t => { winner = t; calls++; });
        tasks[1] = Task.CompletedTask;
        second.SetException(new Exception("unobserved"));
        scheduler.Drain();
        if (!any.IsCompletedSuccessfully || winner != second.Task || calls != 1) return 1;
        first.SetCanceled(); scheduler.Drain();
        if (calls != 1) return 2;
        var cancel = new CancellationToken(true);
        var pending = new TaskCompletionSource<int>();
        var eager = generic.ContinueWhenAny(new Task[] { pending.Task }, t => 42, cancel);
        if (!eager.IsCanceled) return 3;
        var lazy = generic.ContinueWhenAny(new Task[] { pending.Task }, t => 42, cancel, TaskContinuationOptions.LazyCancellation, scheduler);
        if (lazy.IsCompleted) return 4;
        pending.SetResult(1); scheduler.Drain();
        if (!lazy.IsCanceled) return 5;
        var original = new Exception("continuation");
        var failed = generic.ContinueWhenAny(new Task[] { Task.CompletedTask }, t => throw original);
        scheduler.Drain();
        if (!ReferenceEquals(failed.Exception!.InnerException, original)) return 6;
        try { factory.ContinueWhenAny((Task[])null!, (Action<Task>)null!, cancel, (TaskContinuationOptions)(-1), null!); return 7; }
        catch (ArgumentNullException ex) { if (ex.ParamName != "continuationAction") return 8; }
        try { factory.ContinueWhenAny((Task[])null!, t => { }, cancel, (TaskContinuationOptions)(-1), null!); return 9; }
        catch (ArgumentOutOfRangeException ex) { if (ex.ParamName != "continuationOptions") return 10; }
        try { factory.ContinueWhenAny(Array.Empty<Task>(), t => { }, CancellationToken.None, TaskContinuationOptions.None, null!); return 11; }
        catch (ArgumentException ex) { if (ex.ParamName != "tasks") return 12; }
        try { factory.ContinueWhenAny(new Task[] { Task.CompletedTask, null! }, t => { }); return 13; }
        catch (ArgumentException ex) { if (ex.ParamName != "tasks") return 14; }
        var completedFirst = factory.ContinueWhenAny(new Task[] { first.Task, second.Task }, t => t == first.Task ? 42 : 0);
        scheduler.Drain();
        if (completedFirst.Result != 42) return 15;
        return Overloads(scheduler, factory, generic) ? input + 1 : 16;
    }
    private static bool Overloads(QueueScheduler scheduler, TaskFactory factory, TaskFactory<int> generic)
    {
        var calls = 0;
        var tasks = new Task[] { Task.CompletedTask };
        var typed = new Task<int>[] { Task.FromResult(42) };
        Action<Task> action = a => { if (a == tasks[0]) calls++; };
        Action<Task<int>> typedAction = a => { if (a.Result == 42) calls++; };
        Func<Task, int> function = a => a == tasks[0] ? 42 : 0;
        Func<Task<int>, int> typedFunction = a => a.Result;
        var result0 = factory.ContinueWhenAny(tasks, action);
        var result1 = factory.ContinueWhenAny(tasks, action, CancellationToken.None);
        var result2 = factory.ContinueWhenAny(tasks, action, TaskContinuationOptions.None);
        var result3 = factory.ContinueWhenAny(tasks, action, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        var result4 = factory.ContinueWhenAny(typed, typedAction);
        var result5 = factory.ContinueWhenAny(typed, typedAction, CancellationToken.None);
        var result6 = factory.ContinueWhenAny(typed, typedAction, TaskContinuationOptions.None);
        var result7 = factory.ContinueWhenAny(typed, typedAction, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        var result8 = factory.ContinueWhenAny(tasks, function);
        var result9 = factory.ContinueWhenAny(tasks, function, CancellationToken.None);
        var result10 = factory.ContinueWhenAny(tasks, function, TaskContinuationOptions.None);
        var result11 = factory.ContinueWhenAny(tasks, function, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        var result12 = factory.ContinueWhenAny(typed, typedFunction);
        var result13 = factory.ContinueWhenAny(typed, typedFunction, CancellationToken.None);
        var result14 = factory.ContinueWhenAny(typed, typedFunction, TaskContinuationOptions.None);
        var result15 = factory.ContinueWhenAny(typed, typedFunction, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        var result16 = generic.ContinueWhenAny(tasks, function);
        var result17 = generic.ContinueWhenAny(tasks, function, CancellationToken.None);
        var result18 = generic.ContinueWhenAny(tasks, function, TaskContinuationOptions.None);
        var result19 = generic.ContinueWhenAny(tasks, function, CancellationToken.None, TaskContinuationOptions.None, scheduler);
        var result20 = generic.ContinueWhenAny(typed, typedFunction);
        var result21 = generic.ContinueWhenAny(typed, typedFunction, CancellationToken.None);
        var result22 = generic.ContinueWhenAny(typed, typedFunction, TaskContinuationOptions.None);
        var result23 = generic.ContinueWhenAny(typed, typedFunction, CancellationToken.None, TaskContinuationOptions.None, scheduler);
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
