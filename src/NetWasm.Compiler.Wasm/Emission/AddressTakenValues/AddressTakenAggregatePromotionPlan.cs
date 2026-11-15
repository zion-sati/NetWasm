// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

internal sealed record AddressTakenAggregateStorageSlot(
    int ByteOffset,
    int Size,
    CliValueKind StackKind);

internal sealed record AddressTakenAggregateLocalPlan(
    CliTypeIdentity Type,
    int Size,
    ImmutableArray<AddressTakenAggregateStorageSlot> StorageSlots)
{
    public static AddressTakenAggregateLocalPlan CreateI4Storage(
        CliTypeIdentity type,
        int size) => new(
        type,
        size,
        [.. Enumerable.Range(0, size / sizeof(int)).Select(index =>
            new AddressTakenAggregateStorageSlot(
                index * sizeof(int),
                sizeof(int),
                CliValueKind.I4))]);
}

internal sealed record AddressTakenAggregateProjection(
    int LocalIndex,
    int ByteOffset,
    int Size,
    CliValueKind StackKind);

internal sealed record AddressTakenAggregateCallSynchronization(
    ImmutableArray<int> LocalIndices,
    ImmutableArray<int> ReloadLocalIndices);

internal sealed record AddressTakenAggregatePromotionPlan(
    ImmutableDictionary<int, AddressTakenAggregateLocalPlan> PromotedLocals,
    ImmutableDictionary<int, AddressTakenAggregateProjection> Projections,
    ImmutableDictionary<int, AddressTakenAggregateCallSynchronization>
        CallSynchronizations)
{
    public static AddressTakenAggregatePromotionPlan Empty { get; } =
        new([], [], []);
}

internal sealed record AddressTakenAggregatePromotionLayout(
    AddressTakenAggregatePromotionPlan Plan,
    ImmutableDictionary<int, ImmutableArray<int>> StorageLocals)
{
    public static AddressTakenAggregatePromotionLayout Empty { get; } =
        new(AddressTakenAggregatePromotionPlan.Empty, []);
}
