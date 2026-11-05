using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public sealed class ControlFlowPostDominanceAnalyzer : IControlFlowPostDominanceAnalyzer
{
    // The maximal fixed point of PDom(n) = {n} ∪ ∩ PDom(s) over n's successors, where a
    // block with a successor outside the allowed blocks, the stop block and a block with no
    // allowed successor post-dominate only themselves.
    public Dictionary<int, ImmutableHashSet<int>> Analyze(
        ControlFlowGraph graph,
        int? stopBlock,
        ImmutableHashSet<int> allowed)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(allowed);
        var sets = new DominanceBitSets(DominanceBitSets.CountFor(graph, allowed));
        foreach (var node in allowed)
        {
            var row = sets.Row(node);
            var anyAllowed = false;
            foreach (var successor in graph.Successors[node])
            {
                if (allowed.Contains(successor))
                {
                    anyAllowed = true;
                    break;
                }
            }
            if (node == stopBlock || !anyAllowed)
            {
                row.Clear();
                DominanceBitSets.Set(row, node);
            }
            else
            {
                DominanceBitSets.Fill(row, allowed);
            }
        }
        var order = new List<int>(allowed);
        order.Sort(static (left, right) => right.CompareTo(left));
        var scratch = sets.Scratch;
        bool changed;
        do
        {
            changed = false;
            foreach (var node in order)
            {
                if (node == stopBlock)
                {
                    continue;
                }
                var first = true;
                var leaves = false;
                foreach (var successor in graph.Successors[node])
                {
                    if (!allowed.Contains(successor))
                    {
                        leaves = true;
                        continue;
                    }
                    if (first)
                    {
                        sets.Row(successor).CopyTo(scratch);
                        first = false;
                    }
                    else
                    {
                        DominanceBitSets.And(scratch, sets.Row(successor));
                    }
                }
                if (first || leaves) Array.Clear(scratch);
                DominanceBitSets.Set(scratch, node);
                changed |= sets.Update(node);
            }
        }
        while (changed);
        return sets.ToSets(allowed);
    }
}
