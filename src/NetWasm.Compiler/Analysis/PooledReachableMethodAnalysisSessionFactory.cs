using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Analysis;

/// <summary>
/// Begins closures whose methods are analyzed by a pool of workers, each with its own
/// analyzer, and by the closure's thread with its own.
/// </summary>
internal sealed class PooledReachableMethodAnalysisSessionFactory(
    ImmutableArray<IReachableMethodAnalyzer> workers,
    IReachableMethodAnalyzer closureAnalyzer,
    IReachableMethodBatchObserver observer) : IReachableMethodAnalysisSessionFactory
{
    private readonly ImmutableArray<IReachableMethodAnalyzer> _workers = workers.IsDefaultOrEmpty
        ? throw new ArgumentException("At least one reachable-method analyzer is required.", nameof(workers))
        : workers;
    private readonly IReachableMethodAnalyzer _closureAnalyzer =
        closureAnalyzer ?? throw new ArgumentNullException(nameof(closureAnalyzer));
    private readonly IReachableMethodBatchObserver _observer =
        observer ?? throw new ArgumentNullException(nameof(observer));

    public ReachableMethodAnalysisSession Begin()
    {
        var pool = new ReachableMethodAnalysisPool(_workers, _closureAnalyzer);
        return new(new ReachableMethodPrefetcher(pool), new PipelinedReachableMethodBatchAnalyzer(pool, _observer), pool);
    }
}
