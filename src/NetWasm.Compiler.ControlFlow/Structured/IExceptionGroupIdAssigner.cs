using System.Collections.Immutable;

using Draft = NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal interface IExceptionGroupIdAssigner
{
    ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> Assign(
        ImmutableArray<Draft.StructuredExceptionGroupDraft> groups);
}
