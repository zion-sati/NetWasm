using System.Collections.Immutable;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Analysis.ManagedCallSites;

internal interface IManagedCallSiteLedgerWriter
{
    void Write(ReachabilityLedger ledger, ImmutableArray<ManagedCallSite> callSites);
}
