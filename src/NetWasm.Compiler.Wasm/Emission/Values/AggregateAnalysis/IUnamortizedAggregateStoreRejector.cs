namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IUnamortizedAggregateStoreRejector
{
    // Rejects the storages whose whole-value stores are not outside every loop that accesses their lanes.
    void Reject(ManagedAggregateAnalysisSession session);
}
