namespace NetWasm.Compiler.Analysis;

/// <summary>
/// Leaves each method to be analyzed with its batch, for hosts that analyze on one thread.
/// </summary>
internal sealed class DeferredReachableMethodPrefetcher : IReachableMethodPrefetcher
{
    public void Prefetch(ReachableMethodRequest request) =>
        System.ArgumentNullException.ThrowIfNull(request);
}
