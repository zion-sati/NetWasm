// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// A forward must-analysis of root-frame facts over a method's blocks. The fact at a block's entry is
// the meet of the exits of its ordinary predecessors that some path reaches, and the empty fact at
// an opaque block (the method's entry among them). Starting from the opaque blocks, exits only
// shrink, so the iteration stops at the greatest fixpoint; a block no path reaches gets the empty
// fact. The method's entry block may start with a fact of its own (what the frame's entry
// establishes) when no ordinary edge returns to it.
internal static class RootFactFlow
{
    public static ImmutableDictionary<StructuredBlockId, TFact> Solve<TFact>(
        StructuredMethod method,
        StructuredBlockPredecessors flow,
        TFact empty,
        Func<TFact, TFact, TFact> meet,
        Func<TFact, TFact, bool> equal,
        Func<StructuredBlockDefinition, TFact, TFact> transfer) =>
        Solve(method, flow, empty, empty, meet, equal, transfer);

    public static ImmutableDictionary<StructuredBlockId, TFact> Solve<TFact>(
        StructuredMethod method,
        StructuredBlockPredecessors flow,
        TFact empty,
        TFact methodEntry,
        Func<TFact, TFact, TFact> meet,
        Func<TFact, TFact, bool> equal,
        Func<StructuredBlockDefinition, TFact, TFact> transfer)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(flow);
        var exits = new Dictionary<StructuredBlockId, TFact>();
        var successors = flow.Predecessors
            .SelectMany(pair => pair.Value.Select(predecessor => (Predecessor: predecessor, Block: pair.Key)))
            .ToLookup(edge => edge.Predecessor, edge => edge.Block);
        var roots = method.Blocks.Keys.Where(flow.Opaque.Contains).OrderBy(id => id.Value).ToArray();
        // Blocks are taken in reverse postorder, so a block whose predecessors are all ahead of it meets them once
        // they are done; first in, first out, a join of n arms would meet its reached predecessors once per arm. The
        // greatest fixpoint does not depend on the order.
        var order = ReversePostorder(roots, successors);
        var pending = new PriorityQueue<StructuredBlockId, int>();
        var queued = new HashSet<StructuredBlockId>();
        foreach (var id in roots)
        {
            pending.Enqueue(id, order[id]);
            queued.Add(id);
        }
        while (pending.TryDequeue(out var id, out _))
        {
            queued.Remove(id);
            var exit = transfer(method.Blocks[id], Entry(id));
            if (exits.TryGetValue(id, out var previous) && equal(previous, exit)) continue;
            exits[id] = exit;
            foreach (var successor in successors[id])
            {
                if (queued.Add(successor)) pending.Enqueue(successor, order[successor]);
            }
        }
        return method.Blocks.Keys.ToImmutableDictionary(id => id, id => exits.ContainsKey(id) ? Entry(id) : empty);

        TFact Entry(StructuredBlockId id)
        {
            if (flow.Opaque.Contains(id))
                return id == method.EntryBlock && flow.Predecessors[id].IsEmpty ? methodEntry : empty;
            var reached = flow.Predecessors[id].Where(exits.ContainsKey).ToArray();
            // A block is queued only once a predecessor is reached.
            return reached.Skip(1).Aggregate(exits[reached[0]], (fact, predecessor) => meet(fact, exits[predecessor]));
        }
    }

    // Each block the roots reach, numbered in reverse postorder of a depth-first walk from the roots in turn.
    private static Dictionary<StructuredBlockId, int> ReversePostorder(
        StructuredBlockId[] roots, ILookup<StructuredBlockId, StructuredBlockId> successors)
    {
        var postorder = new List<StructuredBlockId>();
        var visited = new HashSet<StructuredBlockId>();
        var stack = new Stack<(StructuredBlockId Block, IEnumerator<StructuredBlockId> Successors)>();
        foreach (var root in roots)
        {
            if (!visited.Add(root)) continue;
            stack.Push((root, successors[root].GetEnumerator()));
            while (stack.TryPeek(out var top))
            {
                if (top.Successors.MoveNext())
                {
                    var next = top.Successors.Current;
                    if (visited.Add(next)) stack.Push((next, successors[next].GetEnumerator()));
                    continue;
                }
                stack.Pop();
                postorder.Add(top.Block);
            }
        }
        var order = new Dictionary<StructuredBlockId, int>(postorder.Count);
        for (var index = 0; index < postorder.Count; index++)
        {
            order[postorder[index]] = postorder.Count - 1 - index;
        }
        return order;
    }
}
