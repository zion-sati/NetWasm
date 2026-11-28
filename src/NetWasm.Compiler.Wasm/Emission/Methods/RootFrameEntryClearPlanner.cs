// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IRootFrameEntryClearPlanner
{
    bool Plan(StructuredMethod method, MethodEmissionContext context);
}

// Whether a method zeroes its root frame on entry. Every slot must hold a value or null before a
// collection reads the frame, so a safepoint stores null into each slot it does not publish and
// cannot prove still null. Zeroing the frame on entry makes every slot known null there, so those
// stores remain only where a slot's value dies. The method zeroes its frame when that removes more
// stores than the zeroing writes (counting the entry's own null stores it replaces: authoritative
// locals'). A frame entered inside a terminal arm is not zeroed here.
internal sealed class RootFrameEntryClearPlanner(IRootClearElisionPlanner clears, ITargetLayout layouts) :
    IRootFrameEntryClearPlanner
{
    public bool Plan(StructuredMethod method, MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(context);
        if (context.RootMap.SlotCount == 0 ||
            context.TerminalBranchValueFramePlacement is { Plan.DefersRootFrame: true })
            return false;
        var unzeroed = NullStores(context, clears.Plan(method, context with { RootFrameStartsZeroed = false }));
        var zeroed = NullStores(context, clears.Plan(method, context with { RootFrameStartsZeroed = true }));
        var replaced = context.AuthoritativeRootSlots.Keys.Count(source => source.Kind == RootSourceKind.Local);
        var zeroing = FrameSlotZeroing.Writes(layouts.Target, context.RootSlotCount, ArgumentSlots(context));
        return unzeroed - zeroed + replaced > zeroing;
    }

    // The slots an entry fills with authoritative arguments, which its zeroing skips.
    internal static IReadOnlySet<int> ArgumentSlots(MethodEmissionContext context) => context.AuthoritativeRootSlots
        .Where(static pair => pair.Key.Kind != RootSourceKind.Local)
        .Select(static pair => pair.Value)
        .ToHashSet();

    // The null stores the method's safepoints emit under these clear elisions, and the stores of deferrable roots they
    // leave unpublished only where a slot is known null.
    private static int NullStores(
        MethodEmissionContext context,
        ImmutableDictionary<(int Offset, bool ConstructorCall), ImmutableHashSet<int>> elisions)
    {
        var roots = context.RootMap;
        var stores = 0;
        foreach (var (offset, point) in roots.Safepoints)
        {
            stores += Inactive(point.Roots, (offset, false));
            elisions.TryGetValue((offset, false), out var knownZero);
            var deferred = DeferredRoots.Of(roots, point, knownZero);
            stores += point.Deferrable.Count(source => point.Roots.Contains(source) && !deferred.Contains(source));
            if (!point.ConstructorCallRoots.IsEmpty) stores += Inactive(point.ConstructorCallRoots, (offset, true));
        }
        return stores;

        int Inactive(ImmutableArray<RootSource> live, (int, bool) site)
        {
            var occupied = live.Select(source => roots.Slots[source]).ToHashSet();
            elisions.TryGetValue(site, out var knownNull);
            return Enumerable.Range(0, roots.SlotCount).Count(slot =>
                !occupied.Contains(slot) && knownNull?.Contains(slot) != true);
        }
    }
}
