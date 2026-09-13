using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public interface IControlFlowNaturalLoopAnalyzer
{
    ImmutableArray<ImmutableHashSet<int>> Analyze(
        ControlFlowGraph graph,
        int entry,
        ImmutableHashSet<int> blocks);
}
