using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public interface IControlFlowGraphAnalyzer
{
    ControlFlowGraphAnalysis Analyze(
        ControlFlowGraph graph,
        int entry,
        ImmutableHashSet<int> blocks);
}
