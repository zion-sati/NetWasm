using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface INormalLeaveTargetFinder
{
    int[] Find(ControlFlowStructuringState state, IEnumerable<CilExceptionRegion> regions, IReadOnlyCollection<ExceptionGroupSource> excluded, IReadOnlyCollection<ExceptionGroupSource> handlerExcluded);
}
