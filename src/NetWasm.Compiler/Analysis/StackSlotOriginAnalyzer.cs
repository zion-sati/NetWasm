// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IStackSlotOriginAnalyzer
{
    StackSlotOrigins Analyze(ValidatedControlFlowGraph controlFlow);
}

// Where each stack value came from, across the branches that join before it is used, as an argument computed with
// ?. or ?? joins before the call that takes the arguments pushed ahead of it.
internal sealed class StackSlotOrigins(
    ImmutableDictionary<int, ImmutableArray<int?>> producers,
    ImmutableDictionary<int, ImmutableHashSet<int>> consumers,
    ImmutableHashSet<int> merged)
{
    // The index of the instruction that pushed the value in `slot`, counted from the bottom of the entry stack of the
    // instruction at `offset`, when every path to it agrees.
    internal int? Producer(int offset, int slot) =>
        producers.TryGetValue(offset, out var stack) && slot < stack.Length ? stack[slot] : null;

    // Whether every instruction that reads the value the producer pushed is the consumer: no other pops it, dup
    // copies it, or path meets it with another value. Leaving it on a stack that a throw or leave empties reads it.
    internal bool IsOnlyReadBy(int producer, int consumer) =>
        !merged.Contains(producer) &&
        consumers.TryGetValue(producer, out var readers) && readers.Count == 1 && readers.Contains(consumer);

    // The instructions that read what the producer pushed, when no path meets it with another value: dup's two
    // copies are both its own, read where each is consumed.
    internal ImmutableHashSet<int> ReadersOf(int producer) =>
        !merged.Contains(producer) && consumers.TryGetValue(producer, out var readers) ? readers : [];
}

// Strategy: a forward analysis of the instruction that pushed each stack value, over the graph's blocks as
// StaticReferenceTypeAnalyzer runs, with every handler and filter starting from values of unknown origin. Where
// paths meet with different producers the slot is unknown and both producers count as read elsewhere. Each slot
// changes once at most, from one producer to unknown, so a block is analyzed a bounded number of times.
internal sealed class StackSlotOriginAnalyzer(ICalledMethodResolver calls) : IStackSlotOriginAnalyzer
{
    public StackSlotOrigins Analyze(ValidatedControlFlowGraph controlFlow)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        var graph = controlFlow.Graph;
        var body = graph.MethodBody;
        var indices = new Dictionary<int, int>(body.Instructions.Length);
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            indices.Add(body.Instructions[index].Offset, index);
        }
        var entries = new Dictionary<int, int?[]>();
        var pending = new Queue<int>();
        var producers = new Dictionary<int, ImmutableArray<int?>>();
        var consumers = new Dictionary<int, HashSet<int>>();
        var merged = new HashSet<int>();
        Merge(graph.Entry.Index, []);
        foreach (var region in body.ExceptionRegions)
        {
            Enter(region.HandlerOffset);
            if (region.FilterOffset is { } filter)
            {
                Enter(filter);
            }
        }
        while (pending.TryDequeue(out var blockIndex))
        {
            var block = graph.GetBlock(blockIndex);
            var stack = entries[blockIndex].ToList();
            for (var position = 0; position < block.Instructions.Length; position++)
            {
                var instruction = block.Instructions[position];
                var index = indices[instruction.Offset];
                producers[instruction.Offset] = [.. stack];
                var outgoing = position + 1 < block.Instructions.Length
                    ? controlFlow.InstructionEntryStacks[block.Instructions[position + 1].Offset].Length
                    : graph.Successors[blockIndex].IsEmpty
                        ? 0
                        : controlFlow.EntryStacks[graph.Successors[blockIndex][0]].Length;
                Transfer(instruction, index, outgoing, stack, consumers);
            }
            foreach (var successor in graph.Successors[blockIndex])
            {
                Merge(successor, [.. stack]);
            }
        }
        return new StackSlotOrigins(
            producers.ToImmutableDictionary(),
            consumers.ToImmutableDictionary(static pair => pair.Key, static pair => pair.Value.ToImmutableHashSet()),
            [.. merged]);

        void Enter(int offset)
        {
            var handler = graph.GetBlockAtOffset(offset).Index;
            if (graph.ReachableBlocks.Contains(handler))
            {
                Merge(handler, new int?[controlFlow.EntryStacks[handler].Length]);
            }
        }

        void Merge(int block, int?[] incoming)
        {
            if (!entries.TryGetValue(block, out var existing))
            {
                entries.Add(block, incoming);
                pending.Enqueue(block);
                return;
            }
            var changed = false;
            for (var slot = 0; slot < existing.Length && slot < incoming.Length; slot++)
            {
                if (existing[slot] == incoming[slot])
                {
                    continue;
                }
                if (existing[slot] is { } kept)
                {
                    merged.Add(kept);
                    existing[slot] = null;
                    changed = true;
                }
                if (incoming[slot] is { } other)
                {
                    merged.Add(other);
                }
            }
            if (changed)
            {
                pending.Enqueue(block);
            }
        }
    }

    // The instruction pushes one value or none (two for dup), and reads every value the stack after it lacks: what it
    // pops, and what a throw or leave discards.
    private void Transfer(
        CilInstruction instruction, int index, int outgoing, List<int?> stack, Dictionary<int, HashSet<int>> consumers)
    {
        var pushed = instruction.Operation == CilOperation.Duplicate ? 2 : StackPushes.Of(instruction, calls);
        var remaining = Math.Max(0, outgoing - pushed);
        for (var slot = remaining; slot < stack.Count; slot++)
        {
            if (stack[slot] is { } producer)
            {
                if (!consumers.TryGetValue(producer, out var readers))
                {
                    consumers.Add(producer, readers = []);
                }
                readers.Add(index);
            }
        }
        stack.RemoveRange(remaining, stack.Count - remaining);
        while (stack.Count < outgoing)
        {
            stack.Add(index);
        }
    }
}
