// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from System.Private.CoreLib TaskFactory configuration contracts.

namespace System.Threading.Tasks;

public partial class TaskFactory
{
    private readonly CancellationToken _cancellationToken;
    private readonly TaskScheduler? _scheduler;
    private readonly TaskCreationOptions _creationOptions;
    private readonly TaskContinuationOptions _continuationOptions;

    public TaskFactory() { }
    public TaskFactory(CancellationToken cancellationToken) => _cancellationToken = cancellationToken;
    public TaskFactory(TaskScheduler? scheduler) => _scheduler = scheduler;
    public TaskFactory(TaskCreationOptions creationOptions, TaskContinuationOptions continuationOptions)
    {
        TaskFactory.CheckMultiTaskContinuationOptions(continuationOptions);
        TaskFactory.CheckCreationOptions(creationOptions);
        _creationOptions = creationOptions;
        _continuationOptions = continuationOptions;
    }
    public TaskFactory(CancellationToken cancellationToken, TaskCreationOptions creationOptions,
        TaskContinuationOptions continuationOptions, TaskScheduler? scheduler)
        : this(creationOptions, continuationOptions)
    {
        _cancellationToken = cancellationToken;
        _scheduler = scheduler;
    }
    public CancellationToken CancellationToken => _cancellationToken;
    public TaskScheduler? Scheduler => _scheduler;
    public TaskCreationOptions CreationOptions => _creationOptions;
    public TaskContinuationOptions ContinuationOptions => _continuationOptions;
    internal static void CheckCreationOptions(TaskCreationOptions creationOptions)
    {
        // Check for validity of options
        if ((creationOptions &
                ~(TaskCreationOptions.AttachedToParent |
                  TaskCreationOptions.DenyChildAttach |
                  TaskCreationOptions.HideScheduler |
                  TaskCreationOptions.LongRunning |
                  TaskCreationOptions.PreferFairness |
                  TaskCreationOptions.RunContinuationsAsynchronously)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(creationOptions));
        }
    }

    internal static void CheckMultiTaskContinuationOptions(TaskContinuationOptions continuationOptions)
    {
        // Construct a mask to check for illegal options
        const TaskContinuationOptions NotOnAny = TaskContinuationOptions.NotOnCanceled |
                                           TaskContinuationOptions.NotOnFaulted |
                                           TaskContinuationOptions.NotOnRanToCompletion;

        // Check that LongRunning and ExecuteSynchronously are not specified together
        const TaskContinuationOptions illegalMask = TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.LongRunning;
        if ((continuationOptions & illegalMask) == illegalMask)
        {
            throw new ArgumentOutOfRangeException(nameof(continuationOptions), "The specified TaskContinuationOptions combined LongRunning and ExecuteSynchronously.");
        }

        // Check that no nonsensical options are specified.
        if ((continuationOptions & ~(
            TaskContinuationOptions.LongRunning |
            TaskContinuationOptions.PreferFairness |
            TaskContinuationOptions.AttachedToParent |
            TaskContinuationOptions.DenyChildAttach |
            TaskContinuationOptions.HideScheduler |
            TaskContinuationOptions.LazyCancellation |
            NotOnAny |
            TaskContinuationOptions.ExecuteSynchronously)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(continuationOptions));
        }

        // Check that no "fire" options are specified.
        if ((continuationOptions & NotOnAny) != 0)
            throw new ArgumentOutOfRangeException(nameof(continuationOptions), "It is invalid to exclude specific continuation kinds for continuations off of multiple tasks.");
    }
    public Task StartNew(Action action) =>
        StartNew(action, _cancellationToken, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task StartNew(Action action, CancellationToken cancellationToken) =>
        StartNew(action, cancellationToken, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task StartNew(Action action, TaskCreationOptions creationOptions) =>
        StartNew(action, _cancellationToken, creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task StartNew(Action action, CancellationToken cancellationToken,
        TaskCreationOptions creationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(action);
        var task = new Task((Delegate)action, null, cancellationToken, creationOptions, true);
        task.StartFromFactory(scheduler);
        return task;
    }

    public Task StartNew(Action<object?> action, object? state) =>
        StartNew(action, state, _cancellationToken, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task StartNew(Action<object?> action, object? state, CancellationToken cancellationToken) =>
        StartNew(action, state, cancellationToken, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task StartNew(Action<object?> action, object? state, TaskCreationOptions creationOptions) =>
        StartNew(action, state, _cancellationToken, creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task StartNew(Action<object?> action, object? state, CancellationToken cancellationToken,
        TaskCreationOptions creationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(action);
        var task = new Task((Delegate)action, state, cancellationToken, creationOptions, true);
        task.StartFromFactory(scheduler);
        return task;
    }

    public Task<TResult> StartNew<TResult>(Func<TResult> function) =>
        StartNew<TResult>(function, _cancellationToken, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> StartNew<TResult>(Func<TResult> function, CancellationToken cancellationToken) =>
        StartNew<TResult>(function, cancellationToken, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> StartNew<TResult>(Func<TResult> function, TaskCreationOptions creationOptions) =>
        StartNew<TResult>(function, _cancellationToken, creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> StartNew<TResult>(Func<TResult> function, CancellationToken cancellationToken,
        TaskCreationOptions creationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(function);
        var task = new Task<TResult>((Delegate)function, null, cancellationToken, creationOptions, true);
        task.StartFromFactory(scheduler);
        return task;
    }

    public Task<TResult> StartNew<TResult>(Func<object?, TResult> function, object? state) =>
        StartNew<TResult>(function, state, _cancellationToken, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> StartNew<TResult>(Func<object?, TResult> function, object? state, CancellationToken cancellationToken) =>
        StartNew<TResult>(function, state, cancellationToken, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> StartNew<TResult>(Func<object?, TResult> function, object? state, TaskCreationOptions creationOptions) =>
        StartNew<TResult>(function, state, _cancellationToken, creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> StartNew<TResult>(Func<object?, TResult> function, object? state, CancellationToken cancellationToken,
        TaskCreationOptions creationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(function);
        var task = new Task<TResult>((Delegate)function, state, cancellationToken, creationOptions, true);
        task.StartFromFactory(scheduler);
        return task;
    }

    public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction) =>
        ContinueWhenAll(tasks, continuationAction, _cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction, CancellationToken cancellationToken) =>
        ContinueWhenAll(tasks, continuationAction, cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction, TaskContinuationOptions continuationOptions) =>
        ContinueWhenAll(tasks, continuationAction, _cancellationToken, continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task ContinueWhenAll(Task[] tasks, Action<Task[]> continuationAction, CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(continuationAction);
        var starter = TaskFactory.PrepareAll(tasks, cancellationToken, continuationOptions, scheduler);
        return starter.ContinueWith(completed => continuationAction(completed.Result), cancellationToken, continuationOptions, scheduler);
    }

    public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction) =>
        ContinueWhenAll<TAntecedentResult>(tasks, continuationAction, _cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction, CancellationToken cancellationToken) =>
        ContinueWhenAll<TAntecedentResult>(tasks, continuationAction, cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction, TaskContinuationOptions continuationOptions) =>
        ContinueWhenAll<TAntecedentResult>(tasks, continuationAction, _cancellationToken, continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>[]> continuationAction, CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(continuationAction);
        var starter = TaskFactory.PrepareAll(tasks, cancellationToken, continuationOptions, scheduler);
        return starter.ContinueWith(completed => continuationAction(completed.Result), cancellationToken, continuationOptions, scheduler);
    }

    public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction) =>
        ContinueWhenAll<TResult>(tasks, continuationFunction, _cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction, CancellationToken cancellationToken) =>
        ContinueWhenAll<TResult>(tasks, continuationFunction, cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
        ContinueWhenAll<TResult>(tasks, continuationFunction, _cancellationToken, continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAll<TResult>(Task[] tasks, Func<Task[], TResult> continuationFunction, CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(continuationFunction);
        var starter = TaskFactory.PrepareAll(tasks, cancellationToken, continuationOptions, scheduler);
        return starter.ContinueWith(completed => continuationFunction(completed.Result), cancellationToken, continuationOptions, scheduler);
    }

    public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction) =>
        ContinueWhenAll<TAntecedentResult, TResult>(tasks, continuationFunction, _cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, CancellationToken cancellationToken) =>
        ContinueWhenAll<TAntecedentResult, TResult>(tasks, continuationFunction, cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
        ContinueWhenAll<TAntecedentResult, TResult>(tasks, continuationFunction, _cancellationToken, continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAll<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(continuationFunction);
        var starter = TaskFactory.PrepareAll(tasks, cancellationToken, continuationOptions, scheduler);
        return starter.ContinueWith(completed => continuationFunction(completed.Result), cancellationToken, continuationOptions, scheduler);
    }

    public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction) =>
        ContinueWhenAny(tasks, continuationAction, _cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction, CancellationToken cancellationToken) =>
        ContinueWhenAny(tasks, continuationAction, cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction, TaskContinuationOptions continuationOptions) =>
        ContinueWhenAny(tasks, continuationAction, _cancellationToken, continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task ContinueWhenAny(Task[] tasks, Action<Task> continuationAction, CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(continuationAction);
        var starter = TaskFactory.PrepareAny(tasks, continuationOptions, scheduler);
        return starter.ContinueWith(completed => continuationAction(completed.Result), cancellationToken, continuationOptions, scheduler);
    }

    public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction) =>
        ContinueWhenAny<TAntecedentResult>(tasks, continuationAction, _cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction, CancellationToken cancellationToken) =>
        ContinueWhenAny<TAntecedentResult>(tasks, continuationAction, cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction, TaskContinuationOptions continuationOptions) =>
        ContinueWhenAny<TAntecedentResult>(tasks, continuationAction, _cancellationToken, continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Action<Task<TAntecedentResult>> continuationAction, CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(continuationAction);
        var starter = TaskFactory.PrepareAny(tasks, continuationOptions, scheduler);
        return starter.ContinueWith(completed => continuationAction(completed.Result), cancellationToken, continuationOptions, scheduler);
    }

    public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction) =>
        ContinueWhenAny<TResult>(tasks, continuationFunction, _cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction, CancellationToken cancellationToken) =>
        ContinueWhenAny<TResult>(tasks, continuationFunction, cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
        ContinueWhenAny<TResult>(tasks, continuationFunction, _cancellationToken, continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAny<TResult>(Task[] tasks, Func<Task, TResult> continuationFunction, CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(continuationFunction);
        var starter = TaskFactory.PrepareAny(tasks, continuationOptions, scheduler);
        return starter.ContinueWith(completed => continuationFunction(completed.Result), cancellationToken, continuationOptions, scheduler);
    }

    public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction) =>
        ContinueWhenAny<TAntecedentResult, TResult>(tasks, continuationFunction, _cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, CancellationToken cancellationToken) =>
        ContinueWhenAny<TAntecedentResult, TResult>(tasks, continuationFunction, cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
        ContinueWhenAny<TAntecedentResult, TResult>(tasks, continuationFunction, _cancellationToken, continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAny<TAntecedentResult, TResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(continuationFunction);
        var starter = TaskFactory.PrepareAny(tasks, continuationOptions, scheduler);
        return starter.ContinueWith(completed => continuationFunction(completed.Result), cancellationToken, continuationOptions, scheduler);
    }

    public Task FromAsync(Func<AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, object? state) =>
        FromAsync(beginMethod, endMethod, state, _creationOptions);

    public Task FromAsync(Func<AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, object? state, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(beginMethod);
        ArgumentNullException.ThrowIfNull(endMethod);
        return TaskFactory.FromAsyncBegin<object?>(beginMethod, null, endMethod, state, creationOptions);
    }

    public Task FromAsync<TArg1>(Func<TArg1, AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, object? state) =>
        FromAsync<TArg1>(beginMethod, endMethod, arg1, state, _creationOptions);

    public Task FromAsync<TArg1>(Func<TArg1, AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, object? state, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(beginMethod);
        ArgumentNullException.ThrowIfNull(endMethod);
        return TaskFactory.FromAsyncBegin<object?>((callback, asyncState) => beginMethod(arg1, callback, asyncState), null, endMethod, state, creationOptions);
    }

    public Task FromAsync<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, object? state) =>
        FromAsync<TArg1, TArg2>(beginMethod, endMethod, arg1, arg2, state, _creationOptions);

    public Task FromAsync<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, object? state, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(beginMethod);
        ArgumentNullException.ThrowIfNull(endMethod);
        return TaskFactory.FromAsyncBegin<object?>((callback, asyncState) => beginMethod(arg1, arg2, callback, asyncState), null, endMethod, state, creationOptions);
    }

    public Task FromAsync<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object? state) =>
        FromAsync<TArg1, TArg2, TArg3>(beginMethod, endMethod, arg1, arg2, arg3, state, _creationOptions);

    public Task FromAsync<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult> beginMethod, Action<IAsyncResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object? state, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(beginMethod);
        ArgumentNullException.ThrowIfNull(endMethod);
        return TaskFactory.FromAsyncBegin<object?>((callback, asyncState) => beginMethod(arg1, arg2, arg3, callback, asyncState), null, endMethod, state, creationOptions);
    }

    public Task<TResult> FromAsync<TResult>(Func<AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, object? state) =>
        FromAsync<TResult>(beginMethod, endMethod, state, _creationOptions);

    public Task<TResult> FromAsync<TResult>(Func<AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, object? state, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(beginMethod);
        ArgumentNullException.ThrowIfNull(endMethod);
        return TaskFactory.FromAsyncBegin<TResult>(beginMethod, endMethod, null, state, creationOptions);
    }

    public Task<TResult> FromAsync<TArg1, TResult>(Func<TArg1, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, object? state) =>
        FromAsync<TArg1, TResult>(beginMethod, endMethod, arg1, state, _creationOptions);

    public Task<TResult> FromAsync<TArg1, TResult>(Func<TArg1, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, object? state, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(beginMethod);
        ArgumentNullException.ThrowIfNull(endMethod);
        return TaskFactory.FromAsyncBegin<TResult>((callback, asyncState) => beginMethod(arg1, callback, asyncState), endMethod, null, state, creationOptions);
    }

    public Task<TResult> FromAsync<TArg1, TArg2, TResult>(Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, object? state) =>
        FromAsync<TArg1, TArg2, TResult>(beginMethod, endMethod, arg1, arg2, state, _creationOptions);

    public Task<TResult> FromAsync<TArg1, TArg2, TResult>(Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, object? state, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(beginMethod);
        ArgumentNullException.ThrowIfNull(endMethod);
        return TaskFactory.FromAsyncBegin<TResult>((callback, asyncState) => beginMethod(arg1, arg2, callback, asyncState), endMethod, null, state, creationOptions);
    }

    public Task<TResult> FromAsync<TArg1, TArg2, TArg3, TResult>(Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object? state) =>
        FromAsync<TArg1, TArg2, TArg3, TResult>(beginMethod, endMethod, arg1, arg2, arg3, state, _creationOptions);

    public Task<TResult> FromAsync<TArg1, TArg2, TArg3, TResult>(Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object? state, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(beginMethod);
        ArgumentNullException.ThrowIfNull(endMethod);
        return TaskFactory.FromAsyncBegin<TResult>((callback, asyncState) => beginMethod(arg1, arg2, arg3, callback, asyncState), endMethod, null, state, creationOptions);
    }

    public Task FromAsync(IAsyncResult asyncResult, Action<IAsyncResult> endMethod) =>
        FromAsync(asyncResult, endMethod, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task FromAsync(IAsyncResult asyncResult, Action<IAsyncResult> endMethod, TaskCreationOptions creationOptions) =>
        FromAsync(asyncResult, endMethod, creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task FromAsync(IAsyncResult asyncResult, Action<IAsyncResult> endMethod, TaskCreationOptions creationOptions, TaskScheduler scheduler) =>
        TaskFactory.FromAsyncResult<object?>(asyncResult, null, endMethod, creationOptions, scheduler);

    public Task<TResult> FromAsync<TResult>(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod) =>
        FromAsync<TResult>(asyncResult, endMethod, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> FromAsync<TResult>(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod, TaskCreationOptions creationOptions) =>
        FromAsync<TResult>(asyncResult, endMethod, creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> FromAsync<TResult>(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod, TaskCreationOptions creationOptions, TaskScheduler scheduler) =>
        TaskFactory.FromAsyncResult<TResult>(asyncResult, endMethod, null, creationOptions, scheduler);

}
