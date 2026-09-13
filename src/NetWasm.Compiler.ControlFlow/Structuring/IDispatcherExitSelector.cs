using System.Collections.Generic;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IDispatcherExitSelector
{
    int[] Select(IEnumerable<int> successors, IEnumerable<int> allowed);
}
