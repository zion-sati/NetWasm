// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime Task continuation contracts for a single reactor.

namespace System.Threading.Tasks;

public partial class Task
{
    public Task ContinueWith(Action<Task> continuationAction) =>
        ContinueWith(continuationAction, default, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task ContinueWith(Action<Task> continuationAction, CancellationToken cancellationToken) =>
        ContinueWith(continuationAction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task ContinueWith(Action<Task> continuationAction, TaskContinuationOptions continuationOptions) =>
        ContinueWith(continuationAction, default, continuationOptions, TaskScheduler.Current);

    public Task ContinueWith(Action<Task> continuationAction, TaskScheduler scheduler) =>
        ContinueWith(continuationAction, default, TaskContinuationOptions.None, scheduler);

    public Task ContinueWith(Action<Task> continuationAction,
        CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ValidateContinuationArguments(continuationAction, nameof(continuationAction), scheduler, continuationOptions);
        return CreateActionContinuation(this, (task, _) => continuationAction(task),
            null, cancellationToken, continuationOptions, scheduler);
    }

    public Task ContinueWith(Action<Task, object?> continuationAction, object? state) =>
        ContinueWith(continuationAction, state, default, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task ContinueWith(Action<Task, object?> continuationAction, object? state, CancellationToken cancellationToken) =>
        ContinueWith(continuationAction, state, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task ContinueWith(Action<Task, object?> continuationAction, object? state, TaskContinuationOptions continuationOptions) =>
        ContinueWith(continuationAction, state, default, continuationOptions, TaskScheduler.Current);

    public Task ContinueWith(Action<Task, object?> continuationAction, object? state, TaskScheduler scheduler) =>
        ContinueWith(continuationAction, state, default, TaskContinuationOptions.None, scheduler);

    public Task ContinueWith(Action<Task, object?> continuationAction, object? state,
        CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ValidateContinuationArguments(continuationAction, nameof(continuationAction), scheduler, continuationOptions);
        return CreateActionContinuation(this, continuationAction,
            state, cancellationToken, continuationOptions, scheduler);
    }

    public Task<TResult> ContinueWith<TResult>(Func<Task, TResult> continuationFunction) =>
        ContinueWith<TResult>(continuationFunction, default, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task<TResult> ContinueWith<TResult>(Func<Task, TResult> continuationFunction, CancellationToken cancellationToken) =>
        ContinueWith<TResult>(continuationFunction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task<TResult> ContinueWith<TResult>(Func<Task, TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
        ContinueWith<TResult>(continuationFunction, default, continuationOptions, TaskScheduler.Current);

    public Task<TResult> ContinueWith<TResult>(Func<Task, TResult> continuationFunction, TaskScheduler scheduler) =>
        ContinueWith<TResult>(continuationFunction, default, TaskContinuationOptions.None, scheduler);

    public Task<TResult> ContinueWith<TResult>(Func<Task, TResult> continuationFunction,
        CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ValidateContinuationArguments(continuationFunction, nameof(continuationFunction), scheduler, continuationOptions);
        return CreateResultContinuation(this, (task, _) => continuationFunction(task),
            null, cancellationToken, continuationOptions, scheduler);
    }

    public Task<TResult> ContinueWith<TResult>(Func<Task, object?, TResult> continuationFunction, object? state) =>
        ContinueWith<TResult>(continuationFunction, state, default, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task<TResult> ContinueWith<TResult>(Func<Task, object?, TResult> continuationFunction, object? state, CancellationToken cancellationToken) =>
        ContinueWith<TResult>(continuationFunction, state, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task<TResult> ContinueWith<TResult>(Func<Task, object?, TResult> continuationFunction, object? state, TaskContinuationOptions continuationOptions) =>
        ContinueWith<TResult>(continuationFunction, state, default, continuationOptions, TaskScheduler.Current);

    public Task<TResult> ContinueWith<TResult>(Func<Task, object?, TResult> continuationFunction, object? state, TaskScheduler scheduler) =>
        ContinueWith<TResult>(continuationFunction, state, default, TaskContinuationOptions.None, scheduler);

    public Task<TResult> ContinueWith<TResult>(Func<Task, object?, TResult> continuationFunction, object? state,
        CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ValidateContinuationArguments(continuationFunction, nameof(continuationFunction), scheduler, continuationOptions);
        return CreateResultContinuation(this, continuationFunction,
            state, cancellationToken, continuationOptions, scheduler);
    }

    private const TaskContinuationOptions ContinuationCreationOptions =
        TaskContinuationOptions.PreferFairness | TaskContinuationOptions.LongRunning |
        TaskContinuationOptions.AttachedToParent | TaskContinuationOptions.DenyChildAttach |
        TaskContinuationOptions.HideScheduler | TaskContinuationOptions.RunContinuationsAsynchronously;

    internal static void ValidateContinuationArguments(
        Delegate continuation, string delegateParameterName, TaskScheduler scheduler, TaskContinuationOptions continuationOptions)
    {
        if (continuation is null) throw new ArgumentNullException(delegateParameterName);
        ArgumentNullException.ThrowIfNull(scheduler);
        const TaskContinuationOptions exclusions = TaskContinuationOptions.NotOnRanToCompletion |
            TaskContinuationOptions.NotOnFaulted | TaskContinuationOptions.NotOnCanceled;
        const TaskContinuationOptions valid = ContinuationCreationOptions | exclusions |
            TaskContinuationOptions.LazyCancellation | TaskContinuationOptions.ExecuteSynchronously;
        if ((continuationOptions & ~valid) != 0 || (continuationOptions & exclusions) == exclusions ||
            (continuationOptions & (TaskContinuationOptions.LongRunning | TaskContinuationOptions.ExecuteSynchronously)) ==
                (TaskContinuationOptions.LongRunning | TaskContinuationOptions.ExecuteSynchronously))
            throw new ArgumentOutOfRangeException(nameof(continuationOptions));
        // Parent/child completion accounting is not part of the reactor Task
        // implementation. Reject it instead of silently treating attachment as a hint.
        if ((continuationOptions & TaskContinuationOptions.AttachedToParent) != 0)
            throw new PlatformNotSupportedException("Attached child tasks are not supported.");
    }

    internal static Task CreateActionContinuation<TAntecedent>(
        TAntecedent antecedent, Action<TAntecedent, object?> action, object? state,
        CancellationToken token, TaskContinuationOptions options, TaskScheduler scheduler)
        where TAntecedent : Task
    {
        var task = new ActionContinuationTask<TAntecedent>(antecedent, action, state,
            (TaskCreationOptions)(options & ContinuationCreationOptions));
        antecedent.AttachContinuation(task, token, options, scheduler);
        return task;
    }

    internal static Task<TResult> CreateResultContinuation<TAntecedent, TResult>(
        TAntecedent antecedent, Func<TAntecedent, object?, TResult> function, object? state,
        CancellationToken token, TaskContinuationOptions options, TaskScheduler scheduler)
        where TAntecedent : Task
    {
        var task = new ResultContinuationTask<TAntecedent, TResult>(antecedent, function, state,
            (TaskCreationOptions)(options & ContinuationCreationOptions));
        antecedent.AttachContinuation(task, token, options, scheduler);
        return task;
    }

    private void AttachContinuation(Task continuation, CancellationToken token,
        TaskContinuationOptions options, TaskScheduler scheduler)
    {
        continuation._isContinuation = true;
        continuation._capturedContext = ExecutionContext.Capture();
        continuation._cancellationToken = token;
        var registration = new ContinueWithRegistration(this, continuation, options, scheduler);
        continuation._continuationActivation = registration;
        var lazy = (options & TaskContinuationOptions.LazyCancellation) != 0;
        if (token.CanBeCanceled && !lazy)
        {
            continuation._cancellationRegistration = token.UnsafeRegister(static state =>
                ((Task)state!).CancelFromToken(), continuation);
        }
        if (continuation.IsCompleted)
        {
            // Registration may invoke synchronously for a precanceled token.
            continuation._cancellationRegistration.Dispose();
            continuation._cancellationRegistration = default;
            return;
        }
        if (IsCompleted)
            registration.Run(this, allowInline: true);
        else
            (_continuations ??= new Collections.Generic.List<TaskContinuation>()).Add(registration);
    }

    private abstract class TaskContinuation
    {
        internal abstract void Run(Task antecedent, bool allowInline);
    }

    private sealed class ContinueWithRegistration(
        Task antecedent, Task task, TaskContinuationOptions options, TaskScheduler scheduler) : TaskContinuation
    {
        private Task? _antecedent = antecedent;
        private Task? _task = task;

        internal void Detach()
        {
            _antecedent?._continuations?.Remove(this);
            _antecedent = null;
            _task = null;
        }

        internal override void Run(Task antecedent, bool allowInline)
        {
            var task = _task;
            Detach();
            if (task is null || task.IsCompleted)
                return;
            task._continuationActivation = null;
            // OnlyOn* are composite aliases of these exclusion bits.
            var excluded = antecedent.IsCompletedSuccessfully ? TaskContinuationOptions.NotOnRanToCompletion
                : antecedent.IsCanceled ? TaskContinuationOptions.NotOnCanceled
                : TaskContinuationOptions.NotOnFaulted;
            if (task._cancellationToken.IsCancellationRequested || (options & excluded) != 0)
            {
                task.TrySetCanceled(new TaskCanceledException(null, null, task._cancellationToken));
                return;
            }
            task._scheduler = scheduler;
            task._started = true;
            try
            {
                if (!allowInline || (options & TaskContinuationOptions.ExecuteSynchronously) == 0 ||
                    !scheduler.TryRunInline(task, previouslyQueued: false))
                    scheduler.QueueTask(task);
            }
            catch (Exception exception)
            {
                // Internal activation has no throwing caller to observe the
                // failure. Leave it on the returned Task until that Task is consumed.
                task.TrySetException(new TaskSchedulerException(exception));
            }
        }
    }

    private sealed class ActionContinuationTask<TAntecedent>(
        TAntecedent antecedent, Action<TAntecedent, object?> action, object? state,
        TaskCreationOptions options) : Task(state, options) where TAntecedent : Task
    {
        private TAntecedent? _antecedent = antecedent;
        private Action<TAntecedent, object?>? _continuation = action;

        protected override void ExecuteAction()
        {
            var antecedent = _antecedent!;
            var action = _continuation!;
            try
            {
                ExecuteAsCurrent(() => action(antecedent, AsyncState));
                TrySetResult();
            }
            catch (OperationCanceledException exception)
            {
                if (CancellationToken.IsCancellationRequested && exception.CancellationToken == CancellationToken)
                    TrySetCanceled(exception);
                else
                    TrySetException(exception);
            }
            catch (Exception exception) { TrySetException(exception); }
        }

        internal override void ClearExecutionReferences()
        {
            _antecedent = null;
            _continuation = null;
            base.ClearExecutionReferences();
        }
    }

    private sealed class ResultContinuationTask<TAntecedent, TResult>(
        TAntecedent antecedent, Func<TAntecedent, object?, TResult> function, object? state,
        TaskCreationOptions options) : Task<TResult>(state, options) where TAntecedent : Task
    {
        private TAntecedent? _antecedent = antecedent;
        private Func<TAntecedent, object?, TResult>? _continuation = function;

        protected override void ExecuteAction()
        {
            var antecedent = _antecedent!;
            var function = _continuation!;
            try
            {
                var result = default(TResult)!;
                ExecuteAsCurrent(() => result = function(antecedent, AsyncState));
                TrySetResult(result);
            }
            catch (OperationCanceledException exception)
            {
                if (CancellationToken.IsCancellationRequested && exception.CancellationToken == CancellationToken)
                    TrySetCanceled(exception);
                else
                    TrySetException(exception);
            }
            catch (Exception exception) { TrySetException(exception); }
        }

        internal override void ClearExecutionReferences()
        {
            _antecedent = null;
            _continuation = null;
            base.ClearExecutionReferences();
        }
    }
}

public partial class Task<T>
{
    public Task ContinueWith(Action<Task<T>> continuationAction) =>
        ContinueWith(continuationAction, default, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task ContinueWith(Action<Task<T>> continuationAction, CancellationToken cancellationToken) =>
        ContinueWith(continuationAction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task ContinueWith(Action<Task<T>> continuationAction, TaskContinuationOptions continuationOptions) =>
        ContinueWith(continuationAction, default, continuationOptions, TaskScheduler.Current);

    public Task ContinueWith(Action<Task<T>> continuationAction, TaskScheduler scheduler) =>
        ContinueWith(continuationAction, default, TaskContinuationOptions.None, scheduler);

    public Task ContinueWith(Action<Task<T>> continuationAction,
        CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ValidateContinuationArguments(continuationAction, nameof(continuationAction), scheduler, continuationOptions);
        return CreateActionContinuation(this, (task, _) => continuationAction(task),
            null, cancellationToken, continuationOptions, scheduler);
    }

    public Task ContinueWith(Action<Task<T>, object?> continuationAction, object? state) =>
        ContinueWith(continuationAction, state, default, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task ContinueWith(Action<Task<T>, object?> continuationAction, object? state, CancellationToken cancellationToken) =>
        ContinueWith(continuationAction, state, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task ContinueWith(Action<Task<T>, object?> continuationAction, object? state, TaskContinuationOptions continuationOptions) =>
        ContinueWith(continuationAction, state, default, continuationOptions, TaskScheduler.Current);

    public Task ContinueWith(Action<Task<T>, object?> continuationAction, object? state, TaskScheduler scheduler) =>
        ContinueWith(continuationAction, state, default, TaskContinuationOptions.None, scheduler);

    public Task ContinueWith(Action<Task<T>, object?> continuationAction, object? state,
        CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ValidateContinuationArguments(continuationAction, nameof(continuationAction), scheduler, continuationOptions);
        return CreateActionContinuation(this, continuationAction,
            state, cancellationToken, continuationOptions, scheduler);
    }

    public Task<TResult> ContinueWith<TResult>(Func<Task<T>, TResult> continuationFunction) =>
        ContinueWith<TResult>(continuationFunction, default, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task<TResult> ContinueWith<TResult>(Func<Task<T>, TResult> continuationFunction, CancellationToken cancellationToken) =>
        ContinueWith<TResult>(continuationFunction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task<TResult> ContinueWith<TResult>(Func<Task<T>, TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
        ContinueWith<TResult>(continuationFunction, default, continuationOptions, TaskScheduler.Current);

    public Task<TResult> ContinueWith<TResult>(Func<Task<T>, TResult> continuationFunction, TaskScheduler scheduler) =>
        ContinueWith<TResult>(continuationFunction, default, TaskContinuationOptions.None, scheduler);

    public Task<TResult> ContinueWith<TResult>(Func<Task<T>, TResult> continuationFunction,
        CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ValidateContinuationArguments(continuationFunction, nameof(continuationFunction), scheduler, continuationOptions);
        return CreateResultContinuation(this, (task, _) => continuationFunction(task),
            null, cancellationToken, continuationOptions, scheduler);
    }

    public Task<TResult> ContinueWith<TResult>(Func<Task<T>, object?, TResult> continuationFunction, object? state) =>
        ContinueWith<TResult>(continuationFunction, state, default, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task<TResult> ContinueWith<TResult>(Func<Task<T>, object?, TResult> continuationFunction, object? state, CancellationToken cancellationToken) =>
        ContinueWith<TResult>(continuationFunction, state, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    public Task<TResult> ContinueWith<TResult>(Func<Task<T>, object?, TResult> continuationFunction, object? state, TaskContinuationOptions continuationOptions) =>
        ContinueWith<TResult>(continuationFunction, state, default, continuationOptions, TaskScheduler.Current);

    public Task<TResult> ContinueWith<TResult>(Func<Task<T>, object?, TResult> continuationFunction, object? state, TaskScheduler scheduler) =>
        ContinueWith<TResult>(continuationFunction, state, default, TaskContinuationOptions.None, scheduler);

    public Task<TResult> ContinueWith<TResult>(Func<Task<T>, object?, TResult> continuationFunction, object? state,
        CancellationToken cancellationToken, TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ValidateContinuationArguments(continuationFunction, nameof(continuationFunction), scheduler, continuationOptions);
        return CreateResultContinuation(this, continuationFunction,
            state, cancellationToken, continuationOptions, scheduler);
    }

}
