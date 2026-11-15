// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.LoopInvariantFields;

internal sealed record AggregateFieldHoist(
    int PreheaderTerminatorOffset,
    int SourceLocalIndex,
    int CachedLocalIndex,
    CliTypeIdentity FieldType,
    CilInstruction AddressLoad,
    CilInstruction Projection,
    ImmutableArray<int> AddressOffsets,
    ImmutableArray<int> ProjectionOffsets);

internal sealed record AggregateFieldHoistingPlan(
    ImmutableArray<AggregateFieldHoist> Hoists)
{
    public static AggregateFieldHoistingPlan Empty { get; } = new([]);
}
