// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// The instruction offsets, exits included, of the blocks that lie on a cycle of ordinary edges: those a loop may run
// again. Strongly connected components (Tarjan's, iteratively), so linear in blocks and edges; a block is on a cycle when
// its component has another block or it is its own successor.
internal static class StructuredCycles
{
    public static ImmutableHashSet<int> Offsets(StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var index = new Dictionary<StructuredBlockId, int>();
        var low = new Dictionary<StructuredBlockId, int>();
        var onStack = new HashSet<StructuredBlockId>();
        var stack = new Stack<StructuredBlockId>();
        var cyclic = new HashSet<StructuredBlockId>();
        var work = new Stack<(StructuredBlockId Block, IEnumerator<StructuredBlockId> Successors)>();
        foreach (var root in method.Blocks.Keys.OrderBy(static id => id.Value))
        {
            if (index.ContainsKey(root)) continue;
            Visit(root);
            while (work.TryPeek(out var top))
            {
                if (top.Successors.MoveNext())
                {
                    var next = top.Successors.Current;
                    if (!method.Blocks.ContainsKey(next)) continue;
                    if (next == top.Block) cyclic.Add(next);
                    if (!index.TryGetValue(next, out var seen)) Visit(next);
                    else if (onStack.Contains(next)) low[top.Block] = Math.Min(low[top.Block], seen);
                    continue;
                }
                work.Pop();
                if (work.TryPeek(out var parent)) low[parent.Block] = Math.Min(low[parent.Block], low[top.Block]);
                if (low[top.Block] != index[top.Block]) continue;
                var component = new List<StructuredBlockId>();
                StructuredBlockId member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    component.Add(member);
                }
                while (member != top.Block);
                if (component.Count > 1) cyclic.UnionWith(component);
            }
        }
        return cyclic
            .SelectMany(id => method.Blocks[id].Instructions.Select(static instruction => instruction.Offset)
                .Concat(StructuredBlockEdges.ExitOffset(method.Blocks[id].Exit)))
            .ToImmutableHashSet();

        void Visit(StructuredBlockId block)
        {
            index[block] = low[block] = index.Count;
            stack.Push(block);
            onStack.Add(block);
            work.Push((block, StructuredBlockEdges.Successors(method.Blocks[block].Exit).GetEnumerator()));
        }
    }
}
