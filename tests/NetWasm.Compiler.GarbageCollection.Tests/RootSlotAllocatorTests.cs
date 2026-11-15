// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection.Tests;

public sealed class RootSlotAllocatorTests
{
    [Fact]
    public void EmptySafepointsRequireNoSlots()
    {
        var allocator = CreateAllocator();
        var slots = allocator.Allocate([]);

        Assert.Empty(slots);
        Assert.Equal(0, CreateRootMap(slots).SlotCount);
        Assert.Throws<ArgumentNullException>(() => allocator.Allocate(null!));
    }

    [Fact]
    public void DisjointRootsReuseTheLowestSlot()
    {
        var first = new RootSource(RootSourceKind.Local, 0);
        var second = new RootSource(RootSourceKind.Local, 1);

        var slots = CreateAllocator().Allocate([
            new SafepointRootMap(0, [first], []),
            new SafepointRootMap(1, [second], []),
        ]);

        Assert.Equal(0, slots[first]);
        Assert.Equal(0, slots[second]);
        Assert.Equal(1, CreateRootMap(slots).SlotCount);
    }

    [Fact]
    public void PairwiseInterferenceAcrossSafepointsRequiresThreeSlots()
    {
        var first = new RootSource(RootSourceKind.Local, 0);
        var second = new RootSource(RootSourceKind.Local, 1);
        var third = new RootSource(RootSourceKind.Local, 2);

        var slots = CreateAllocator().Allocate([
            new SafepointRootMap(0, [first, second], []),
            new SafepointRootMap(1, [second, third], []),
            new SafepointRootMap(2, [first, third], []),
        ]);

        Assert.Equal(0, slots[first]);
        Assert.Equal(1, slots[second]);
        Assert.Equal(2, slots[third]);
        Assert.Equal(3, CreateRootMap(slots).SlotCount);
    }

    [Fact]
    public void ConstructorRootsInterfereWithoutBlockingDisjointReuse()
    {
        var ordinary = new RootSource(RootSourceKind.Local, 0);
        var disjoint = new RootSource(RootSourceKind.Local, 1);
        var allocation = new RootSource(RootSourceKind.AllocationTemporary, 0);

        var slots = CreateAllocator().Allocate([
            new SafepointRootMap(0, [ordinary], [ordinary, allocation]),
            new SafepointRootMap(1, [disjoint], []),
        ]);

        Assert.Equal(slots[ordinary], slots[disjoint]);
        Assert.NotEqual(slots[ordinary], slots[allocation]);
    }

    [Fact]
    public void CompleteRootIdentityAndCanonicalOrderMakeAllocationDeterministic()
    {
        var address = new RootSource(RootSourceKind.ManagedAddressArgument, 0);
        var firstPointee = new RootSource(RootSourceKind.ManagedAddressArgument, 0, 0);
        var secondPointee = new RootSource(RootSourceKind.ManagedAddressArgument, 0, 8);
        var forward = ImmutableArray.Create(address, firstPointee, secondPointee);
        var reverse = forward.Reverse().ToImmutableArray();
        var allocator = CreateAllocator();

        var first = allocator.Allocate([
            new SafepointRootMap(0, forward, []),
        ]);
        var second = allocator.Allocate([
            new SafepointRootMap(1, reverse, []),
        ]);

        Assert.Equal(0, first[address]);
        Assert.Equal(1, first[firstPointee]);
        Assert.Equal(2, first[secondPointee]);
        Assert.All(first, pair => Assert.Equal(pair.Value, second[pair.Key]));
    }

#pragma warning disable CA1859 // Tests exercise the allocator through its capability contract.
    private static IRootSlotAllocator CreateAllocator() => new RootSlotAllocator();
#pragma warning restore CA1859

    private static MethodRootMap CreateRootMap(
        ImmutableDictionary<RootSource, int> slots) =>
        new(default, slots, ImmutableDictionary<int, SafepointRootMap>.Empty);
}
