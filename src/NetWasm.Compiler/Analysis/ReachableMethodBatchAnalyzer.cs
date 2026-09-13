using System.Diagnostics;
using System.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachableMethodBatchAnalyzer(
    ImmutableArray<IReachableMethodAnalyzer> analyzers,
    IReachableMethodBatchObserver observer) : IReachableMethodBatchAnalyzer
{
    private readonly IReachableMethodBatchObserver _observer =
        observer ?? throw new ArgumentNullException(nameof(observer));
    private readonly ImmutableArray<IReachableMethodAnalyzer> _analyzers = Validate(analyzers);

    public IEnumerable<ReachableMethodAnalysis> Analyze(
        ImmutableArray<ReachableMethodRequest> requests)
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

        return AnalyzeConcurrent(requests);
    }

    private IEnumerable<ReachableMethodAnalysis> AnalyzeConcurrent(
        ImmutableArray<ReachableMethodRequest> requests)
    {
        var batchStarted = Stopwatch.GetTimestamp();
        var orderedWaitTicks = 0L;
        var peakBufferedResults = 0;
        var workerCount = Math.Min(_analyzers.Length, requests.Length);
        var workerBusyTicks = 0L;
        var pending = new ConcurrentQueue<int>();
        for (var requestIndex = 0; requestIndex < requests.Length; requestIndex++)
        {
            pending.Enqueue(requestIndex);
        }

        using var completed = new BlockingCollection<ReachableMethodResult>();
        var workers = new Task[workerCount];

        for (var workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            var analyzer = _analyzers[workerIndex];

            workers[workerIndex] = Task.Run(() =>

            {

                var workerStarted = Stopwatch.GetTimestamp();

                try

                {

                    AnalyzePending(analyzer);

                }

                finally

                {

                    Interlocked.Add(ref workerBusyTicks, Stopwatch.GetTimestamp() - workerStarted);

                }

            });
        }

        var buffered = new Dictionary<int, ReachableMethodResult>();
        try
        {
            for (var requestIndex = 0; requestIndex < requests.Length; requestIndex++)
            {
                var waitStarted = Stopwatch.GetTimestamp();

                var result = TakeResult(requestIndex);

                orderedWaitTicks += Stopwatch.GetTimestamp() - waitStarted;

                peakBufferedResults = Math.Max(peakBufferedResults, buffered.Count);
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
            Task.WaitAll(workers);

            _observer.Observe(new ReachableMethodBatchObservation(

                requests.Length,

                workerCount,

                Stopwatch.GetElapsedTime(batchStarted),

                Stopwatch.GetElapsedTime(0, workerBusyTicks),

                Stopwatch.GetElapsedTime(0, orderedWaitTicks),

                peakBufferedResults));
        }

        void AnalyzePending(IReachableMethodAnalyzer analyzer)
        {
            while (pending.TryDequeue(out var requestIndex))
            {
                try
                {
                    completed.Add(new ReachableMethodResult(
                        requestIndex,
                        analyzer.Analyze(requests[requestIndex]),
                        Failure: null));
                }
                catch (Exception exception)
                {
                    completed.Add(new ReachableMethodResult(
                        requestIndex,
                        Analysis: null,
                        exception));
                }
            }
        }

        ReachableMethodResult TakeResult(int requestIndex)
        {
            if (buffered.Remove(requestIndex, out var bufferedResult))
            {
                return bufferedResult;
            }

            while (true)
            {
                var completedResult = completed.Take();
                if (completedResult.Index == requestIndex)
                {
                    return completedResult;
                }

                buffered.Add(completedResult.Index, completedResult);
            }
        }
    }

    private sealed record ReachableMethodResult(
        int Index,
        ReachableMethodAnalysis? Analysis,
        Exception? Failure);

    private static ImmutableArray<IReachableMethodAnalyzer> Validate(
        ImmutableArray<IReachableMethodAnalyzer> analyzers)
    {
        if (analyzers.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "At least one reachable-method analyzer is required.",
                nameof(analyzers));
        }

        foreach (var analyzer in analyzers)
        {
            ArgumentNullException.ThrowIfNull(analyzer);
        }

        return analyzers;
    }
}
