// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

// Strategy: finite backward demand closes over declarative normal-success
// transfers. Physical storage and publication are separate emission concerns.
public sealed class StackStorageAnchorPlanner(IStackStorageContinuationAnalyzer continuations) : IStackStorageAnchorPlanner
{
    private readonly IStackStorageContinuationAnalyzer _continuations =
        continuations ?? throw new ArgumentNullException(nameof(continuations));

    public StackStorageAnchorPlan Plan(StackStorageAnchorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        ArgumentNullException.ThrowIfNull(request.Storage);
        ArgumentNullException.ThrowIfNull(request.Demands);
        var method = request.Method;
        var graph = method.ControlFlow.Graph;
        foreach (var (offset, demands) in request.Demands)
        {
            ArgumentNullException.ThrowIfNull(demands);
            if (!method.ControlFlow.InstructionEntryStacks.TryGetValue(offset, out var entry))
                throw new ArgumentException("Anchor demand has no validated instruction entry.", nameof(request));
            foreach (var anchor in demands)
            {
                ArgumentNullException.ThrowIfNull(anchor.BackingType);
                if (anchor.BackingType.ContainsGenericParameters)
                    throw new ArgumentException("Anchor backing type must be closed.", nameof(request));
                var valid = anchor.Location.Kind switch
                {
                    StackStorageLocationKind.EvaluationStack => (uint)anchor.Location.Index < (uint)entry.Length,
                    StackStorageLocationKind.Local => (uint)anchor.Location.Index < (uint)method.Body.Locals.Length &&
                        method.Body.Locals[anchor.Location.Index] == CliValueKind.ManagedAddress,
                    _ => false,
                };
                if (!valid) throw new ArgumentException("Anchor demand has an invalid storage location.", nameof(request));
                StackStorageType? descriptor = anchor.Location.Kind == StackStorageLocationKind.EvaluationStack
                    ? request.Storage.Entries.TryGetValue(offset, out var stack) && anchor.Location.Index < stack.Length
                        ? stack[anchor.Location.Index] : null
                    : request.Storage.Locals.TryGetValue(offset, out var locals) && anchor.Location.Index < locals.Length
                        ? locals[anchor.Location.Index] : null;
                if (descriptor is not { CompleteBackingKnown: true } known || !known.CapturedBackings.Contains(anchor.BackingType))
                    throw new ArgumentException("Anchor demand has no complete captured backing evidence.", nameof(request));
            }
        }
        if (request.Demands.Values.All(demands => demands.IsEmpty)) return StackStorageAnchorPlan.Empty;

        foreach (var transfer in request.Storage.Transfers.Values)
        {
            if (!transfer.Captures.IsEmpty && transfer.Destination.Kind != StackStorageLocationKind.EvaluationStack)
                throw new ArgumentException("A captured base must come from an evaluation-stack result.", nameof(request));
        }

        var flow = _continuations.Analyze(method);
        var blocks = graph.Blocks.Where(block => graph.ReachableBlocks.Contains(block.Index))
            .OrderByDescending(block => block.Index).ToArray();
        var liveIn = graph.Blocks.ToDictionary(block => block.Index, _ => ImmutableHashSet<StackStorageAnchor>.Empty);
        var after = new Dictionary<int, ImmutableHashSet<StackStorageAnchor>>();
        var needed = new HashSet<StackStorageAnchor>();
        bool changed;
        do
        {
            changed = false;
            foreach (var block in blocks)
            {
                var live = Follow(flow.Normal[block.Index]);
                for (var index = block.Instructions.Length - 1; index >= 0; index--)
                {
                    var instruction = block.Instructions[index];
                    after[instruction.Offset] = live;
                    needed.UnionWith(live);
                    if (request.Storage.Transfers.TryGetValue(instruction.Offset, out var transfer))
                    {
                        var replaced = live.Where(anchor => anchor.Location == transfer.Destination).ToArray();
                        live = live.Except(replaced);
                        foreach (var anchor in replaced)
                        {
                            if (transfer.Copies.TryGetValue(anchor.BackingType, out var source))
                                live = live.Add(new(source, anchor.BackingType));
                            // Capture terminates backward demand. A missing
                            // active variant also terminates, as inactive zero.
                        }
                    }
                    if (CilSafepointClassifier.MayTransferControlExceptionally(instruction))
                        live = live.Union(Follow(flow.Exceptional[block.Index]));
                    if (request.Demands.TryGetValue(instruction.Offset, out var demands)) live = live.Union(demands);
                    needed.UnionWith(live);
                }
                if (!liveIn[block.Index].SetEquals(live))
                {
                    liveIn[block.Index] = live;
                    changed = true;
                }
            }
        }
        while (changed);

        var anchors = needed.OrderBy(anchor => anchor.Location.Kind).ThenBy(anchor => anchor.Location.Index)
            .ThenBy(anchor => anchor.BackingType.CanonicalName, StringComparer.Ordinal).ToImmutableArray();
        var slots = anchors.Select((anchor, index) => (anchor, index)).ToDictionary(item => item.anchor, item => item.index);
        var byLocation = anchors.GroupBy(anchor => anchor.Location).ToDictionary(group => group.Key, group => group.ToArray());
        var assignments = ImmutableDictionary.CreateBuilder<int, ImmutableArray<StackStorageAnchorAssignment>>();
        foreach (var (offset, transfer) in request.Storage.Transfers.OrderBy(pair => pair.Key))
        {
            if (!after.TryGetValue(offset, out var live) || !byLocation.TryGetValue(transfer.Destination, out var variants)) continue;
            var updates = ImmutableArray.CreateBuilder<StackStorageAnchorAssignment>();
            foreach (var anchor in variants)
            {
                var destination = slots[anchor];
                if (live.Contains(anchor) && transfer.Copies.TryGetValue(anchor.BackingType, out var source))
                    updates.Add(new StackStorageAnchorAssignment.Copy(destination, slots[new(source, anchor.BackingType)]));
                else if (live.Contains(anchor) && transfer.Captures.Contains(anchor.BackingType))
                    updates.Add(new StackStorageAnchorAssignment.Capture(destination, transfer.Destination.Index));
                else updates.Add(new StackStorageAnchorAssignment.Clear(destination));
            }
            assignments.Add(offset, updates.ToImmutable());
        }
        return new(anchors, assignments.ToImmutable());

        ImmutableHashSet<StackStorageAnchor> Follow(ImmutableArray<StackStorageFlowEdge> edges) =>
            edges.SelectMany(edge => liveIn[edge.Block].Where(anchor =>
                    !edge.ResetStack || anchor.Location.Kind == StackStorageLocationKind.Local))
                .ToImmutableHashSet();
    }
}
