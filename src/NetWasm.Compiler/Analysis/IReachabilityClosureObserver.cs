namespace NetWasm.Compiler.Analysis;

internal interface IReachabilityClosureObserver
{
    void Observe(ReachabilityClosureObservation observation);
}
