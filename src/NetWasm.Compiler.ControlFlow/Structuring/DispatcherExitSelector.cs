using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class DispatcherExitSelector : IDispatcherExitSelector
{
    public int[] Select(IEnumerable<int> successors, IEnumerable<int> allowed)
    {
        ArgumentNullException.ThrowIfNull(successors);
        ArgumentNullException.ThrowIfNull(allowed);

        return successors.Where(allowed.Contains).ToArray();
    }
}
