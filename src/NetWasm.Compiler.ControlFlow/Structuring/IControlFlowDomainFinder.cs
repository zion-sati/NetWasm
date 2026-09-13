using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IControlFlowDomainFinder
{
    ImmutableArray<ControlFlowDomain> Find(ControlFlowGraph graph);
}
