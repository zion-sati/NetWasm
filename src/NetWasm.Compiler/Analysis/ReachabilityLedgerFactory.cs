namespace NetWasm.Compiler.Analysis;

internal sealed class ReachabilityLedgerFactory : IReachabilityLedgerFactory
{
    public ReachabilityLedger Create() => new();
}
