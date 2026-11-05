using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace NetWasm.Compiler.Analysis;

/// <summary>
/// Returns a batch's analyses in request order from its closure's pool, which may have
/// analyzed them while the closure applied earlier batches.
/// </summary>
internal sealed class PipelinedReachableMethodBatchAnalyzer(
    ReachableMethodAnalysisPool pool,
    IReachableMethodBatchObserver observer) : IReachableMethodBatchAnalyzer
{
    private readonly ReachableMethodAnalysisPool _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    private readonly IReachableMethodBatchObserver _observer =
        observer ?? throw new ArgumentNullException(nameof(observer));

    public IEnumerable<ReachableMethodAnalysis> Analyze(ImmutableArray<ReachableMethodRequest> requests)
    {
        if (requests.IsDefault)
        {
            throw new ArgumentException(
                "The reachable-method request batch must be initialized.",
                nameof(requests));
        }
        if (requests.IsEmpty)
        {
            return [];
        }
        // Every request is queued before the first result is taken.
        var slots = new ReachableMethodAnalysisSlot[requests.Length];
        for (var index = 0; index < requests.Length; index++)
        {
            slots[index] = _pool.Submit(requests[index]);
        }
        return Take(slots);
    }

    private IEnumerable<ReachableMethodAnalysis> Take(ReachableMethodAnalysisSlot[] slots)
    {
        var started = Stopwatch.GetTimestamp();
        var busyBefore = _pool.BusyTicks;
        var waitTicks = 0L;
        var peakFinishedAhead = 0;
        try
        {
            foreach (var slot in slots)
            {
                peakFinishedAhead = Math.Max(peakFinishedAhead, _pool.FinishedAhead);
                yield return _pool.Take(slot, ref waitTicks);
            }
        }
        finally
        {
            _observer.Observe(new ReachableMethodBatchObservation(
                slots.Length,
                _pool.WorkerCount,
                Stopwatch.GetElapsedTime(started),
                Stopwatch.GetElapsedTime(0, _pool.BusyTicks - busyBefore),
                Stopwatch.GetElapsedTime(0, waitTicks),
                peakFinishedAhead));
        }
    }
}
