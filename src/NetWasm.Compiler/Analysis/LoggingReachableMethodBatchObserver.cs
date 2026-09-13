using System;
using Microsoft.Extensions.Logging;

namespace NetWasm.Compiler.Analysis;

internal sealed class LoggingReachableMethodBatchObserver(
    ILogger<LoggingReachableMethodBatchObserver> logger)
    : IReachableMethodBatchObserver
{
    private static readonly Action<ILogger, int, int, double, double, double, int, Exception?>
        LogBatch = LoggerMessage.Define<int, int, double, double, double, int>(
            LogLevel.Debug,
            new EventId(1, nameof(Observe)),
            "Reachable method batch: requests={RequestCount}, workers={WorkerCount}, elapsedMs={ElapsedMilliseconds}, workerBusyMs={WorkerBusyMilliseconds}, orderedWaitMs={OrderedWaitMilliseconds}, peakBuffered={PeakBufferedResults}");
    public void Observe(ReachableMethodBatchObservation observation)
    {
        LogBatch(
            logger,
            observation.RequestCount,
            observation.WorkerCount,
            observation.Elapsed.TotalMilliseconds,
            observation.WorkerBusy.TotalMilliseconds,
            observation.OrderedWait.TotalMilliseconds,
            observation.PeakBufferedResults,
            null);
    }
}
