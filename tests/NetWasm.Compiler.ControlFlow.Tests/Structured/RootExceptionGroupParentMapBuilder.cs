using System.Collections.Generic;
using System.Collections.Immutable;
using NwDraft = NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Tests.Structured;

internal sealed class RootExceptionGroupParentMapBuilder : NwDraft.IExceptionGroupParentMapBuilder
{
    public IReadOnlyDictionary<NwDraft.StructuredExceptionGroupDraft, NwDraft.StructuredExceptionGroupDraft?> Build(
        ImmutableArray<NwDraft.StructuredExceptionGroupDraft> groups)
    {
        var parents = new Dictionary<NwDraft.StructuredExceptionGroupDraft, NwDraft.StructuredExceptionGroupDraft?>(
            ReferenceEqualityComparer.Instance);

        foreach (var group in groups)
        {
            parents.Add(group, null);
        }

        return parents;
    }
}
