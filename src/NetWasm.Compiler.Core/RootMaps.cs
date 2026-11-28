// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core;

public enum RootSourceKind
{
    Argument,
    Local,
    EvaluationStack,
    ManagedAddressArgument,
    ManagedAddressLocal,
    ManagedAddressEvaluationStack,
    AllocationTemporary,
    StackStorageAnchor,
}

public readonly record struct RootSource(
    RootSourceKind Kind,
    int Index,
    int ByteOffset = -1)
{
    public bool IsIndirect => ByteOffset >= 0;
}

public sealed record SafepointRootMap(
    int IlOffset,
    ImmutableArray<RootSource> Roots,
    ImmutableArray<RootSource> ConstructorCallRoots)
{
    // Of Roots, those a point that collects only while what it throws travels to a handler in the method need not
    // publish where their slot still holds null: no handler reads them, and the evaluation stack is gone once the throw
    // leaves. A collection there sees null in the slot, so a value dead after the throw is not kept alive. A point that
    // can collect and go on has none.
    public ImmutableArray<RootSource> Deferrable { get; init; } = [];
}

public sealed record MethodRootMap(
    EntityKey Method,
    ImmutableDictionary<RootSource, int> Slots,
    ImmutableDictionary<int, SafepointRootMap> Safepoints)
{
    public StackStorageAnchorPlan AnchorPlan { get; init; } = StackStorageAnchorPlan.Empty;

    // The stores, by IL offset, that a collector with generations need not be told of: each into an object the
    // method allocated with no safepoint since, which is young until the next collection.
    public ImmutableHashSet<int> UnrecordedStores { get; init; } = [];

    public int SlotCount => Slots.Count == 0
        ? 0
        : checked(Slots.Values.Max() + 1);
}
