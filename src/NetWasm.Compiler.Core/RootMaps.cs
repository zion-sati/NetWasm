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
    ImmutableArray<RootSource> ConstructorCallRoots);

public sealed record MethodRootMap(
    EntityKey Method,
    ImmutableDictionary<RootSource, int> Slots,
    ImmutableDictionary<int, SafepointRootMap> Safepoints)
{
    public StackStorageAnchorPlan AnchorPlan { get; init; } = StackStorageAnchorPlan.Empty;

    public int SlotCount => Slots.Count == 0
        ? 0
        : checked(Slots.Values.Max() + 1);
}
