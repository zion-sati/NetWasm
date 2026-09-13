using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Generic;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IContinuationDispatcherBuilder
{
    StructuredDispatcherDraft Build(ControlFlowStructuringState state, IEnumerable<int> targets, int continuationBoundary, int methodEnd);
}
