// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal readonly record struct TransientAggregateProjection(
    int ProducerOffset,
    int ElementIndex);

internal sealed record TransientAggregatePromotionPlan(
    ImmutableDictionary<int, CliTypeIdentity> Producers,
    ImmutableDictionary<int, TransientAggregateProjection> Projections,
    ImmutableHashSet<int> LocalTransferOffsets,
    ImmutableHashSet<int> PromotedLocalIndices)
{
    public static TransientAggregatePromotionPlan Empty { get; } = new(
        ImmutableDictionary<int, CliTypeIdentity>.Empty,
        ImmutableDictionary<int, TransientAggregateProjection>.Empty,
        ImmutableHashSet<int>.Empty,
        ImmutableHashSet<int>.Empty);
}

internal readonly record struct TransientAggregateScalarLocals(
    int First,
    int Second);

internal sealed record TransientAggregatePromotionLayout(
    TransientAggregatePromotionPlan Plan,
    ImmutableDictionary<int, TransientAggregateScalarLocals> ProducerLocals)
{
    public static TransientAggregatePromotionLayout Empty { get; } = new(
        TransientAggregatePromotionPlan.Empty,
        ImmutableDictionary<int, TransientAggregateScalarLocals>.Empty);
}
