using System;
using Microsoft.Extensions.Logging;

namespace NetWasm.Compiler.Analysis;

internal sealed class LoggingReachabilityClosureObserver(
    ILogger<LoggingReachabilityClosureObserver> logger)
    : IReachabilityClosureObserver
{
    private static readonly Action<ILogger, int, double, int, int, double, double, Exception?>
        LogIteration = LoggerMessage.Define<int, double, int, int, double, double>(
            LogLevel.Debug,
            new EventId(1, nameof(Observe)),
            "Reachability closure iteration: methodRequests={MethodRequestCount}, methodPublicationMs={MethodPublicationMilliseconds}, dispatches={DispatchCount}, resolvedDispatches={ResolvedDispatchCount}, dispatchPublicationMs={DispatchPublicationMilliseconds}, elapsedMs={ElapsedMilliseconds}");

    public void Observe(ReachabilityClosureObservation observation)
    {
        LogIteration(
            logger,
            observation.MethodRequestCount,
            observation.MethodPublication.TotalMilliseconds,
            observation.DispatchCount,
            observation.ResolvedDispatchCount,
            observation.DispatchPublication.TotalMilliseconds,
            observation.Elapsed.TotalMilliseconds,
            null);
    }
}
