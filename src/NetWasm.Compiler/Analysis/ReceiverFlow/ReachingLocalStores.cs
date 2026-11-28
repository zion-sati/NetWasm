// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.ReceiverFlow;

internal interface IReachingLocalStores
{
    // For each load of a followed local, by its offset, the offsets of the stores whose value it may read; null when
    // the method is too large to follow its locals one store at a time.
    IReadOnlyDictionary<int, ImmutableArray<int>>? Resolve(ControlFlowGraph graph, IReadOnlySet<int> locals);
}

// Reaching definitions over a body's blocks for the locals given: a load reads the stores that reach it on some path,
// not every store to its slot, so locals the optimizer gave one slot keep their own values. A handler is reached by
// every store of the blocks it protects. A filtered catch handler runs after its filter accepts: the graph has no edge
// from endfilter to it, so it is reached here by what reaches the filter's start and every store of the filter's
// blocks (the catch variable Roslyn stores in the filter among them). A local's first value is null, which adds
// nothing.
internal sealed class ReachingLocalStores : IReachingLocalStores
{
    // Stores times blocks past which a method's locals are followed by slot instead.
    internal const long MaximumBits = 1L << 24;

    public IReadOnlyDictionary<int, ImmutableArray<int>>? Resolve(ControlFlowGraph graph, IReadOnlySet<int> locals)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(locals);
        var stores = new List<(int Offset, int Local)>();
        var storeIndex = new Dictionary<int, int>();
        foreach (var block in graph.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                if (instruction is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var local } } &&
                    locals.Contains(local))
                {
                    storeIndex.Add(instruction.Offset, stores.Count);
                    stores.Add((instruction.Offset, local));
                }
            }
        }
        var count = stores.Count;
        var blocks = graph.Blocks.Length;
        if ((long)count * blocks > MaximumBits) return null;
        var words = (count + 63) / 64;
        var byLocal = new Dictionary<int, ulong[]>();
        for (var index = 0; index < count; index++)
        {
            if (!byLocal.TryGetValue(stores[index].Local, out var mask)) byLocal.Add(stores[index].Local, mask = new ulong[words]);
            mask[index >> 6] |= 1UL << index;
        }
        var generated = new ulong[blocks][];
        var killed = new ulong[blocks][];
        var anywhere = new ulong[blocks][];
        for (var block = 0; block < blocks; block++)
        {
            (generated[block], killed[block], anywhere[block]) = (new ulong[words], new ulong[words], new ulong[words]);
            foreach (var instruction in graph.Blocks[block].Instructions)
            {
                if (!storeIndex.TryGetValue(instruction.Offset, out var store)) continue;
                var mask = byLocal[stores[store].Local];
                for (var word = 0; word < words; word++)
                {
                    killed[block][word] |= mask[word];
                    generated[block][word] &= ~mask[word];
                }
                generated[block][store >> 6] |= 1UL << store;
                anywhere[block][store >> 6] |= 1UL << store;
            }
        }
        var entry = new ulong[blocks][];
        var exit = new ulong[blocks][];
        for (var block = 0; block < blocks; block++) (entry[block], exit[block]) = (new ulong[words], new ulong[words]);
        var filters = FilteredHandlers(graph);
        var pending = new Queue<int>();
        var queued = new bool[blocks];
        for (var block = 0; block < blocks; block++)
        {
            pending.Enqueue(block);
            queued[block] = true;
        }
        while (pending.TryDequeue(out var block))
        {
            queued[block] = false;
            var input = entry[block];
            var grew = false;
            foreach (var predecessor in graph.Predecessors[block]) grew |= Or(input, exit[predecessor]);
            // A handler may start after any instruction of a block it protects.
            foreach (var predecessor in graph.ExceptionalPredecessors[block])
            {
                grew |= Or(input, entry[predecessor]);
                grew |= Or(input, anywhere[predecessor]);
            }
            if (filters.ByHandler.TryGetValue(block, out var filter))
            {
                grew |= Or(input, entry[filter.Start]);
                foreach (var filterBlock in filter.Blocks) grew |= Or(input, anywhere[filterBlock]);
            }
            // The handlers of this block read its entry stores as well as its exit's.
            if (grew)
            {
                foreach (var successor in graph.ExceptionalSuccessors[block]) Enqueue(successor);
            }
            var changed = false;
            var output = exit[block];
            for (var word = 0; word < words; word++)
            {
                var value = generated[block][word] | (input[word] & ~killed[block][word]);
                if (value == output[word]) continue;
                output[word] = value;
                changed = true;
            }
            // A filter start's entry feeds its handler.
            if (grew && filters.HandlerOf.TryGetValue(block, out var fed)) Enqueue(fed);
            if (!changed) continue;
            foreach (var successor in graph.Successors[block]) Enqueue(successor);
            foreach (var successor in graph.ExceptionalSuccessors[block]) Enqueue(successor);
        }
        var result = new Dictionary<int, ImmutableArray<int>>();
        for (var block = 0; block < blocks; block++)
        {
            var last = new Dictionary<int, int>();
            foreach (var instruction in graph.Blocks[block].Instructions)
            {
                if (storeIndex.TryGetValue(instruction.Offset, out var store))
                {
                    last[stores[store].Local] = instruction.Offset;
                    continue;
                }
                if (instruction is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var local } } ||
                    !locals.Contains(local))
                {
                    continue;
                }
                if (last.TryGetValue(local, out var offset))
                {
                    result[instruction.Offset] = [offset];
                    continue;
                }
                var reaching = ImmutableArray.CreateBuilder<int>();
                if (byLocal.TryGetValue(local, out var mask))
                {
                    for (var word = 0; word < words; word++)
                    {
                        var bits = entry[block][word] & mask[word];
                        while (bits != 0)
                        {
                            var bit = System.Numerics.BitOperations.TrailingZeroCount(bits);
                            reaching.Add(stores[(word << 6) + bit].Offset);
                            bits &= bits - 1;
                        }
                    }
                }
                result[instruction.Offset] = reaching.ToImmutable();
            }
        }
        return result;

        void Enqueue(int block)
        {
            if (queued[block]) return;
            queued[block] = true;
            pending.Enqueue(block);
        }
    }

    // Each filtered catch handler's start block, with its filter's start block and the blocks of its filter (those
    // from the filter's offset to the handler's); and each filter start block's handler.
    private static (Dictionary<int, (int Start, List<int> Blocks)> ByHandler, Dictionary<int, int> HandlerOf)
        FilteredHandlers(ControlFlowGraph graph)
    {
        var byHandler = new Dictionary<int, (int Start, List<int> Blocks)>();
        var handlerOf = new Dictionary<int, int>();
        BasicBlock[]? ordered = null;
        int[]? starts = null;
        foreach (var region in graph.MethodBody.ExceptionRegions)
        {
            if (region.FilterOffset is not { } filterOffset) continue;
            // Blocks by start offset, made once, so each filter's blocks are a range found by search.
            ordered ??= [.. graph.Blocks.OrderBy(static block => block.StartOffset)];
            starts ??= [.. ordered.Select(static block => block.StartOffset)];
            var start = graph.GetBlockAtOffset(filterOffset);
            var handler = graph.GetBlockAtOffset(region.HandlerOffset);
            var filterBlocks = new List<int>();
            for (var index = Array.BinarySearch(starts, filterOffset);
                 index < ordered.Length && ordered[index].StartOffset < region.HandlerOffset;
                 index++)
            {
                filterBlocks.Add(ordered[index].Index);
            }
            byHandler[handler.Index] = (start.Index, filterBlocks);
            handlerOf[start.Index] = handler.Index;
        }
        return (byHandler, handlerOf);
    }

    private static bool Or(ulong[] target, ulong[] source)
    {
        var grew = false;
        for (var word = 0; word < target.Length; word++)
        {
            var value = target[word] | source[word];
            grew |= value != target[word];
            target[word] = value;
        }
        return grew;
    }
}
