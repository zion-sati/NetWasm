namespace NetWasm.Compiler.Analysis;

/// <summary>Starts analyzing a method the closure will ask for, ahead of its turn.</summary>
internal interface IReachableMethodPrefetcher
{
    void Prefetch(ReachableMethodRequest request);
}
