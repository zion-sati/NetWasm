using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace NetWasm.Compiler.Analysis;

internal sealed class SynchronousReachableMethodBatchAnalyzer(
    IReachableMethodAnalyzer analyzer,
    IReachableMethodBatchObserver observer) : IReachableMethodBatchAnalyzer
{
    private readonly IReachableMethodAnalyzer _analyzer =
        analyzer ?? throw new ArgumentNullException(nameof(analyzer));
    private readonly IReachableMethodBatchObserver _observer =
        observer ?? throw new ArgumentNullException(nameof(observer));

    public IEnumerable<ReachableMethodAnalysis> Analyze(
        ImmutableArray<ReachableMethodRequest> requests)
    {
        if (requests.IsDefault)
        {
            throw new ArgumentException(
                "The reachable-method request batch must be initialized.",
                nameof(requests));
        }

        return requests.IsEmpty ? [] : AnalyzeSynchronous(requests);
    }

    private IEnumerable<ReachableMethodAnalysis> AnalyzeSynchronous(
        ImmutableArray<ReachableMethodRequest> requests)
    {
        var batchStarted = Stopwatch.GetTimestamp();
        var workerBusyTicks = 0L;
        var results = new (ReachableMethodAnalysis? Analysis, Exception? Failure)[requests.Length];
        try
        {
            // Like the parallel strategy, finish the batch even when a consumer
            // stops enumeration or a result fails. Publish failures in request order.
            for (var requestIndex = 0; requestIndex < requests.Length; requestIndex++)
            {
                var workerStarted = Stopwatch.GetTimestamp();
                try
                {
                    results[requestIndex].Analysis = _analyzer.Analyze(requests[requestIndex]);
                }
                catch (Exception exception)
                {
                    results[requestIndex].Failure = exception;
                }
                finally
                {
                    workerBusyTicks += Stopwatch.GetTimestamp() - workerStarted;
                }
            }

            foreach (var result in results)
            {
                if (result.Failure is { } failure)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }

                yield return result.Analysis
                    ?? throw new InvalidOperationException(
                        "A reachable-method analyzer did not publish a result.");
            }
        }
        finally
        {
            _observer.Observe(new ReachableMethodBatchObservation(
                requests.Length,
                WorkerCount: 1,
                Stopwatch.GetElapsedTime(batchStarted),
                Stopwatch.GetElapsedTime(0, workerBusyTicks),
                OrderedWait: TimeSpan.Zero,
                PeakBufferedResults: 0));
        }
    }
}
