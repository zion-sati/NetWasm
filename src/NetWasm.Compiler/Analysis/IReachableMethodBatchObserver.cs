namespace NetWasm.Compiler.Analysis;

internal interface IReachableMethodBatchObserver
{
    void Observe(ReachableMethodBatchObservation observation);
}
