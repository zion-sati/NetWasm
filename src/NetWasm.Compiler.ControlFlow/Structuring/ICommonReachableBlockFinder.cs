using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface ICommonReachableBlockFinder
{
    int? Find(ControlFlowStructuringState state, int first, int second, int? stop, ImmutableHashSet<int> allowed);

    int? Find(ControlFlowStructuringState state, int[] starts, int? stop, ImmutableHashSet<int> allowed);
}
