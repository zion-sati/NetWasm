using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal interface IStructuredControlFlowOwnershipProjector
{
    StructuredMethodDraft Project(
        StructuredMethodDraft method,
        ImmutableDictionary<int, int> selectedOwners);
}
