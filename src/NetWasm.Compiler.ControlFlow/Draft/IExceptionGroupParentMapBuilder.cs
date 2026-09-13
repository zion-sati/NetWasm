using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal interface IExceptionGroupParentMapBuilder
{
    IReadOnlyDictionary<StructuredExceptionGroupDraft, StructuredExceptionGroupDraft?> Build(
        ImmutableArray<StructuredExceptionGroupDraft> exceptionGroups);
}
