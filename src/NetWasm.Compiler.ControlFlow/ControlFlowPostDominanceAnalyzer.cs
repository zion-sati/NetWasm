using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow;

public sealed class ControlFlowPostDominanceAnalyzer : IControlFlowPostDominanceAnalyzer
{
    public Dictionary<int, ImmutableHashSet<int>> Analyze(
        ControlFlowGraph graph,
        int? stopBlock,
        ImmutableHashSet<int> allowed)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(allowed);
        var result = allowed.ToDictionary(
            node => node,
            node => node == stopBlock || !graph.Successors[node].Any(allowed.Contains)
                ? [node]
                : allowed);
        bool changed;
        do
        {
            changed = false;
            foreach (var node in allowed.OrderDescending())
            {
                if (node == stopBlock)
                {
                    continue;
                }
                var successors = graph.Successors[node].Where(allowed.Contains).ToArray();
                var intersection = successors.Length == 0
                    ? []
                    : result[successors[0]];
                foreach (var successor in successors.Skip(1))
                {
                    intersection = intersection.Intersect(result[successor]);
                }
                if (graph.Successors[node].Any(successor => !allowed.Contains(successor)))
                {
                    intersection = [];
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
