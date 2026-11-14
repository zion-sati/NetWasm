// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from System.Private.CoreLib TaskFactory APM callback adapters.

namespace System.Threading.Tasks;

public partial class TaskFactory
{
    internal static void CheckFromAsyncOptions(TaskCreationOptions creationOptions, bool hasBeginMethod)
    {
        if (hasBeginMethod && (creationOptions & (TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness)) != 0)
            throw new ArgumentOutOfRangeException(nameof(creationOptions));
        if ((creationOptions & ~(TaskCreationOptions.AttachedToParent | TaskCreationOptions.DenyChildAttach |
            TaskCreationOptions.HideScheduler | TaskCreationOptions.PreferFairness | TaskCreationOptions.LongRunning)) != 0)
            throw new ArgumentOutOfRangeException(nameof(creationOptions));
    }

    internal static Task<TResult> FromAsyncBegin<TResult>(Func<AsyncCallback, object?, IAsyncResult> beginMethod,
        Func<IAsyncResult, TResult>? endFunction, Action<IAsyncResult>? endAction, object? state,
        TaskCreationOptions creationOptions)
    {
        CheckFromAsyncOptions(creationOptions, true);
        // Upstream then uses its promise-task constructor, whose option mask is
        // narrower than CheckFromAsyncOptions. Our shared Task constructor is not.
        if ((creationOptions & ~TaskCreationOptions.AttachedToParent) != 0)
            throw new ArgumentOutOfRangeException(nameof(creationOptions));
        var promise = new Task<TResult>(state, creationOptions);
        try
        {
            var result = beginMethod(result =>
            {
                if (!result.CompletedSynchronously) CompleteFromAsync(result, endFunction, endAction, promise);
            }, state);
            if (result.CompletedSynchronously) CompleteFromAsync(result, endFunction, endAction, promise);
        }
        catch
        {
            promise.TrySetResult(default!);
            throw;
        }
        return promise;
    }

    internal static Task<TResult> FromAsyncResult<TResult>(IAsyncResult asyncResult,
        Func<IAsyncResult, TResult>? endFunction, Action<IAsyncResult>? endAction,
        TaskCreationOptions creationOptions, TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(asyncResult);
        if (endFunction is null && endAction is null) throw new ArgumentNullException("endMethod");
        ArgumentNullException.ThrowIfNull(scheduler);
        CheckFromAsyncOptions(creationOptions, false);
        // Preserve the subsequent upstream promise-task constructor validation.
        if ((creationOptions & ~TaskCreationOptions.AttachedToParent) != 0)
            throw new ArgumentOutOfRangeException(nameof(creationOptions));
        var promise = new Task<TResult>((object?)null, creationOptions);
        if (!asyncResult.IsCompleted)
            throw new PlatformNotSupportedException("TaskFactory.FromAsync for an incomplete IAsyncResult requires host wait registration, which is not available on this NetWasm platform. Use a begin/end callback overload instead.");
        var callback = new Task(() => CompleteFromAsync(asyncResult, endFunction, endAction, promise));
        try { callback.StartFromFactory(scheduler, tryInline: true); }
        catch (Exception exception) { promise.TrySetException(exception); }
        return promise;
    }

    private static void CompleteFromAsync<TResult>(IAsyncResult result, Func<IAsyncResult, TResult>? endFunction,
        Action<IAsyncResult>? endAction, Task<TResult> promise)
    {
        try
        {
            var value = default(TResult)!;
            if (endFunction is not null) value = endFunction(result);
            else endAction!(result);
            promise.TrySetResult(value);
        }
        catch (OperationCanceledException exception) { promise.TrySetCanceled(exception); }
        catch (Exception exception) { promise.TrySetException(exception); }
    }
}
