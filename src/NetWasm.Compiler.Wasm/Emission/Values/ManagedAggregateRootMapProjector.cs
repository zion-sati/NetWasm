// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal sealed class ManagedAggregateRootMapProjector(
    IRootSlotAllocator rootSlots) : IManagedAggregateRootMapProjector
{
    private readonly IRootSlotAllocator _rootSlots = rootSlots ??
        throw new ArgumentNullException(nameof(rootSlots));

    public bool TryProject(
        ManagedAggregateRootMapProjectionRequest request,
        out MethodRootMap rootMap)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RootMap);
        ArgumentNullException.ThrowIfNull(request.Plan);
        rootMap = request.RootMap;
        if (request.Plan.RootDependencies.IsEmpty)
        {
            return true;
        }

        var removals = new HashSet<(int Offset, RootSource Source, ManagedAggregateRootSetKind Set)>();
        foreach (var dependency in request.Plan.RootDependencies)
        {
            if (!request.Plan.Storages.TryGetValue(
                    dependency.Address.Storage,
                    out var storage) ||
                !storage.Owned ||
                dependency.Source.IsIndirect ||
                dependency.Source.Kind is not (
                    RootSourceKind.ManagedAddressLocal or
                    RootSourceKind.ManagedAddressEvaluationStack) ||
                !request.RootMap.Safepoints.TryGetValue(
                    dependency.SafepointOffset,
                    out var safepoint))
            {
                return false;
            }
            var occurrence = (
                dependency.SafepointOffset,
                dependency.Source,
                dependency.Set);
            var sources = dependency.Set == ManagedAggregateRootSetKind.Ordinary
                ? safepoint.Roots
                : safepoint.ConstructorCallRoots;
            if (sources.Count(source => source == dependency.Source) != 1 ||
                !removals.Add(occurrence))
            {
                return false;
            }
        }

        var safepoints = ImmutableDictionary.CreateBuilder<int, SafepointRootMap>();
        foreach ((var offset, var safepoint) in request.RootMap.Safepoints)
        {
            var roots = Filter(
                safepoint.Roots,
                offset,
                ManagedAggregateRootSetKind.Ordinary,
                removals);
            var constructorRoots = Filter(
                safepoint.ConstructorCallRoots,
                offset,
                ManagedAggregateRootSetKind.Constructor,
                removals);
            safepoints.Add(offset, safepoint with
            {
                Roots = roots,
                ConstructorCallRoots = constructorRoots,
            });
        }

        var projectedSafepoints = safepoints.ToImmutable();
        rootMap = request.RootMap with
        {
            Slots = _rootSlots.Allocate(
                [.. projectedSafepoints.Values.OrderBy(item => item.IlOffset)]),
            Safepoints = projectedSafepoints,
        };
        return true;
    }

    private static ImmutableArray<RootSource> Filter(
        ImmutableArray<RootSource> sources,
        int safepointOffset,
        ManagedAggregateRootSetKind set,
        HashSet<(int Offset, RootSource Source, ManagedAggregateRootSetKind Set)> removals)
    {
        var projected = ImmutableArray.CreateBuilder<RootSource>(sources.Length);
        foreach (var source in sources)
        {
            var occurrence = (safepointOffset, source, set);
            if (removals.Contains(occurrence))
            {
                continue;
            }
            projected.Add(source);
        }
        return projected.ToImmutable();
    }

}
