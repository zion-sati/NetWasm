using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IControlFlowCycleClassifier
{
    bool Classify(ControlFlowGraph graph, ImmutableHashSet<int> component);
}
