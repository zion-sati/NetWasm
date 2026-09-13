using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IControlFlowDistanceFinder
{
    Dictionary<int, int> Find(ControlFlowStructuringState state, int start, int? stop, ImmutableHashSet<int> allowed);
}
