using System;

namespace NetWasm.Compiler.Analysis;

/// <summary>Begins closures that analyze each batch on the calling thread, as in the browser.</summary>
internal sealed class SynchronousReachableMethodAnalysisSessionFactory(
    IReachableMethodAnalyzer analyzer,
    IReachableMethodBatchObserver observer) : IReachableMethodAnalysisSessionFactory
{
    private readonly IReachableMethodBatchAnalyzer _batches =
        new SynchronousReachableMethodBatchAnalyzer(analyzer, observer);

    public ReachableMethodAnalysisSession Begin() =>
        new(new DeferredReachableMethodPrefetcher(), _batches, NothingToEnd.Instance);

    private sealed class NothingToEnd : IDisposable
    {
        internal static NothingToEnd Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
