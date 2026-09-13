using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IReachableBlockFinder
{
    ImmutableHashSet<int> Find(ControlFlowStructuringState state, int start, int? stop, ImmutableHashSet<int> allowed);

    ImmutableHashSet<int> Find(ControlFlowGraph graph, int entry, ImmutableHashSet<int> allowed);

    ImmutableHashSet<int> Find(
        ControlFlowGraph graph,
        int entry,
        ImmutableHashSet<int> stops,
        ImmutableHashSet<int> allowed);
}
