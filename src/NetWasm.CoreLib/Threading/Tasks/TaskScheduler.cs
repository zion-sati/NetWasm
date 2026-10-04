// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from System.Private.CoreLib TaskScheduler for a single managed reactor.

using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace System.Threading.Tasks;

public abstract class TaskScheduler
{
    private static int s_nextId;
    private int _id;
    private readonly bool _isDefaultScheduler;

    protected TaskScheduler() { }

    private TaskScheduler(bool isDefaultScheduler) =>
        _isDefaultScheduler = isDefaultScheduler;

    public static TaskScheduler Default => DefaultScheduler.Instance;
    public static TaskScheduler Current => Task.InternalCurrentScheduler ?? Default;
    public virtual int MaximumConcurrencyLevel => int.MaxValue;
    internal bool IsDefaultScheduler => _isDefaultScheduler;

    public int Id
    {
        get
        {
            if (_id == 0)
            {
                do { _id = unchecked(++s_nextId); } while (_id == 0);
            }
            return _id;
        }
    }

    public static event EventHandler<UnobservedTaskExceptionEventArgs>? UnobservedTaskException;

    public static TaskScheduler FromCurrentSynchronizationContext() => new SynchronizationContextTaskScheduler();

    protected internal abstract void QueueTask(Task task);
    protected abstract bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued);
    protected abstract IEnumerable<Task>? GetScheduledTasks();
    protected internal virtual bool TryDequeue(Task task) => false;

    protected bool TryExecuteTask(Task task)
    {
        if (task.ExecutingTaskScheduler != this)
            throw new InvalidOperationException("The task is associated with a different scheduler.");
        return task.ExecuteEntry();
    }

    internal bool TryRunInline(Task task, bool previouslyQueued)
    {
        if (task.ExecutingTaskScheduler is null || task.HasEnteredExecution || task.IsCompleted)
            return false;
        if (task.ExecutingTaskScheduler != this)
            return task.ExecutingTaskScheduler.TryRunInline(task, previouslyQueued);
        var inlined = TryExecuteTaskInline(task, previouslyQueued);
        if (inlined && !task.HasEnteredExecution && !task.IsCanceled)
            throw new InvalidOperationException("The scheduler reported inline execution without executing the task.");
        return inlined;
    }

    internal static void PublishUnobservedTaskException(Task sender, UnobservedTaskExceptionEventArgs args) =>
        UnobservedTaskException?.Invoke(sender, args);

    // Event-only consumers must not retain a platform scheduler or its queue.
    private static class DefaultScheduler
    {
        internal static readonly TaskScheduler Instance = new ReactorTaskScheduler();
    }

    private sealed class ReactorTaskScheduler : TaskScheduler
    {
        internal ReactorTaskScheduler()
            : base(isDefaultScheduler: true)
        {
        }

        public override int MaximumConcurrencyLevel => 1;

        protected internal override void QueueTask(Task task) =>
            PlatformServices.Scheduler.Schedule(() => TryExecuteTask(task), 0);

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => TryExecuteTask(task);

        protected override IEnumerable<Task>? GetScheduledTasks() =>
            throw new NotSupportedException("The host scheduler does not expose its pending task queue.");
    }

    private sealed class SynchronizationContextTaskScheduler : TaskScheduler
    {
        private readonly SynchronizationContext _context = SynchronizationContext.Current ??
            throw new InvalidOperationException("There is no current SynchronizationContext.");

        public override int MaximumConcurrencyLevel => 1;

        protected internal override void QueueTask(Task task) =>
            _context.Post(static state => ((Task)state!).ExecuteEntry(), task);

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) =>
            SynchronizationContext.Current == _context && TryExecuteTask(task);

        protected override IEnumerable<Task>? GetScheduledTasks() => null;
    }
}
