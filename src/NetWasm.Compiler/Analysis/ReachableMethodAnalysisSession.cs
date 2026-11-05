using System;

namespace NetWasm.Compiler.Analysis;

/// <summary>
/// The method analysis of one reachability closure: requests it prefetches as it discovers
/// methods, and the batches it applies in order. Disposing it ends any analysis the closure
/// did not take, so none outlives the closure.
/// </summary>
internal sealed class ReachableMethodAnalysisSession(
    IReachableMethodPrefetcher prefetcher,
    IReachableMethodBatchAnalyzer batches,
    IDisposable lifetime) : IDisposable
{
    private readonly IDisposable _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));

    internal IReachableMethodPrefetcher Prefetcher { get; } =
        prefetcher ?? throw new ArgumentNullException(nameof(prefetcher));

    internal IReachableMethodBatchAnalyzer Batches { get; } =
        batches ?? throw new ArgumentNullException(nameof(batches));

    public void Dispose() => _lifetime.Dispose();
}
