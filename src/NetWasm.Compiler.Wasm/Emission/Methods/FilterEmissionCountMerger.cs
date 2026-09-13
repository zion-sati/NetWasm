using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class FilterEmissionCountMerger : IFilterEmissionCountMerger
{
    public void Merge(
        IList<ManagedMethodEmissionRecord> emissions,
        StructuredMethod filterMethod,
        ImmutableDictionary<int, int> filterCounts)
    {
        ArgumentNullException.ThrowIfNull(emissions);
        ArgumentNullException.ThrowIfNull(filterMethod);
        ArgumentNullException.ThrowIfNull(filterCounts);

        if (filterCounts.IsEmpty)
        {
            return;
        }

        var methodKey = ManagedMethodBodyKey.Resolve(filterMethod);
        var index = -1;
        for (var candidate = 0; candidate < emissions.Count; candidate++)
        {
            if (emissions[candidate].Emission.MethodKey == methodKey)
            {
                index = candidate;
                break;
            }
        }
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"filter emission owner '{methodKey}' has no managed method body emission");
        }

        var item = emissions[index];
        var counts = item.Emission.OriginalBlockEmissionCounts.ToBuilder();
        foreach ((var blockIndex, var count) in filterCounts)
        {
            counts.TryGetValue(blockIndex, out var previousCount);
            counts[blockIndex] = previousCount + count;
        }
        emissions[index] = item with
        {
            Emission = item.Emission with
            {
                OriginalBlockEmissionCounts = counts.ToImmutable(),
            },
        };
    }
}
