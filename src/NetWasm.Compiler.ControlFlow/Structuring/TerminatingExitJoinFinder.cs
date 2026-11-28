// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// Where a loop's exits meet when some of their paths end the region instead: the nearest block outside the loop that
// every exit reaches, where every path from each exit either arrives or ends in a return, a throw or a leave out of the
// allowed blocks first, without passing back through the loop. A path that ends the region needs no join, but it
// breaks post-dominance (CommonReachableBlockFinder): a loop whose condition exit can also leave its protected block,
// and whose other exit goes where the condition exit's code goes on to, had no exit to structure after and became a
// dispatcher. The nearest blocks are tried first, at most MaximumCandidates of them.
internal sealed class TerminatingExitJoinFinder : ITerminatingExitJoinFinder
{
    private const int MaximumCandidates = 64;

    public int? Find(ControlFlowGraph graph, IReadOnlyCollection<int> exits, ImmutableHashSet<int> loop,
        ImmutableHashSet<int> allowed)
    {
        if (exits.Count < 2 || exits.Any(exit => !allowed.Contains(exit) || loop.Contains(exit))) return null;
        var distances = exits.Select(exit => Distances(graph, exit, loop, allowed)).ToArray();
        var candidates = distances[0].Keys
            .Where(block => distances.All(distance => distance.ContainsKey(block)))
            .OrderBy(block => distances.Sum(distance => distance[block]))
            .ThenBy(block => distances.Max(distance => distance[block]))
            .ThenBy(block => block)
            .Take(MaximumCandidates);
        foreach (var candidate in candidates)
        {
            if (exits.All(exit => ArrivesOrEnds(graph, exit, candidate, loop, allowed))) return candidate;
        }
        return null;
    }

    private static Dictionary<int, int> Distances(ControlFlowGraph graph, int start, ImmutableHashSet<int> loop,
        ImmutableHashSet<int> allowed)
    {
        var distances = new Dictionary<int, int> { [start] = 0 };
        var pending = new Queue<int>([start]);
        while (pending.TryDequeue(out var block))
        {
            foreach (var successor in graph.Successors[block])
            {
                if (!allowed.Contains(successor) || loop.Contains(successor) || distances.ContainsKey(successor))
                    continue;
                distances[successor] = distances[block] + 1;
                pending.Enqueue(successor);
            }
        }
        return distances;
    }

    // Every path from the exit reaches the join, or ends the region before it, without re-entering the loop.
    private static bool ArrivesOrEnds(ControlFlowGraph graph, int exit, int join, ImmutableHashSet<int> loop,
        ImmutableHashSet<int> allowed)
    {
        if (exit == join) return true;
        var seen = new HashSet<int> { exit };
        var pending = new Stack<int>([exit]);
        while (pending.TryPop(out var block))
        {
            if (loop.Contains(block)) return false;
            var successors = graph.Successors[block];
            if (successors.IsEmpty &&
                graph.GetBlock(block).Terminator.Operation is not (CilOperation.Return or CilOperation.Throw or
                    CilOperation.Rethrow))
            {
                return false;
            }
            foreach (var successor in successors)
            {
                if (successor == join) continue;
                if (!allowed.Contains(successor))
                {
                    if (graph.GetBlock(block).Terminator.Operation != CilOperation.Leave) return false;
                    continue;
                }
                if (seen.Add(successor)) pending.Push(successor);
            }
        }
        return true;
    }
}
