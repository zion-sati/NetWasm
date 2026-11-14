// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// One thread, as upstream's ThreadPool.Wasi.cs: work items run from the reactor's queue; registered waits and
// native overlapped I/O throw, as upstream's do.
using System.Runtime.InteropServices;
namespace System.Threading;

public static class ThreadPool
{
    // As upstream's single-threaded WASI ThreadPool (ThreadPool.Wasi.cs): one thread, and work items run in order
    // from the reactor's queue once the current work returns. The safe overloads flow the ExecutionContext.
    private static long s_pendingWorkItemCount;

    public static int ThreadCount => 1;
    public static long CompletedWorkItemCount => 0;
    public static long PendingWorkItemCount => s_pendingWorkItemCount;

    public static bool QueueUserWorkItem(WaitCallback callBack) => QueueUserWorkItem(callBack, null);
    public static bool QueueUserWorkItem(WaitCallback callBack, object? state)
    {
        ArgumentNullException.ThrowIfNull(callBack);
        Queue(() => callBack(state), ExecutionContext.Capture());
        return true;
    }
    public static bool QueueUserWorkItem<TState>(Action<TState> callBack, TState state, bool preferLocal)
    {
        ArgumentNullException.ThrowIfNull(callBack);
        Queue(() => callBack(state), ExecutionContext.Capture());
        return true;
    }
    public static bool UnsafeQueueUserWorkItem(WaitCallback callBack, object? state)
    {
        ArgumentNullException.ThrowIfNull(callBack);
        Queue(() => callBack(state), null);
        return true;
    }
    public static bool UnsafeQueueUserWorkItem<TState>(Action<TState> callBack, TState state, bool preferLocal)
    {
        ArgumentNullException.ThrowIfNull(callBack);
        Queue(() => callBack(state), null);
        return true;
    }
    public static bool UnsafeQueueUserWorkItem(IThreadPoolWorkItem callBack, bool preferLocal)
    {
        ArgumentNullException.ThrowIfNull(callBack);
        // As upstream: a Task is queued through its scheduler, never as a work item.
        if (callBack is System.Threading.Tasks.Task)
        {
            throw new ArgumentOutOfRangeException(nameof(callBack));
        }
        Queue(callBack.Execute, null);
        return true;
    }

    private static void Queue(Action work, ExecutionContext? context)
    {
        s_pendingWorkItemCount++;
        PlatformServices.Scheduler.Schedule(() =>
        {
            s_pendingWorkItemCount--;
            if (context is null)
            {
                work();
            }
            else
            {
                ExecutionContext.Run(context, static state => ((Action)state!)(), work);
            }
        }, 0);
    }

    public static bool BindHandle(IntPtr osHandle) => throw Unsupported();
    public static bool BindHandle(SafeHandle osHandle) => throw Unsupported();
    [CLSCompliant(false)]
    public static unsafe bool UnsafeQueueNativeOverlapped(NativeOverlapped* overlapped)
    {
        if (overlapped == null) throw new ArgumentNullException(nameof(overlapped));
        throw Unsupported();
    }
    public static bool SetMinThreads(int workerThreads, int completionPortThreads) =>
        workerThreads == 1 && completionPortThreads == 1;
    public static bool SetMaxThreads(int workerThreads, int completionPortThreads) =>
        workerThreads == 1 && completionPortThreads == 1;
    public static void GetMinThreads(out int workerThreads, out int completionPortThreads) =>
        (workerThreads, completionPortThreads) = (1, 1);
    public static void GetMaxThreads(out int workerThreads, out int completionPortThreads) =>
        (workerThreads, completionPortThreads) = (1, 1);
    public static void GetAvailableThreads(out int workerThreads, out int completionPortThreads) =>
        (workerThreads, completionPortThreads) = (1, 1);
    private static RegisteredWaitHandle RegisterWaitForSingleObject(WaitHandle? waitObject, WaitOrTimerCallback? callBack, object? state, uint millisecondsTimeOutInterval, bool executeOnlyOnce, bool flowExecutionContext) => throw Unsupported();
    private static PlatformNotSupportedException Unsupported() => new("ThreadPool wait registration and native overlapped I/O are not supported on this NetWasm platform.");
    [CLSCompliant(false)]
    public static RegisteredWaitHandle RegisterWaitForSingleObject(
         WaitHandle waitObject,
         WaitOrTimerCallback callBack,
         object? state,
         uint millisecondsTimeOutInterval,
         bool executeOnlyOnce    // NOTE: we do not allow other options that allow the callback to be queued as an APC
         )
    {
        if (millisecondsTimeOutInterval > (uint)int.MaxValue && millisecondsTimeOutInterval != uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(millisecondsTimeOutInterval), "Argument must be less than or equal to 2^31 - 1 milliseconds.");


        return RegisterWaitForSingleObject(waitObject, callBack, state, millisecondsTimeOutInterval, executeOnlyOnce, true);
    }

    [CLSCompliant(false)]
    public static RegisteredWaitHandle UnsafeRegisterWaitForSingleObject(
         WaitHandle waitObject,
         WaitOrTimerCallback callBack,
         object? state,
         uint millisecondsTimeOutInterval,
         bool executeOnlyOnce    // NOTE: we do not allow other options that allow the callback to be queued as an APC
         )
    {
        if (millisecondsTimeOutInterval > (uint)int.MaxValue && millisecondsTimeOutInterval != uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(millisecondsTimeOutInterval), "Number must be either non-negative and less than or equal to Int32.MaxValue or -1.");


        return RegisterWaitForSingleObject(waitObject, callBack, state, millisecondsTimeOutInterval, executeOnlyOnce, false);
    }

    public static RegisteredWaitHandle RegisterWaitForSingleObject(
         WaitHandle waitObject,
         WaitOrTimerCallback callBack,
         object? state,
         int millisecondsTimeOutInterval,
         bool executeOnlyOnce    // NOTE: we do not allow other options that allow the callback to be queued as an APC
         )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeOutInterval, -1);
        return RegisterWaitForSingleObject(waitObject, callBack, state, (uint)millisecondsTimeOutInterval, executeOnlyOnce, true);
    }

    public static RegisteredWaitHandle UnsafeRegisterWaitForSingleObject(
         WaitHandle waitObject,
         WaitOrTimerCallback callBack,
         object? state,
         int millisecondsTimeOutInterval,
         bool executeOnlyOnce    // NOTE: we do not allow other options that allow the callback to be queued as an APC
         )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeOutInterval, -1);


        return RegisterWaitForSingleObject(waitObject, callBack, state, (uint)millisecondsTimeOutInterval, executeOnlyOnce, false);
    }

    public static RegisteredWaitHandle RegisterWaitForSingleObject(
        WaitHandle waitObject,
        WaitOrTimerCallback callBack,
        object? state,
        long millisecondsTimeOutInterval,
        bool executeOnlyOnce    // NOTE: we do not allow other options that allow the callback to be queued as an APC
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeOutInterval, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(millisecondsTimeOutInterval, int.MaxValue);


        return RegisterWaitForSingleObject(waitObject, callBack, state, (uint)millisecondsTimeOutInterval, executeOnlyOnce, true);
    }

    public static RegisteredWaitHandle UnsafeRegisterWaitForSingleObject(
        WaitHandle waitObject,
        WaitOrTimerCallback callBack,
        object? state,
        long millisecondsTimeOutInterval,
        bool executeOnlyOnce    // NOTE: we do not allow other options that allow the callback to be queued as an APC
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeOutInterval, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(millisecondsTimeOutInterval, int.MaxValue);


        return RegisterWaitForSingleObject(waitObject, callBack, state, (uint)millisecondsTimeOutInterval, executeOnlyOnce, false);
    }

    public static RegisteredWaitHandle RegisterWaitForSingleObject(
                      WaitHandle waitObject,
                      WaitOrTimerCallback callBack,
                      object? state,
                      TimeSpan timeout,
                      bool executeOnlyOnce
                      )
    {
        long tm = (long)timeout.TotalMilliseconds;

        ArgumentOutOfRangeException.ThrowIfLessThan(tm, -1, nameof(timeout));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tm, int.MaxValue, nameof(timeout));


        return RegisterWaitForSingleObject(waitObject, callBack, state, (uint)tm, executeOnlyOnce, true);
    }

    public static RegisteredWaitHandle UnsafeRegisterWaitForSingleObject(
                      WaitHandle waitObject,
                      WaitOrTimerCallback callBack,
                      object? state,
                      TimeSpan timeout,
                      bool executeOnlyOnce
                      )
    {
        long tm = (long)timeout.TotalMilliseconds;

        ArgumentOutOfRangeException.ThrowIfLessThan(tm, -1, nameof(timeout));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tm, int.MaxValue, nameof(timeout));


        return RegisterWaitForSingleObject(waitObject, callBack, state, (uint)tm, executeOnlyOnce, false);
    }

}
