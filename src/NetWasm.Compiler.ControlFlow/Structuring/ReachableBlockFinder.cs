using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ReachableBlockFinder(IControlFlowDistanceFinder distances) : IReachableBlockFinder
{
    private readonly IControlFlowDistanceFinder _distances = distances ?? throw new ArgumentNullException(nameof(distances));

    public ImmutableHashSet<int> Find(ControlFlowStructuringState state,
        int start,
        int? stop,
        ImmutableHashSet<int> allowed) =>
        _distances.Find(state, start, stop, allowed).Keys
            .Where(block => block != stop)
            .ToImmutableHashSet();

    public ImmutableHashSet<int> Find(
        ControlFlowGraph graph,
        int entry,
        ImmutableHashSet<int> allowed)
    {
        var reachable = ImmutableHashSet.CreateBuilder<int>();
        var pending = new Stack<int>();
        pending.Push(entry);
        while (pending.TryPop(out var node))
        {
            if (!allowed.Contains(node) || !reachable.Add(node))
            {
                continue;
            }
            foreach (var successor in graph.Successors[node])
            {
                pending.Push(successor);
            }
        }
        return reachable.ToImmutable();
    }

    public ImmutableHashSet<int> Find(
        ControlFlowGraph graph,
        int entry,
        ImmutableHashSet<int> stops,
        ImmutableHashSet<int> allowed)
    {
        var reachable = ImmutableHashSet.CreateBuilder<int>();
        var pending = new Stack<int>();
        pending.Push(entry);
        while (pending.TryPop(out var node))
        {
            if (!allowed.Contains(node) || stops.Contains(node) || !reachable.Add(node))
            {
                continue;
            }
            foreach (var successor in graph.Successors[node])
            {
                pending.Push(successor);
            }
        }
        return reachable.ToImmutable();
    }
}
