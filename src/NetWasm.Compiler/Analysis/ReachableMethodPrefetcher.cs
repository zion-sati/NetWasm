namespace NetWasm.Compiler.Analysis;

/// <summary>Queues a discovered method's analysis in its closure's pool.</summary>
internal sealed class ReachableMethodPrefetcher(ReachableMethodAnalysisPool pool) : IReachableMethodPrefetcher
{
    private readonly ReachableMethodAnalysisPool _pool = pool ?? throw new System.ArgumentNullException(nameof(pool));

    public void Prefetch(ReachableMethodRequest request) => _pool.Submit(request);
}
