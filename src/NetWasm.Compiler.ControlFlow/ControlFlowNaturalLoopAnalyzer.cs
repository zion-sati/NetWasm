using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ControlFlow;

public sealed class ControlFlowNaturalLoopAnalyzer(
    IControlFlowDominanceAnalyzer dominance) : IControlFlowNaturalLoopAnalyzer
{
    private readonly IControlFlowDominanceAnalyzer _dominance =
        dominance ?? throw new ArgumentNullException(nameof(dominance));

    public ImmutableArray<ImmutableHashSet<int>> Analyze(
        ControlFlowGraph graph,
        int entry,
        ImmutableHashSet<int> blocks)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(blocks);
        var dominators = _dominance.Analyze(graph, entry, blocks);
        var loopsByHeader = new Dictionary<int, HashSet<int>>();
        foreach (var source in blocks)
        {
            foreach (var target in graph.Successors[source])
            {
                if (!blocks.Contains(target) || !dominators[source].Contains(target))
                {
                    continue;
                }
                if (!loopsByHeader.TryGetValue(target, out var loop))
                {
                    loop = [target];
                    loopsByHeader.Add(target, loop);
                }
                var pending = new Stack<int>();
                if (loop.Add(source) && source != target)
                {
                    pending.Push(source);
                }
                while (pending.TryPop(out var node))
                {
                    foreach (var predecessor in graph.Predecessors[node])
                    {
                        if (!blocks.Contains(predecessor))
                        {
                            continue;
                        }
                        if (loop.Add(predecessor) && predecessor != target)
                        {
                            pending.Push(predecessor);
                        }
                    }
                }
            }
        }
        return
        [
            .. loopsByHeader.OrderBy(pair => pair.Key)
                .Select(pair => pair.Value.ToImmutableHashSet()),
        ];
    }
}
