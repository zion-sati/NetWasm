using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Draft;

internal sealed class StructuredControlFlowOwnerSelector : IStructuredControlFlowOwnerSelector
{
    public ImmutableDictionary<int, int> Select(
        ImmutableArray<StructuredControlFlowBlockOccurrence> occurrences)
    {
        var selected = ImmutableDictionary.CreateBuilder<int, int>();
        var selectedPreferred = ImmutableDictionary.CreateBuilder<int, bool>();
        foreach (var occurrence in occurrences)
        {
            if (!selectedPreferred.TryGetValue(occurrence.Block, out var wasPreferred) ||
                occurrence.Preferred && !wasPreferred)
            {
                selected[occurrence.Block] = occurrence.Ordinal;
                selectedPreferred[occurrence.Block] = occurrence.Preferred;
            }
        }

        return selected.ToImmutable();
    }
}
