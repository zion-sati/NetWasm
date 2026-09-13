using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface ILoopExitPartitioner
{
    LoopExitPartition Partition(
        ControlFlowGraph graph,
        ImmutableHashSet<int> component,
        int exit,
        int conditionExit,
        ImmutableHashSet<int> additionalExits);
}
