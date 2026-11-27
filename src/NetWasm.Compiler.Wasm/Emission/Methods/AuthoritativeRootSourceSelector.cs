// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Arrays;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// An authoritative variable's slot is written wherever the variable is stored, so no safepoint publishes it; any other
// is published where a collection can happen, except where its slot still holds its value (the value elisions) or
// still holds null at a point that collects only by throwing (a deferred root). The variable is authoritative where
// its stores cost less than the publications the clear and value elisions planned with none authoritative leave: a
// constructor call's publication, the first publication after a join or handler entry the planner knows nothing of,
// one inside a loop the planner cannot carry around the back edge. A store or publication inside a cycle counts as
// LoopWeight of one outside, and an argument's slot is also written on entry; on a tie the variable is published, since
// a store runs on every path and a publication only on one that may collect. A byref argument stays authoritative: it
// keeps one value for the call unless stored, and a pointer is not tracked to skip its republication, which a value
// type's instance methods and async MoveNext would otherwise pay at every safepoint for their receiver.
internal sealed class AuthoritativeRootSourceSelector(IRootClearElisionPlanner clears, IRootValueElisionPlanner values) :
    IAuthoritativeRootSourceSelector
{
    internal const int LoopWeight = 8;

    private readonly IRootClearElisionPlanner _clears = clears ?? throw new ArgumentNullException(nameof(clears));
    private readonly IRootValueElisionPlanner _values = values ?? throw new ArgumentNullException(nameof(values));

    public ImmutableHashSet<RootSource> Select(StructuredMethod method, MethodEmissionContext context, ArrayMetadataCachingPlan arrays)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arrays);
        var roots = context.RootMap;
        var layout = context.ValueLayout;
        var filters = context.FilterEnvironment;
        var candidates = roots.Slots.Keys.Where(source => !source.IsIndirect &&
            source.Kind is RootSourceKind.Argument or RootSourceKind.Local or RootSourceKind.ManagedAddressArgument &&
            !filters.Captures.ContainsKey(new(source.Kind != RootSourceKind.Local, source.Index)) &&
            !arrays.Slots.Contains(new(source.Kind != RootSourceKind.Local, source.Index)) &&
            (source.Kind == RootSourceKind.Local
                ? !layout.SpilledScalarLocals.Contains(source.Index)
                : !layout.ArgumentOffsets.ContainsKey(source.Index))).ToImmutableHashSet();
        if (candidates.All(static source => source.Kind == RootSourceKind.ManagedAddressArgument)) return candidates;

        var unpublished = context with
        {
            AuthoritativeRootSlots = ImmutableDictionary<RootSource, int>.Empty,
            RootFrameStartsZeroed = true,
        };
        var zero = _clears.Plan(method, unpublished);
        var unchanged = _values.Plan(method, unpublished with { RootClearElisions = zero });
        var cyclic = StructuredCycles.Offsets(method);
        int Weight(int offset) => cyclic.Contains(offset) ? LoopWeight : 1;

        var publications = new Dictionary<RootSource, int>();
        foreach (var (offset, point) in roots.Safepoints)
        {
            zero.TryGetValue((offset, false), out var known);
            var deferred = DeferredRoots.Of(roots, point, known);
            unchanged.TryGetValue(offset, out var skipped);
            foreach (var source in point.Roots)
            {
                if (candidates.Contains(source) && !deferred.Contains(source) && skipped?.Contains(roots.Slots[source]) != true)
                    publications[source] = publications.GetValueOrDefault(source) + Weight(offset);
            }
            foreach (var source in point.ConstructorCallRoots)
            {
                if (candidates.Contains(source)) publications[source] = publications.GetValueOrDefault(source) + Weight(offset);
            }
        }
        var stores = candidates.Where(static source => source.Kind == RootSourceKind.Argument)
            .ToDictionary(static source => source, static _ => 1);
        foreach (var block in method.Blocks.Values)
        {
            foreach (var instruction in block.Instructions)
            {
                RootSource? stored = instruction.Operation switch
                {
                    CilOperation.StoreLocal => new(RootSourceKind.Local, CilOperandReader.GetIndex(instruction)),
                    CilOperation.StoreArgument => new(RootSourceKind.Argument, CilOperandReader.GetIndex(instruction)),
                    _ => null,
                };
                if (stored is { } source && candidates.Contains(source))
                    stores[source] = stores.GetValueOrDefault(source) + Weight(instruction.Offset);
            }
        }
        return candidates.Where(source => source.Kind == RootSourceKind.ManagedAddressArgument ||
            stores.GetValueOrDefault(source) < publications.GetValueOrDefault(source)).ToImmutableHashSet();
    }
}
