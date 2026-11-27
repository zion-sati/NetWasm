// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// A method that enters its root frame lazily: its frame local is 0 until the first safepoint that publishes a root,
// which enters (and zeroes) the frame there. EagerGroups are the protected blocks that enter it before they begin,
// since one of their handlers, or the code a handler flows into, reaches a safepoint that publishes: a landing pad
// rewinds the arenas to the block's marks, which would pop a frame entered inside the block, slots and all. Entered
// holds the instruction offsets where every path has already entered the frame, which need neither its entry nor a
// test of it.
internal sealed record LazyRootFramePlan(
    ImmutableHashSet<StructuredExceptionGroupId> EagerGroups,
    ImmutableHashSet<int> Entered);

internal interface ILazyRootFramePlanner
{
    // Null when the method enters its root frame on entry.
    LazyRootFramePlan? Plan(StructuredMethod method, MethodEmissionContext context);
}

// A frame entered on entry costs every call its entry, zeroing and leave even where the path taken never publishes a
// root (an async method's direct call that completes, whose frame only its suspension, resumption and fault paths use).
// Lazy entry costs a test where a root is live at a safepoint, so the method takes it only when some path from entry to a
// return has none. An authoritative slot (written where its variable is stored) is written only in an entered frame,
// whose entry copies every authoritative variable in. The method keeps entry for a filter environment (whose funclets
// publish into the parent's frame) and a frame placed in a branch arm.
internal sealed class LazyRootFramePlanner(IRootClearElisionPlanner clears, IFallbackPublicationClassifier fallbacks) :
    ILazyRootFramePlanner
{
    private readonly IRootClearElisionPlanner _clears = clears ?? throw new ArgumentNullException(nameof(clears));
    private readonly IFallbackPublicationClassifier _fallbacks = fallbacks ?? throw new ArgumentNullException(nameof(fallbacks));

    public LazyRootFramePlan? Plan(StructuredMethod method, MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(context);
        if (context.RootSlotCount == 0 || !context.FilterEnvironment.IsEmpty ||
            context.TerminalBranchValueFramePlacement is not null || context.IsFilterFunclet || method.Blocks.IsEmpty)
        {
            return null;
        }

        // The blocks of each instruction offset, the exit's (a throw, a branch, a leave, a condition) among them: a block
        // copied where a tail is duplicated shares its offsets with the original.
        var blocksOf = new Dictionary<int, List<StructuredBlockId>>();
        foreach (var (id, block) in method.Blocks)
        {
            foreach (var offset in block.Instructions.Select(static instruction => instruction.Offset).Concat(StructuredBlockEdges.ExitOffset(block.Exit)))
            {
                if (!blocksOf.TryGetValue(offset, out var list)) blocksOf[offset] = list = [];
                list.Add(id);
            }
        }
        // A lazy frame starts zeroed, so its clear elisions, and the roots a throw-only point leaves unpublished where
        // their slot holds null, are those of a zeroed frame: such a point publishes nothing.
        var zeroed = _clears.Plan(method, context with { RootFrameStartsZeroed = true });
        var publishing = new HashSet<StructuredBlockId>();
        var publishingOffsets = new HashSet<int>();
        // A publication on an allocation's fallback enters the frame itself; the path through the allocation does not.
        var fallbacks = _fallbacks.Classify(method, context);
        foreach (var (offset, point) in context.RootMap.Safepoints)
        {
            zeroed.TryGetValue((offset, false), out var knownZero);
            var deferred = DeferredRoots.Of(context.RootMap, point, knownZero);
            if (point.Roots.All(deferred.Contains) && point.ConstructorCallRoots.IsEmpty) continue;
            if (fallbacks.Contains(offset)) continue;
            if (!blocksOf.TryGetValue(offset, out var ids)) return null;
            publishing.UnionWith(ids);
            publishingOffsets.Add(offset);
        }

        // Each block's handlers: the entries of the protected blocks it lies in.
        var handlers = new Dictionary<StructuredBlockId, List<StructuredBlockId>>();
        var entries = new Dictionary<StructuredExceptionGroupId, List<StructuredBlockId>>();
        foreach (var (id, group) in method.ExceptionGroups)
        {
            var groupEntries = group.Clauses.Select(static clause => clause.HandlerBlock).ToList();
            entries[id] = groupEntries;
            foreach (var block in group.ProtectedBlocks)
            {
                if (!handlers.TryGetValue(block, out var list)) handlers[block] = list = [];
                list.AddRange(groupEntries);
            }
        }

        // Some path from entry to a return publishes nothing.
        if (!Reaches(method, [method.EntryBlock], publishing, handlers: null, block =>
                method.Blocks[block].Exit is StructuredTerminalExit { Instruction.Operation: CilOperation.Return }))
        {
            return null;
        }

        var eager = ImmutableHashSet.CreateBuilder<StructuredExceptionGroupId>();
        foreach (var (id, groupEntries) in entries)
        {
            if (Reaches(method, groupEntries, avoiding: null, handlers, publishing.Contains)) eager.Add(id);
        }
        var eagerGroups = eager.ToImmutable();
        return new(eagerGroups, Entered(method, publishing, publishingOffsets, entries, eagerGroups));
    }

    // Where every path has entered the frame: a forward analysis over blocks, entered at a block's end when entered at
    // its start or when it publishes, and at its start when entered at the end of every predecessor. A method's entry
    // has no frame; a handler's entry has one only when its block is eager (its pad restores the local the block saved
    // after entering the frame), whatever the throw point held. A tail copy shares its offsets with the original, so an
    // offset is entered only when it is in every copy.
    private static ImmutableHashSet<int> Entered(
        StructuredMethod method,
        HashSet<StructuredBlockId> publishing,
        HashSet<int> publishingOffsets,
        Dictionary<StructuredExceptionGroupId, List<StructuredBlockId>> entries,
        ImmutableHashSet<StructuredExceptionGroupId> eager)
    {
        var predecessors = method.Blocks.Keys.ToDictionary(static id => id, static _ => new List<StructuredBlockId>());
        foreach (var (id, block) in method.Blocks)
        {
            foreach (var next in StructuredBlockEdges.Successors(block.Exit))
            {
                if (predecessors.TryGetValue(next, out var list)) list.Add(id);
            }
        }
        var fixedStart = new Dictionary<StructuredBlockId, bool> { [method.EntryBlock] = false };
        foreach (var (id, groupEntries) in entries)
        {
            foreach (var entry in groupEntries)
            {
                fixedStart[entry] = (!fixedStart.TryGetValue(entry, out var known) || known) && eager.Contains(id);
            }
        }
        // Optimistic start, lowered until stable: a block no path reaches keeps true, which nothing executes.
        var atStart = method.Blocks.Keys.ToDictionary(static id => id, id => fixedStart.GetValueOrDefault(id, true));
        var pending = new Queue<StructuredBlockId>(method.Blocks.Keys.OrderBy(static id => id.Value));
        var queued = new HashSet<StructuredBlockId>(pending);
        while (pending.TryDequeue(out var block))
        {
            queued.Remove(block);
            var atEnd = atStart[block] || publishing.Contains(block);
            foreach (var next in StructuredBlockEdges.Successors(method.Blocks[block].Exit))
            {
                if (!atStart.TryGetValue(next, out var known) || !known || atEnd) continue;
                atStart[next] = false;
                if (queued.Add(next)) pending.Enqueue(next);
            }
        }

        var offsets = new Dictionary<int, bool>();
        foreach (var (id, block) in method.Blocks)
        {
            var entered = atStart[id];
            foreach (var offset in block.Instructions.Select(static instruction => instruction.Offset).Concat(StructuredBlockEdges.ExitOffset(block.Exit)))
            {
                offsets[offset] = (!offsets.TryGetValue(offset, out var known) || known) && entered;
                if (publishingOffsets.Contains(offset)) entered = true;
            }
        }
        return offsets.Where(static pair => pair.Value).Select(static pair => pair.Key).ToImmutableHashSet();
    }

    // Whether a block the goal accepts is reachable from the starts, through no block avoided (a start included), along
    // normal successors and, when handlers are given, the handlers of the protected blocks a block lies in.
    private static bool Reaches(
        StructuredMethod method,
        IEnumerable<StructuredBlockId> starts,
        HashSet<StructuredBlockId>? avoiding,
        Dictionary<StructuredBlockId, List<StructuredBlockId>>? handlers,
        Func<StructuredBlockId, bool> goal)
    {
        var seen = new HashSet<StructuredBlockId>();
        var pending = new Stack<StructuredBlockId>();
        foreach (var start in starts)
        {
            if (avoiding?.Contains(start) != true && seen.Add(start)) pending.Push(start);
        }
        while (pending.TryPop(out var block))
        {
            if (goal(block)) return true;
            foreach (var next in StructuredBlockEdges.Successors(method.Blocks[block].Exit))
            {
                if (avoiding?.Contains(next) != true && seen.Add(next)) pending.Push(next);
            }
            if (handlers is not null && handlers.TryGetValue(block, out var entries))
            {
                foreach (var next in entries)
                {
                    if (seen.Add(next)) pending.Push(next);
                }
            }
        }
        return false;
    }
}
