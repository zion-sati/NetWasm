using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public interface IControlFlowDominanceAnalyzer
{
    Dictionary<int, ImmutableHashSet<int>> Analyze(
        ControlFlowGraph graph,
        int entry,
        ImmutableHashSet<int> blocks);
}
