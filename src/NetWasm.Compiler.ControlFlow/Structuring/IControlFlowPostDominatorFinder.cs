using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IControlFlowPostDominatorFinder
{
    Dictionary<int, ImmutableHashSet<int>> Find(ControlFlowStructuringState state, int? stop, ImmutableHashSet<int> allowed);
}
