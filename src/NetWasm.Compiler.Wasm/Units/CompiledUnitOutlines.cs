using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Units;

/// <summary>
/// Defers structuring for methods whose units are expected to be reused: the unit's input
/// matches and it recorded the method's exception groups. Planning reads such a method's
/// instructions and exception groups only, so its outline carries those; if the unit turns
/// out stale when queries are checked, the method is structured then.
/// </summary>
internal sealed class CompiledUnitOutlines(
    CompiledUnitState state,
    FrozenDictionary<string, MethodRootMap> rootMaps,
    BranchPrefixDuplicationPolicy branchPrefixDuplication,
    DispatcherEliminationPolicy dispatcherElimination) : IMethodOutlineProvider
{
    public StructuredMethod? Outline(ManagedMethodBody method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var identity = method.Method.CanonicalName;
        if (!rootMaps.TryGetValue(identity, out var rootMap))
            return null;
        ImmutableArray<UnitExceptionGroup> groups;
        // A store another process replaced or damaged costs reuse, never the compile.
        try
        {
            if (state.Store.Find(identity) is not { } location)
                return null;
            var unit = state.Store.Segments[location.Segment]!.Record(location.Offset);
            if (!unit.HasExceptionGroups || state.InputOf(identity, rootMap) is not { } input || input != unit.Input)
                return null;
            groups = unit.ExceptionGroups();
        }
        catch (InvalidDataException)
        {
            return null;
        }
        var outline = UnitExceptionGroups.Outline(method, groups, new(method, branchPrefixDuplication, dispatcherElimination));
        if (outline is not null) System.Threading.Interlocked.Increment(ref state.Outlined);
        return outline;
    }
}

internal interface IMethodOutlineProviderFactory
{
    // Outlines for the methods whose units this compile expects to reuse.
    IMethodOutlineProvider Create(
        CompiledUnitState units,
        IReadOnlyDictionary<EntityKey, ManagedMethodBody> methods,
        IReadOnlyDictionary<EntityKey, MethodRootMap> rootMaps,
        IReadOnlyDictionary<string, MethodRootMap> constructedRootMaps,
        BranchPrefixDuplicationPolicy branchPrefixDuplication,
        DispatcherEliminationPolicy dispatcherElimination);
}

internal sealed class CompiledUnitOutlinesFactory : IMethodOutlineProviderFactory
{
    public IMethodOutlineProvider Create(
        CompiledUnitState units,
        IReadOnlyDictionary<EntityKey, ManagedMethodBody> methods,
        IReadOnlyDictionary<EntityKey, MethodRootMap> rootMaps,
        IReadOnlyDictionary<string, MethodRootMap> constructedRootMaps,
        BranchPrefixDuplicationPolicy branchPrefixDuplication,
        DispatcherEliminationPolicy dispatcherElimination)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(rootMaps);
        ArgumentNullException.ThrowIfNull(constructedRootMaps);
        // Root maps by method identity, as units name methods.
        var byIdentity = new Dictionary<string, MethodRootMap>(methods.Count + constructedRootMaps.Count,
            StringComparer.Ordinal);
        foreach (var (key, method) in methods)
        {
            if (rootMaps.TryGetValue(key, out var rootMap)) byIdentity[method.Method.CanonicalName] = rootMap;
        }
        foreach (var (name, rootMap) in constructedRootMaps) byIdentity[name] = rootMap;
        return new CompiledUnitOutlines(units, byIdentity.ToFrozenDictionary(StringComparer.Ordinal),
            branchPrefixDuplication, dispatcherElimination);
    }
}

/// <summary>A method's exception groups as units keep them.</summary>
internal static class UnitExceptionGroups
{
    private static readonly StructuredSequence Empty = new([]);

    // The groups in the order planning reads them, each clause named by its body exception
    // region; null when a group has a filter or a clause that is not one region.
    internal static ImmutableArray<UnitExceptionGroup>? Of(
        StructuredMethod method,
        ImmutableArray<CilExceptionRegion> regions,
        IExceptionGroupEnumerator enumerator)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(enumerator);
        if (method.ExceptionGroups.IsEmpty) return [];
        var groups = ImmutableArray.CreateBuilder<UnitExceptionGroup>(method.ExceptionGroups.Count);
        foreach (var id in enumerator.Enumerate(method))
        {
            var group = method.ExceptionGroups[id];
            var clauses = ImmutableArray.CreateBuilder<int>(group.Clauses.Length);
            foreach (var clause in group.Clauses)
            {
                if (clause.Kind == CilExceptionRegionKind.Filter || clause.FilterBody is not null) return null;
                var region = RegionOf(group, clause, regions);
                if (region < 0) return null;
                clauses.Add(region);
            }
            groups.Add(new(id.Value, group.Parent?.Value ?? -1, group.TryOffset, group.TryLength,
                clauses.MoveToImmutable()));
        }
        return groups.Count == method.ExceptionGroups.Count ? groups.MoveToImmutable() : null;
    }

    // A method that stands in for its structured form until it must be emitted: its header,
    // and its exception groups without bodies; null when the groups do not fit the body.
    internal static StructuredMethod? Outline(
        ManagedMethodBody method,
        ImmutableArray<UnitExceptionGroup> groups,
        StructuredMethodOutline outline)
    {
        var regions = method.Body.ExceptionRegions;
        var built = ImmutableArray.CreateBuilder<StructuredExceptionGroup>(groups.Length);
        var ids = new HashSet<int>();
        foreach (var group in groups)
        {
            if (!ids.Add(group.Id)) return null;
            var clauses = ImmutableArray.CreateBuilder<StructuredExceptionClause>(group.Regions.Length);
            foreach (var index in group.Regions)
            {
                if ((uint)index >= (uint)regions.Length) return null;
                var region = regions[index];
                if (region.Kind == CilExceptionRegionKind.Filter) return null;
                clauses.Add(new(region.Kind, region.HandlerOffset, region.HandlerLength, region.CatchType,
                    region.FilterOffset, Empty, null)
                {
                    CatchTypeIdentity = region.CatchTypeIdentity,
                    HandlerBlock = new(-1),
                });
            }
            built.Add(new(new(group.Id), group.Parent < 0 ? null : new(group.Parent), group.TryOffset,
                group.TryLength, [], clauses.MoveToImmutable(), [], null, null));
        }
        return outline.StandIn(built.MoveToImmutable());
    }

    // Whether two methods' groups are laid out alike, group by group and clause by clause;
    // where the groups sit in the body may move.
    internal static bool Same(ImmutableArray<UnitExceptionGroup> before, ImmutableArray<UnitExceptionGroup> now) =>
        before.Length == now.Length && before.Zip(now).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Parent == pair.Second.Parent &&
            pair.First.Regions.SequenceEqual(pair.Second.Regions));

    private static int RegionOf(
        StructuredExceptionGroup group, StructuredExceptionClause clause, ImmutableArray<CilExceptionRegion> regions)
    {
        for (var index = 0; index < regions.Length; index++)
        {
            var region = regions[index];
            if (region.Kind == clause.Kind && region.TryOffset == group.TryOffset &&
                region.TryLength == group.TryLength && region.HandlerOffset == clause.HandlerOffset &&
                region.HandlerLength == clause.HandlerLength && Nullable.Equals(region.CatchType, clause.CatchType) &&
                Nullable.Equals(region.FilterOffset, clause.FilterOffset) &&
                Equals(region.CatchTypeIdentity, clause.CatchTypeIdentity))
            {
                return index;
            }
        }
        return -1;
    }
}
