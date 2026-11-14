// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from System.Private.CoreLib TaskFactory configuration contracts.

namespace System.Threading.Tasks;

public partial class TaskFactory<TResult>
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
    public Task<TResult> StartNew(Func<TResult> function) =>
        StartNew(function, _cancellationToken, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> StartNew(Func<TResult> function, CancellationToken cancellationToken) =>
        StartNew(function, cancellationToken, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> StartNew(Func<TResult> function, TaskCreationOptions creationOptions) =>
        StartNew(function, _cancellationToken, creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> StartNew(Func<TResult> function, CancellationToken cancellationToken,
        TaskCreationOptions creationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(function);
        var task = new Task<TResult>((Delegate)function, null, cancellationToken, creationOptions, true);
        task.StartFromFactory(scheduler);
        return task;
    }

    public Task<TResult> StartNew(Func<object?, TResult> function, object? state) =>
        StartNew(function, state, _cancellationToken, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> StartNew(Func<object?, TResult> function, object? state, CancellationToken cancellationToken) =>
        StartNew(function, state, cancellationToken, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> StartNew(Func<object?, TResult> function, object? state, TaskCreationOptions creationOptions) =>
        StartNew(function, state, _cancellationToken, creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> StartNew(Func<object?, TResult> function, object? state, CancellationToken cancellationToken,
        TaskCreationOptions creationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(function);
        var task = new Task<TResult>((Delegate)function, state, cancellationToken, creationOptions, true);
        task.StartFromFactory(scheduler);
        return task;
    }

    public Task<TResult> ContinueWhenAll(Task[] tasks, Func<Task[], TResult> continuationFunction) =>
        ContinueWhenAll(tasks, continuationFunction, _cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAll(Task[] tasks, Func<Task[], TResult> continuationFunction, CancellationToken cancellationToken) =>
        ContinueWhenAll(tasks, continuationFunction, cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAll(Task[] tasks, Func<Task[], TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
        ContinueWhenAll(tasks, continuationFunction, _cancellationToken, continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAll(Task[] tasks, Func<Task[], TResult> continuationFunction, CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(continuationFunction);
        var starter = TaskFactory.PrepareAll(tasks, cancellationToken, continuationOptions, scheduler);
        return starter.ContinueWith(completed => continuationFunction(completed.Result), cancellationToken, continuationOptions, scheduler);
    }

    public Task<TResult> ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction) =>
        ContinueWhenAll<TAntecedentResult>(tasks, continuationFunction, _cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, CancellationToken cancellationToken) =>
        ContinueWhenAll<TAntecedentResult>(tasks, continuationFunction, cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
        ContinueWhenAll<TAntecedentResult>(tasks, continuationFunction, _cancellationToken, continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>[], TResult> continuationFunction, CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(continuationFunction);
        var starter = TaskFactory.PrepareAll(tasks, cancellationToken, continuationOptions, scheduler);
        return starter.ContinueWith(completed => continuationFunction(completed.Result), cancellationToken, continuationOptions, scheduler);
    }

    public Task<TResult> ContinueWhenAny(Task[] tasks, Func<Task, TResult> continuationFunction) =>
        ContinueWhenAny(tasks, continuationFunction, _cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAny(Task[] tasks, Func<Task, TResult> continuationFunction, CancellationToken cancellationToken) =>
        ContinueWhenAny(tasks, continuationFunction, cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAny(Task[] tasks, Func<Task, TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
        ContinueWhenAny(tasks, continuationFunction, _cancellationToken, continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAny(Task[] tasks, Func<Task, TResult> continuationFunction, CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(continuationFunction);
        var starter = TaskFactory.PrepareAny(tasks, continuationOptions, scheduler);
        return starter.ContinueWith(completed => continuationFunction(completed.Result), cancellationToken, continuationOptions, scheduler);
    }

    public Task<TResult> ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction) =>
        ContinueWhenAny<TAntecedentResult>(tasks, continuationFunction, _cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, CancellationToken cancellationToken) =>
        ContinueWhenAny<TAntecedentResult>(tasks, continuationFunction, cancellationToken, _continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, TaskContinuationOptions continuationOptions) =>
        ContinueWhenAny<TAntecedentResult>(tasks, continuationFunction, _cancellationToken, continuationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> ContinueWhenAny<TAntecedentResult>(Task<TAntecedentResult>[] tasks, Func<Task<TAntecedentResult>, TResult> continuationFunction, CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(continuationFunction);
        var starter = TaskFactory.PrepareAny(tasks, continuationOptions, scheduler);
        return starter.ContinueWith(completed => continuationFunction(completed.Result), cancellationToken, continuationOptions, scheduler);
    }

    public Task<TResult> FromAsync(Func<AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, object? state) =>
        FromAsync(beginMethod, endMethod, state, _creationOptions);

    public Task<TResult> FromAsync(Func<AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, object? state, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(beginMethod);
        ArgumentNullException.ThrowIfNull(endMethod);
        return TaskFactory.FromAsyncBegin<TResult>(beginMethod, endMethod, null, state, creationOptions);
    }

    public Task<TResult> FromAsync<TArg1>(Func<TArg1, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, object? state) =>
        FromAsync<TArg1>(beginMethod, endMethod, arg1, state, _creationOptions);

    public Task<TResult> FromAsync<TArg1>(Func<TArg1, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, object? state, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(beginMethod);
        ArgumentNullException.ThrowIfNull(endMethod);
        return TaskFactory.FromAsyncBegin<TResult>((callback, asyncState) => beginMethod(arg1, callback, asyncState), endMethod, null, state, creationOptions);
    }

    public Task<TResult> FromAsync<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, object? state) =>
        FromAsync<TArg1, TArg2>(beginMethod, endMethod, arg1, arg2, state, _creationOptions);

    public Task<TResult> FromAsync<TArg1, TArg2>(Func<TArg1, TArg2, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, object? state, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(beginMethod);
        ArgumentNullException.ThrowIfNull(endMethod);
        return TaskFactory.FromAsyncBegin<TResult>((callback, asyncState) => beginMethod(arg1, arg2, callback, asyncState), endMethod, null, state, creationOptions);
    }

    public Task<TResult> FromAsync<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object? state) =>
        FromAsync<TArg1, TArg2, TArg3>(beginMethod, endMethod, arg1, arg2, arg3, state, _creationOptions);

    public Task<TResult> FromAsync<TArg1, TArg2, TArg3>(Func<TArg1, TArg2, TArg3, AsyncCallback, object?, IAsyncResult> beginMethod, Func<IAsyncResult, TResult> endMethod, TArg1 arg1, TArg2 arg2, TArg3 arg3, object? state, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(beginMethod);
        ArgumentNullException.ThrowIfNull(endMethod);
        return TaskFactory.FromAsyncBegin<TResult>((callback, asyncState) => beginMethod(arg1, arg2, arg3, callback, asyncState), endMethod, null, state, creationOptions);
    }

    public Task<TResult> FromAsync(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod) =>
        FromAsync(asyncResult, endMethod, _creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> FromAsync(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod, TaskCreationOptions creationOptions) =>
        FromAsync(asyncResult, endMethod, creationOptions, _scheduler ?? TaskScheduler.Current);

    public Task<TResult> FromAsync(IAsyncResult asyncResult, Func<IAsyncResult, TResult> endMethod, TaskCreationOptions creationOptions, TaskScheduler scheduler) =>
        TaskFactory.FromAsyncResult<TResult>(asyncResult, endMethod, null, creationOptions, scheduler);

    // Ported from dotnet/runtime deae2e32 (TaskFactory_T.cs's FromAsyncTrim), as Stream uses it to wrap a derived
    // stream's Begin/End methods. NetWasm adaptations: the promise completes through Task<TResult>.TrySetResult (on one
    // thread, upstream's unsynchronized DangerousSetResult has no advantage) and TrySetCanceled(Exception).
    internal static Task<TResult> FromAsyncTrim<TInstance, TArgs>(
        TInstance thisRef, TArgs args,
        Func<TInstance, TArgs, AsyncCallback, object, IAsyncResult> beginMethod,
        Func<TInstance, IAsyncResult, TResult> endMethod)
        where TInstance : class
    {
        System.Diagnostics.Debug.Assert(thisRef != null, "Expected a non-null thisRef");
        System.Diagnostics.Debug.Assert(beginMethod != null, "Expected a non-null beginMethod");
        System.Diagnostics.Debug.Assert(endMethod != null, "Expected a non-null endMethod");

        var promise = new FromAsyncTrimPromise<TInstance>(thisRef, endMethod);
        IAsyncResult asyncResult = beginMethod(thisRef, args, FromAsyncTrimPromise<TInstance>.s_completeFromAsyncResult, promise);

        // If the IAsyncResult completed asynchronously, completing the promise will be handled by the callback.
        // If it completed synchronously, we'll handle that here.
        if (asyncResult.CompletedSynchronously)
        {
            System.Diagnostics.Debug.Assert(asyncResult.IsCompleted, "If the operation completed synchronously, it must be completed.");
            promise.Complete(thisRef, endMethod, asyncResult);
        }

        return promise;
    }

    private sealed class FromAsyncTrimPromise<TInstance> : Task<TResult> where TInstance : class
    {
        internal static readonly AsyncCallback s_completeFromAsyncResult = CompleteFromAsyncResult;

        private TInstance? m_thisRef;
        private Func<TInstance, IAsyncResult, TResult>? m_endMethod;

        internal FromAsyncTrimPromise(TInstance thisRef, Func<TInstance, IAsyncResult, TResult> endMethod)
        {
            m_thisRef = thisRef;
            m_endMethod = endMethod;
        }

        internal static void CompleteFromAsyncResult(IAsyncResult asyncResult)
        {
            if (asyncResult == null) ThrowHelper.ThrowArgumentNullException(ExceptionArgument.asyncResult);

            var promise = asyncResult.AsyncState as FromAsyncTrimPromise<TInstance>;
            if (promise == null) ThrowHelper.ThrowArgumentException(ExceptionResource.InvalidOperation_WrongAsyncResultOrEndCalledMultiple, ExceptionArgument.asyncResult);

            // Grab the relevant state and then null it out so that the task doesn't hold onto the state unnecessarily
            TInstance? thisRef = promise.m_thisRef;
            Func<TInstance, IAsyncResult, TResult>? endMethod = promise.m_endMethod;
            promise.m_thisRef = default;
            promise.m_endMethod = null;
            if (endMethod == null) ThrowHelper.ThrowArgumentException(ExceptionResource.InvalidOperation_WrongAsyncResultOrEndCalledMultiple, ExceptionArgument.asyncResult);

            // Complete the promise.  If the IAsyncResult completed synchronously,
            // we'll instead complete the promise at the call site.
            if (!asyncResult.CompletedSynchronously)
            {
                promise.Complete(thisRef!, endMethod, asyncResult);
            }
        }

        internal void Complete(TInstance thisRef, Func<TInstance, IAsyncResult, TResult> endMethod, IAsyncResult asyncResult)
        {
            bool successfullySet;
            try
            {
                successfullySet = TrySetResult(endMethod(thisRef, asyncResult));
            }
            catch (OperationCanceledException oce)
            {
                successfullySet = TrySetCanceled(oce);
            }
            catch (Exception exc)
            {
                successfullySet = TrySetException(exc);
            }

            System.Diagnostics.Debug.Assert(successfullySet, "Expected the task to not yet be completed");
        }
    }
}
