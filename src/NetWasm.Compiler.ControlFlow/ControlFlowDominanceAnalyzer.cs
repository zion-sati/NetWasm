using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public sealed class ControlFlowDominanceAnalyzer : IControlFlowDominanceAnalyzer
{
    // The maximal fixed point of Dom(n) = {n} ∪ ∩ Dom(p) over n's predecessors within the
    // blocks, starting from {entry} for the entry and every block for the rest.
    public Dictionary<int, ImmutableHashSet<int>> Analyze(
        ControlFlowGraph graph,
        int entry,
        ImmutableHashSet<int> blocks)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(blocks);
        var sets = new DominanceBitSets(DominanceBitSets.CountFor(graph, blocks));
        foreach (var node in blocks)
        {
            var row = sets.Row(node);
            if (node == entry)
            {
                row.Clear();
                DominanceBitSets.Set(row, entry);
            }
            else
            {
                DominanceBitSets.Fill(row, blocks);
            }
        }
        var order = new List<int>(blocks.Count);
        foreach (var node in blocks)
        {
            if (node != entry) order.Add(node);
        }
        order.Sort();
        var scratch = sets.Scratch;
        bool changed;
        do
        {
            changed = false;
            foreach (var node in order)
            {
                var first = true;
                foreach (var predecessor in graph.Predecessors[node])
                {
                    if (!blocks.Contains(predecessor)) continue;
                    if (first)
                    {
                        sets.Row(predecessor).CopyTo(scratch);
                        first = false;
                    }
                    else
                    {
                        DominanceBitSets.And(scratch, sets.Row(predecessor));
                    }
                }
                if (first) Array.Clear(scratch);
                DominanceBitSets.Set(scratch, node);
                changed |= sets.Update(node);
            }
        }
        while (changed);
        return sets.ToSets(blocks);
    }
}
