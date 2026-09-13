using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public interface IControlFlowComponentAnalyzer
{
    ImmutableArray<ImmutableHashSet<int>> Analyze(
        ControlFlowGraph graph,
        ImmutableHashSet<int> allowed);
}
