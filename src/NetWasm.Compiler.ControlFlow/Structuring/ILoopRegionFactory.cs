using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface ILoopRegionFactory
{
    LoopRegionCreation Create(ControlFlowStructuringState state, ControlFlowGraph graph, ImmutableHashSet<int> component, ControlFlowDomain domain, ImmutableArray<ControlFlowDomain> domains);
}
