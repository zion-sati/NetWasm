// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from TaskFactory completion promises for the single managed reactor.

namespace System.Threading.Tasks;

public partial class Task
{
    internal static Task<TTask> CompleteFactoryAny<TTask>(TTask[] tasks) where TTask : Task => WhenAnyCore(tasks);

    internal static Task<TTask[]> CompleteFactoryAll<TTask>(TTask[] tasks) where TTask : Task
    {
        var completion = new TaskCompletionSource<TTask[]>();
        var countdown = new FactoryAllContinuation<TTask>(tasks, completion);
        foreach (var task in tasks) task.RegisterContinuation(countdown, runInlineIfCompleted: true);
        return completion.Task;
    }

    private sealed class FactoryAllContinuation<TTask> : TaskContinuation where TTask : Task
    {
        private readonly TTask[] _tasks;
        private readonly TaskCompletionSource<TTask[]> _completion;
        private int _remaining;

        internal FactoryAllContinuation(TTask[] tasks, TaskCompletionSource<TTask[]> completion)
        {
            _tasks = tasks;
            _completion = completion;
            _remaining = tasks.Length;
        }

        internal override void Run(Task antecedent, bool allowInline)
        {
            // Completion alone matters. Do not read/observe antecedent exceptions.
            if (--_remaining == 0) _completion.TrySetResult(_tasks);
        }
    }
}

public partial class TaskFactory
{
    internal static Task<TTask[]> PrepareAll<TTask>(TTask[] tasks, CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions, TaskScheduler scheduler) where TTask : Task
    {
        CheckMultiTaskContinuationOptions(continuationOptions);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(scheduler);
        if (tasks.Length == 0) throw new ArgumentException("The tasks array must contain at least one task.", nameof(tasks));
        var snapshot = new TTask[tasks.Length];
        for (var index = 0; index < tasks.Length; index++)
            snapshot[index] = tasks[index] ?? throw new ArgumentException("The tasks array included at least one null element.", nameof(tasks));
        // No antecedent registration is needed for an eagerly canceled continuation.
        if (cancellationToken.IsCancellationRequested && (continuationOptions & TaskContinuationOptions.LazyCancellation) == 0)
            return Task.FromResult(snapshot);
        return Task.CompleteFactoryAll(snapshot);
    }
    internal static Task<TTask> PrepareAny<TTask>(TTask[] tasks,
        TaskContinuationOptions continuationOptions, TaskScheduler scheduler) where TTask : Task
    {
        CheckMultiTaskContinuationOptions(continuationOptions);
        ArgumentNullException.ThrowIfNull(tasks);
        if (tasks.Length == 0) throw new ArgumentException("The tasks array must contain at least one task.", nameof(tasks));
        ArgumentNullException.ThrowIfNull(scheduler);
        foreach (var task in tasks)
            if (task is null) throw new ArgumentException("The tasks array included at least one null element.", nameof(tasks));
        // Existing WhenAny tracking snapshots input and removes losing registrations
        // after the winner completes, without observing any antecedent exceptions.
        return Task.CompleteFactoryAny(tasks);
    }
}
