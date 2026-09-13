using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface ILoopExitExtensionCollector
{
    ImmutableHashSet<int> Collect(ControlFlowGraph graph, ImmutableHashSet<int> component, int primaryExit, ImmutableHashSet<int> additionalExits);
}
