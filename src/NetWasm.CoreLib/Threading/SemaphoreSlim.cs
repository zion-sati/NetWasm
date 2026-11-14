// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Derived from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Single-reactor adaptation preserving async waiters and immediate acquisition.
// A blocking wait throws, as on .NET 11's single-threaded runtimes; the wait handle is a ManualResetEvent.

using System.Diagnostics;
using System.Threading.Tasks;

namespace System.Threading;

public class SemaphoreSlim : IDisposable
{
    private int m_currentCount;
    private readonly int m_maxCount;
    private bool m_isDisposed;
    private ManualResetEvent? m_waitHandle;
    private TaskNode? m_asyncHead;
    private TaskNode? m_asyncTail;

    private sealed class TaskNode : Task<bool>
    {
        internal TaskNode? Prev, Next;
        internal TaskNode() : base((object?)null, TaskCreationOptions.RunContinuationsAsynchronously) { }
    }

    public SemaphoreSlim(int initialCount) : this(initialCount, int.MaxValue) { }

    public SemaphoreSlim(int initialCount, int maxCount)
    {
        if (initialCount < 0 || initialCount > maxCount) throw new ArgumentOutOfRangeException(nameof(initialCount));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        m_currentCount = initialCount;
        m_maxCount = maxCount;
    }

    public int CurrentCount => m_currentCount;
    public WaitHandle AvailableWaitHandle
    {
        get
        {
            CheckDispose();
            // As upstream: the event is created on first use and is set while a permit is available.
            return m_waitHandle ??= new ManualResetEvent(m_currentCount != 0);
        }
    }

    public void Wait() => Wait(Timeout.Infinite, default);
    public void Wait(CancellationToken cancellationToken) => Wait(Timeout.Infinite, cancellationToken);
    public bool Wait(int millisecondsTimeout) => Wait(millisecondsTimeout, default);
    public bool Wait(int millisecondsTimeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
        return WaitCore(millisecondsTimeout, cancellationToken);
    }
    public bool Wait(TimeSpan timeout) => Wait(timeout, default);
    public bool Wait(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var milliseconds = (long)timeout.TotalMilliseconds;
        if (milliseconds < -1) throw new ArgumentOutOfRangeException(nameof(timeout));
        return WaitCore(milliseconds, cancellationToken);
    }
    private bool WaitCore(long millisecondsTimeout, CancellationToken cancellationToken)
    {
        CheckDispose();
        cancellationToken.ThrowIfCancellationRequested();
        if (m_currentCount > 0)
        {
            m_currentCount--;
            if (m_currentCount == 0) m_waitHandle?.Reset();
            return true;
        }
        if (millisecondsTimeout == 0) return false;
        throw new PlatformNotSupportedException("SemaphoreSlim.Wait requires host blocking while no permit is available.");
    }

    public Task WaitAsync() => WaitAsyncCore(Timeout.Infinite, default);
    public Task WaitAsync(CancellationToken cancellationToken) => WaitAsyncCore(Timeout.Infinite, cancellationToken);
    public Task<bool> WaitAsync(int millisecondsTimeout) => WaitAsync(millisecondsTimeout, default);
    public Task<bool> WaitAsync(int millisecondsTimeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
        return WaitAsyncCore(millisecondsTimeout, cancellationToken);
    }
    public Task<bool> WaitAsync(TimeSpan timeout) => WaitAsync(timeout, default);
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var milliseconds = (long)timeout.TotalMilliseconds;
        if (milliseconds < -1) throw new ArgumentOutOfRangeException(nameof(timeout));
        return WaitAsyncCore(milliseconds, cancellationToken);
    }

    private Task<bool> WaitAsyncCore(long millisecondsTimeout, CancellationToken cancellationToken)
    {
        CheckDispose();
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<bool>(cancellationToken);
        if (m_currentCount > 0)
        {
            --m_currentCount;
            if (m_currentCount == 0) m_waitHandle?.Reset();
            return Task.FromResult(true);
        }
        if (millisecondsTimeout == 0) return Task.FromResult(false);
        var asyncWaiter = CreateAndAddAsyncWaiter();
        return millisecondsTimeout == Timeout.Infinite && !cancellationToken.CanBeCanceled
            ? asyncWaiter
            : WaitUntilCountOrTimeoutAsync(asyncWaiter, millisecondsTimeout, cancellationToken);
    }

    private TaskNode CreateAndAddAsyncWaiter()
    {
        var task = new TaskNode();
        if (m_asyncHead is null)
        {
            Debug.Assert(m_asyncTail is null);
            m_asyncHead = task;
            m_asyncTail = task;
        }
        else
        {
            Debug.Assert(m_asyncTail is not null);
            m_asyncTail.Next = task;
            task.Prev = m_asyncTail;
            m_asyncTail = task;
        }
        return task;
    }

    private bool RemoveAsyncWaiter(TaskNode task)
    {
        var wasInList = m_asyncHead == task || task.Prev is not null;
        task.Next?.Prev = task.Prev;
        task.Prev?.Next = task.Next;
        if (m_asyncHead == task) m_asyncHead = task.Next;
        if (m_asyncTail == task) m_asyncTail = task.Prev;
        Debug.Assert((m_asyncHead is null) == (m_asyncTail is null));
        task.Next = task.Prev = null;
        return wasInList;
    }

    private async Task<bool> WaitUntilCountOrTimeoutAsync(TaskNode asyncWaiter, long millisecondsTimeout, CancellationToken cancellationToken)
    {
        Task wait;
        try
        {
            wait = asyncWaiter.WaitAsync(TimeSpan.FromMilliseconds(millisecondsTimeout), cancellationToken);
        }
        catch
        {
            // Timer validation/setup can fail before an awaitable is returned.
            // Do not leave an abandoned waiter consuming a later release.
            RemoveAsyncWaiter(asyncWaiter);
            throw;
        }
        await wait.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (cancellationToken.IsCancellationRequested)
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        if (asyncWaiter.IsCompleted)
            return true;

        if (RemoveAsyncWaiter(asyncWaiter))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }

        return await asyncWaiter.ConfigureAwait(false);
    }

    public int Release() => Release(1);

    public int Release(int releaseCount)
    {
        CheckDispose();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(releaseCount);
        var currentCount = m_currentCount;
        if (releaseCount > m_maxCount - currentCount) throw new SemaphoreFullException();
        var returnCount = currentCount;
        currentCount += releaseCount;
        while (currentCount > 0 && m_asyncHead is not null)
        {
            --currentCount;
            var waiterTask = m_asyncHead;
            RemoveAsyncWaiter(waiterTask);
            waiterTask.TrySetResult(true);
        }
        m_currentCount = currentCount;
        if (returnCount == 0 && currentCount > 0) m_waitHandle?.Set();
        return returnCount;
    }

    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            m_isDisposed = true;
            m_asyncHead = m_asyncTail = null;
            m_waitHandle?.Dispose();
            m_waitHandle = null;
        }
    }
    private void CheckDispose() => ObjectDisposedException.ThrowIf(m_isDisposed, this);
}
