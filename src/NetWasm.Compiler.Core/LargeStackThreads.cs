// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace NetWasm.Compiler.Core;

/// <summary>
/// Runs compiler work on threads whose stack holds code nested as deep as real programs nest it.
/// </summary>
/// <remarks>
/// Passes over a method recurse once per nesting level: structuring a 3,000-arm <c>if</c>/<c>else if</c> chain
/// recurses 3,000 levels, about 1.4 KB each. A thread pool thread has about 1.5 MB of stack, and a Windows main
/// thread 1 MB, so such a method crashed the compiler. These threads reserve 256 MB, about 190,000 levels; the
/// stack is reserved address space, committed only as deep as a thread goes. Idle threads are reused, as the
/// thread pool reuses its own, and there are at most as many as processors: compilations running side by side,
/// as a test host runs them, share them, so their working sets do not all grow at once. A large-stack thread that
/// runs work takes its own items too, so it never waits for a thread, only for items other threads are running;
/// items therefore must not wait for each other. An idle thread blocks without running until there is work again.
/// </remarks>
public static class LargeStackThreads
{
    public const int StackSize = 256 * 1024 * 1024;

    // About as long as the thread pool's workers spin for work before they sleep.
    private const int SpinLimit = 50;
    // As many threads as processors, and two on one processor, so a caller and one item can run side by side.
    private static readonly int MaximumThreads = Math.Max(2, Environment.ProcessorCount);
    // An object, not a Lock: waiting threads use Monitor.Wait and Monitor.Pulse on it.
    private static readonly object Gate = new();
    private static readonly Queue<WorkItem> Pending = new();
    // Guarded by Gate: threads started, threads waiting for work, and queued work no thread has taken.
    private static int s_threads;
    private static int s_waiting;
    private static int s_queued;

    [ThreadStatic]
    private static bool t_largeStack;

    /// <summary>Whether the current thread is one of these.</summary>
    public static bool IsCurrentThread => t_largeStack;

    /// <summary>
    /// Runs <paramref name="body"/> once for each index below <paramref name="count"/>, on large-stack threads, and
    /// returns when all have finished. On a large-stack thread the caller runs items too, taking the next one until
    /// none is left, and then waits only for items other threads are running. Items must not wait for each other:
    /// one may run after another on the same thread. A body's exception is rethrown here, the lowest index's first.
    /// A host without threads cannot start them: callers keep their work on the calling thread there.
    /// </summary>
    public static void Run(int count, Action<int> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return;
        }

        using var items = new IndexedItems(count, body);
        var participates = t_largeStack;
        for (var helper = participates ? 1 : 0; helper < count; helper++)
        {
            Enqueue(items.RunEach);
        }

        if (participates)
        {
            items.RunEach();
        }

        items.Wait();
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a large-stack thread and returns its result, on the current thread when it
    /// already is one. A host without threads cannot start one: callers run the work themselves there.
    /// </summary>
    public static TResult Call<TResult>(Func<TResult> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (t_largeStack)
        {
            return work();
        }

        TResult result = default!;
        Run(1, _ => result = work());
        return result;
    }

    // A queued item goes to a waiting thread, or to a new one while queued work outnumbers the waiting threads and
    // fewer than the maximum have started; otherwise it waits for a thread to finish what it runs.
    private static void Enqueue(Action body)
    {
        var item = new WorkItem(ExecutionContext.Capture(), body);
        bool start;
        lock (Gate)
        {
            Pending.Enqueue(item);
            s_queued++;
            start = s_queued > s_waiting && s_threads < MaximumThreads;
            if (start)
            {
                s_threads++;
            }
            else
            {
                Monitor.Pulse(Gate);
            }
        }

        if (start)
        {
            // Unsafe: without the execution context of the call that starts it. Started with it, the thread would
            // keep it, and the compilation it belongs to, for as long as the process runs, and hand it to work
            // whose caller suppressed flow.
            new Thread(Work, StackSize)
            {
                IsBackground = true,
                Name = "NetWasm compiler worker",
            }.UnsafeStart();
        }
    }

    private static void Work()
    {
        t_largeStack = true;
        // The thread started without a context, so this is the empty one. Work whose caller suppressed flow runs in
        // it, and what the work sets there ends with the work, as the thread pool resets its own threads.
        var empty = ExecutionContext.Capture()!;
        while (true)
        {
            var item = Next();
            ExecutionContext.Run(item.Context ?? empty, static state => ((Action)state!)(), item.Body);
        }
    }

    // The next queued item, waiting for one.
    private static WorkItem Next()
    {
        lock (Gate)
        {
            if (Pending.Count != 0)
            {
                return Take();
            }

            // Waiting from here until it takes work or ends, spinning included: a producer counts on it.
            s_waiting++;
        }

        // Work arrives in quick succession, as the thread pool's own workers find, so the thread watches for it
        // briefly before it sleeps and needs no wake-up for most of it.
        var spinner = default(SpinWait);
        for (var spin = 0; spin < SpinLimit && Volatile.Read(ref s_queued) == 0; spin++)
        {
            spinner.SpinOnce(sleep1Threshold: -1);
        }

        lock (Gate)
        {
            while (Pending.Count == 0)
            {
                Monitor.Wait(Gate);
            }

            s_waiting--;
            return Take();
        }
    }

    // Under Gate.
    private static WorkItem Take()
    {
        s_queued--;
        return Pending.Dequeue();
    }

    private readonly record struct WorkItem(ExecutionContext? Context, Action Body);

    // One Run's items: each runner takes the next index until none is left, so a helper that starts late finds
    // nothing and returns, touching only the index, never the disposed countdown.
    private sealed class IndexedItems(int count, Action<int> body) : IDisposable
    {
        private readonly ExceptionDispatchInfo?[] _failures = new ExceptionDispatchInfo?[count];
        private readonly CountdownEvent _finished = new(count);
        private int _next = -1;

        internal void RunEach()
        {
            for (var index = Interlocked.Increment(ref _next); index < count; index = Interlocked.Increment(ref _next))
            {
                try
                {
                    body(index);
                }
#pragma warning disable CA1031 // Rethrown by Wait, on the calling thread.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    _failures[index] = ExceptionDispatchInfo.Capture(exception);
                }
                finally
                {
                    _finished.Signal();
                }
            }
        }

        internal void Wait()
        {
            _finished.Wait();
            foreach (var failure in _failures) failure?.Throw();
        }

        public void Dispose() => _finished.Dispose();
    }
}
