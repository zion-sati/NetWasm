using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Generic;

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IExceptionGroupStructurer
{
    ImmutableArray<StructuredExceptionGroupDraft> Structure(ControlFlowStructuringState state, ImmutableArray<CilExceptionRegion> regions);
}
