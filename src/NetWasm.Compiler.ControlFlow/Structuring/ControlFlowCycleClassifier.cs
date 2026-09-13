using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ControlFlowCycleClassifier : IControlFlowCycleClassifier
{
    public bool Classify(
        ControlFlowGraph graph,
        ImmutableHashSet<int> component) =>
        component.Count > 1 || graph.Successors[component.Single()].Contains(component.Single());
}
