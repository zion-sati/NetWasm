using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace NetWasm.Compiler.Analysis;

/// <summary>
/// Analyzes the methods a reachability closure discovers while it applies earlier results.
/// Requests queue as the closure finds them and workers, each with its own analyzer, take
/// them in that order; workers start when requests arrive and stop when none are left. The
/// closure takes results in its own deterministic order, and analyzes on its own thread any
/// request no worker has started. Only the closure's thread submits and takes results.
/// </summary>
internal sealed class ReachableMethodAnalysisPool : IDisposable
{
    private readonly ConcurrentQueue<ReachableMethodAnalysisSlot> _queue = new();
    private readonly Lock _idleGate = new();
    private readonly Stack<IReachableMethodAnalyzer> _idleAnalyzers;
    private readonly IReachableMethodAnalyzer _closureAnalyzer;
    private readonly Dictionary<ReachableMethodRequest, ReachableMethodAnalysisSlot> _slots =
        new(ReferenceEqualityComparer.Instance);
    // Set when a slot completes or a worker stops, for a closure waiting on either.
    private readonly ManualResetEventSlim _progress = new(false);
    private int _activeWorkers;
    private int _closed;
    private int _finishedAhead;
    private long _busyTicks;

    internal ReachableMethodAnalysisPool(
        ImmutableArray<IReachableMethodAnalyzer> workers,
        IReachableMethodAnalyzer closureAnalyzer)
    {
        if (workers.IsDefaultOrEmpty)
        {
            throw new ArgumentException("At least one reachable-method analyzer is required.", nameof(workers));
        }
        foreach (var worker in workers)
        {
            ArgumentNullException.ThrowIfNull(worker);
        }
        _idleAnalyzers = new Stack<IReachableMethodAnalyzer>(workers);
        _closureAnalyzer = closureAnalyzer ?? throw new ArgumentNullException(nameof(closureAnalyzer));
        WorkerCount = workers.Length;
    }

    internal int WorkerCount { get; }

    internal long BusyTicks => Interlocked.Read(ref _busyTicks);

    // Results finished before the closure took them.
    internal int FinishedAhead => Volatile.Read(ref _finishedAhead);

    internal ReachableMethodAnalysisSlot Submit(ReachableMethodRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_slots.TryGetValue(request, out var slot))
        {
            return slot;
        }
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
        slot = new ReachableMethodAnalysisSlot(request);
        _slots.Add(request, slot);
        _queue.Enqueue(slot);
        StartWorkers();
        return slot;
    }

    // The slot's analysis, analyzing it here if no worker has started it.
    internal ReachableMethodAnalysis Take(ReachableMethodAnalysisSlot slot, ref long waitTicks)
    {
        ArgumentNullException.ThrowIfNull(slot);
        _slots.Remove(slot.Request);
        if (slot.TryClaim())
        {
            Run(slot, _closureAnalyzer);
        }
        else if (!slot.IsDone)
        {
            var waitStarted = Stopwatch.GetTimestamp();
            Wait(slot, static slot => slot.IsDone);
            waitTicks += Stopwatch.GetTimestamp() - waitStarted;
        }
        Interlocked.Decrement(ref _finishedAhead);
        if (slot.Failure is { } failure)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return slot.Analysis ?? throw new InvalidOperationException(
            "A reachable-method analyzer did not publish a result.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }
        while (_queue.TryDequeue(out var slot))
        {
            slot.TryCancel();
        }
        // Workers finish the analysis they hold. The event stays undisposed: a stopping worker
        // sets it after it stops counting, and it holds no handle unless one is asked for.
        Wait(this, static pool => Volatile.Read(ref pool._activeWorkers) == 0);
    }

    private void StartWorkers()
    {
        while (!_queue.IsEmpty && TryActivate(out var analyzer))
        {
            // Workers run in the closure's execution context: the active compilation's cache
            // request and diagnostic scope flow through it.
            ThreadPool.QueueUserWorkItem(
                static worker => worker.Pool.Drain(worker.Analyzer),
                new Worker(this, analyzer),
                preferLocal: false);
        }
    }

    private bool TryActivate(out IReachableMethodAnalyzer analyzer)
    {
        while (Volatile.Read(ref _closed) == 0)
        {
            var active = Volatile.Read(ref _activeWorkers);
            if (active >= WorkerCount)
            {
                break;
            }
            if (Interlocked.CompareExchange(ref _activeWorkers, active + 1, active) != active)
            {
                continue;
            }
            // A worker returns its analyzer before it stops counting as active, so one is idle.
            lock (_idleGate)
                analyzer = _idleAnalyzers.Pop();
            return true;
        }
        analyzer = null!;
        return false;
    }

    private void Drain(IReachableMethodAnalyzer analyzer)
    {
        while (true)
        {
            while (Volatile.Read(ref _closed) == 0 && _queue.TryDequeue(out var slot))
            {
                if (slot.TryClaim())
                {
                    Run(slot, analyzer);
                }
            }
            lock (_idleGate)
                _idleAnalyzers.Push(analyzer);
            Interlocked.Decrement(ref _activeWorkers);
            _progress.Set();
            // The closure queues before it counts active workers, and a worker counts itself out
            // before it looks at the queue, so a request queued meanwhile is seen by one of them.
            if (_queue.IsEmpty || !TryActivate(out analyzer))
            {
                return;
            }
        }
    }

    private void Run(ReachableMethodAnalysisSlot slot, IReachableMethodAnalyzer analyzer)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            slot.Analysis = analyzer.Analyze(slot.Request);
        }
        catch (Exception exception)
        {
            slot.Failure = exception;
        }
        Interlocked.Add(ref _busyTicks, Stopwatch.GetTimestamp() - started);
        Interlocked.Increment(ref _finishedAhead);
        slot.Complete();
        _progress.Set();
    }

    private void Wait<TState>(TState state, Func<TState, bool> condition)
    {
        while (!condition(state))
        {
            _progress.Reset();
            // Reset before looking again: a completion after this look sets the event.
            Interlocked.MemoryBarrier();
            if (condition(state))
            {
                return;
            }
            _progress.Wait();
        }
    }

    private sealed class Worker(ReachableMethodAnalysisPool pool, IReachableMethodAnalyzer analyzer)
    {
        internal ReachableMethodAnalysisPool Pool { get; } = pool;

        internal IReachableMethodAnalyzer Analyzer { get; } = analyzer;
    }
}
