using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal interface IStructuredControlFlowOwnerSelector
{
    ImmutableDictionary<int, int> Select(
        ImmutableArray<StructuredControlFlowBlockOccurrence> occurrences);
}
