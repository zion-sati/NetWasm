using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface ILoopContinueTargetFinder
{
    int Find(ControlFlowStructuringState state, ControlFlowGraph graph, ImmutableHashSet<int> component, int header);
}
