// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.GarbageCollection;
using NetWasm.Compiler.Wasm.Emission.Values;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedAggregateRootMapProjectorTests
{
    private static readonly EntityKey Method = new(new("root-projector"), 1);
    private static readonly ManagedAggregateStorage Storage = new(
        new(ManagedAggregateStorageKind.Argument, 0),
        CliTypeIdentity.FromStackKind(CliValueKind.ValueType),
        8,
        true);
    private static readonly ManagedAggregateAddressRecipe Address = new(
        Storage.Id,
        0,
        Storage.Type);

    [Fact]
    public void RemovesOnlyExactOccurrencesAndReallocatesRemainingSlots()
    {
        var candidate = new RootSource(RootSourceKind.ManagedAddressLocal, 0);
        var unrelated = new RootSource(RootSourceKind.Local, 1);
        var allocation = new RootSource(RootSourceKind.AllocationTemporary, 0);
        var anchor = new StackStorageAnchorPlan(
            [new(
                new(StackStorageLocationKind.Local, 2),
                CliTypeIdentity.FromStackKind(CliValueKind.I4))],
            ImmutableDictionary<int, ImmutableArray<StackStorageAnchorAssignment>>.Empty);
        var rootMap = new MethodRootMap(
            Method,
            ImmutableDictionary<RootSource, int>.Empty
                .Add(candidate, 0)
                .Add(unrelated, 1)
                .Add(allocation, 2),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                10,
                new(10, [candidate, unrelated], [candidate, unrelated, allocation])))
        {
            AnchorPlan = anchor,
        };
        var plan = Plan(
            new(10, candidate, Address, ManagedAggregateRootSetKind.Ordinary),
            new(10, candidate, Address, ManagedAggregateRootSetKind.Constructor));

        Assert.True(CreateProjector().TryProject(new(rootMap, plan), out var projected));

        Assert.Collection(
            projected.Safepoints[10].Roots,
            source => Assert.Equal(unrelated, source));
        Assert.Collection(
            projected.Safepoints[10].ConstructorCallRoots,
            source => Assert.Equal(unrelated, source),
            source => Assert.Equal(allocation, source));
        Assert.Same(anchor, projected.AnchorPlan);
        Assert.Equal(0, projected.Slots[unrelated]);
        Assert.Equal(1, projected.Slots[allocation]);
        Assert.Equal(2, projected.SlotCount);
        Assert.DoesNotContain(candidate, projected.Slots.Keys);
    }

    [Fact]
    public void EmptyPlanReturnsOriginalRootMap()
    {
        var rootMap = new MethodRootMap(Method, [], []);

        Assert.True(CreateProjector().TryProject(
            new(rootMap, ManagedAggregateSsaPlan.Empty),
            out var projected));
        Assert.Same(rootMap, projected);
    }

    [Fact]
    public void RejectsMissingDuplicateOrUnselectedOccurrences()
    {
        var candidate = new RootSource(RootSourceKind.ManagedAddressLocal, 0);
        var rootMap = new MethodRootMap(
            Method,
            ImmutableDictionary<RootSource, int>.Empty.Add(candidate, 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                10,
                new(10, [candidate], [])));
        var dependency = new ManagedAggregateRootDependency(
            10,
            candidate,
            Address,
            ManagedAggregateRootSetKind.Ordinary);
        var missing = dependency with { SafepointOffset = 11 };
        var unselected = Plan(dependency) with { Storages = [] };

        Assert.False(CreateProjector().TryProject(
            new(rootMap, Plan(missing)),
            out var missingProjection));
        Assert.Same(rootMap, missingProjection);
        Assert.False(CreateProjector().TryProject(
            new(rootMap, Plan(dependency, dependency)),
            out var duplicateProjection));
        Assert.Same(rootMap, duplicateProjection);
        Assert.False(CreateProjector().TryProject(
            new(rootMap, unselected),
            out var unselectedProjection));
        Assert.Same(rootMap, unselectedProjection);
    }

    [Fact]
    public void RejectsUnownedIndirectOrNonAddressRoots()
    {
        var direct = new RootSource(RootSourceKind.ManagedAddressLocal, 0);
        var indirect = direct with { ByteOffset = 0 };
        var nonAddress = new RootSource(RootSourceKind.Local, 0);

        AssertRejected(direct, Plan(new ManagedAggregateRootDependency(
            10,
            direct,
            Address,
            ManagedAggregateRootSetKind.Ordinary)) with
        {
            Storages = ImmutableDictionary<
                ManagedAggregateStorageId,
                ManagedAggregateStorage>.Empty.Add(
                    Storage.Id,
                    Storage with { Owned = false }),
        });
        AssertRejected(indirect, Plan(new ManagedAggregateRootDependency(
            10,
            indirect,
            Address,
            ManagedAggregateRootSetKind.Ordinary)));
        AssertRejected(nonAddress, Plan(new ManagedAggregateRootDependency(
            10,
            nonAddress,
            Address,
            ManagedAggregateRootSetKind.Ordinary)));
    }

    [Fact]
    public void RejectsNullDependenciesAndRequestMembers()
    {
        var projector = CreateProjector();
        var rootMap = new MethodRootMap(Method, [], []);

        Assert.Throws<ArgumentNullException>(() =>
            new ManagedAggregateRootMapProjector(null!));
        Assert.Throws<ArgumentNullException>(() =>
            projector.TryProject(null!, out _));
        Assert.Throws<ArgumentNullException>(() => projector.TryProject(new(
            null!,
            ManagedAggregateSsaPlan.Empty), out _));
        Assert.Throws<ArgumentNullException>(() => projector.TryProject(new(
            rootMap,
            null!), out _));
    }

    private static ManagedAggregateSsaPlan Plan(
        params ManagedAggregateRootDependency[] dependencies) => new(
        ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>.Empty
            .Add(Storage.Id, Storage),
        [],
        [],
        [],
        [],
        [],
        [],
        [],
        [.. dependencies]);

    private static void AssertRejected(
        RootSource source,
        ManagedAggregateSsaPlan plan)
    {
        var rootMap = new MethodRootMap(
            Method,
            ImmutableDictionary<RootSource, int>.Empty.Add(source, 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                10,
                new(10, [source], [])));

        Assert.False(CreateProjector().TryProject(
            new(rootMap, plan),
            out var projection));
        Assert.Same(rootMap, projection);
    }

#pragma warning disable CA1859 // Tests exercise the projector through its capability contract.
    private static IManagedAggregateRootMapProjector CreateProjector() =>
        new ManagedAggregateRootMapProjector(new RootSlotAllocator());
#pragma warning restore CA1859
}
