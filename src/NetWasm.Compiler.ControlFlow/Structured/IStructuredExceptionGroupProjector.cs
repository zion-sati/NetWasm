using System.Collections.Immutable;
using Draft = global::NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal interface IStructuredExceptionGroupProjector
{
    StructuredExceptionGroup Project(
        Draft.StructuredExceptionGroupDraft group,
        StructuredExceptionGroupId id,
        Draft.StructuredExceptionGroupDraft? parent,
        Draft.StructuredMethodDraft method,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds);
}
