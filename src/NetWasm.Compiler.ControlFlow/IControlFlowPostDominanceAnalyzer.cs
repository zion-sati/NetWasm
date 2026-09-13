using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public interface IControlFlowPostDominanceAnalyzer
{
    Dictionary<int, ImmutableHashSet<int>> Analyze(
        ControlFlowGraph graph,
        int? stopBlock,
        ImmutableHashSet<int> allowed);
}
