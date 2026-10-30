// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Derived from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.
// Internal async-only stream dependency. The single reactor needs no blocking
// waits, monitor coordination, wait handles, timeout surface or public semaphore.

using System.Diagnostics;
using System.Threading.Tasks;

namespace System.Threading;

internal sealed class SemaphoreSlim
{
    private int m_currentCount;
    private readonly int m_maxCount;
    private TaskNode? m_asyncHead;
    private TaskNode? m_asyncTail;

    private sealed class TaskNode : Task<bool>
    {
        internal TaskNode? Prev, Next;
        internal TaskNode() : base((object?)null, TaskCreationOptions.RunContinuationsAsynchronously) { }
    }

    internal SemaphoreSlim(int initialCount, int maxCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        if (initialCount > maxCount)
            throw new ArgumentOutOfRangeException(nameof(initialCount));
        m_currentCount = initialCount;
        m_maxCount = maxCount;
    }

    internal Task WaitAsync() => WaitAsync(default);

    internal Task WaitAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<bool>(cancellationToken);

        if (m_currentCount > 0)
        {
            --m_currentCount;
            return Task.FromResult(true);
        }

        var asyncWaiter = CreateAndAddAsyncWaiter();
        return cancellationToken.CanBeCanceled
            ? WaitUntilCountOrCancellationAsync(asyncWaiter, cancellationToken)
            : asyncWaiter;
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

    private async Task<bool> WaitUntilCountOrCancellationAsync(TaskNode asyncWaiter, CancellationToken cancellationToken)
    {
        await ((Task)asyncWaiter.WaitAsync(cancellationToken))
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (cancellationToken.IsCancellationRequested)
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        if (asyncWaiter.IsCompleted)
            return true;

        if (RemoveAsyncWaiter(asyncWaiter))
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return await asyncWaiter.ConfigureAwait(false);
    }

    internal int Release()
    {
        var currentCount = m_currentCount;
        if (currentCount == m_maxCount)
            throw new InvalidOperationException("The internal stream semaphore was released without acquisition.");

        var returnCount = currentCount++;
        if (m_asyncHead is not null)
        {
            --currentCount;
            var waiterTask = m_asyncHead;
            RemoveAsyncWaiter(waiterTask);
            waiterTask.TrySetResult(true);
        }
        m_currentCount = currentCount;
        return returnCount;
    }
}
