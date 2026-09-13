using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow;

public sealed class ControlFlowDominanceAnalyzer : IControlFlowDominanceAnalyzer
{
    public Dictionary<int, ImmutableHashSet<int>> Analyze(
        ControlFlowGraph graph,
        int entry,
        ImmutableHashSet<int> blocks)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(blocks);
        var result = blocks.ToDictionary(
            node => node,
            node => node == entry ? [entry] : blocks);
        bool changed;
        do
        {
            changed = false;
            foreach (var node in blocks.Where(node => node != entry).Order())
            {
                var predecessors = graph.Predecessors[node]
                    .Where(blocks.Contains)
                    .ToArray();
                var intersection = predecessors.Length == 0
                    ? []
                    : result[predecessors[0]];
                foreach (var predecessor in predecessors.Skip(1))
                {
                    intersection = intersection.Intersect(result[predecessor]);
                }
                var updated = intersection.Add(node);
                if (!updated.SetEquals(result[node]))
                {
                    result[node] = updated;
                    changed = true;
                }
            }
        }
        while (changed);
        return result;
    }
}
