// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal readonly record struct ManagedAggregateSsaLane(
    ManagedAggregateStorageId Storage,
    int ByteOffset);

internal readonly record struct ManagedAggregateSsaValue(int Id);

internal enum ManagedAggregateSsaDefinitionKind
{
    Initial,
    Phi,
    Store,
    Reload,
}

internal sealed record ManagedAggregateSsaDefinition(
    ManagedAggregateSsaValue Value,
    ManagedAggregateSsaLane Lane,
    ManagedAggregateSsaDefinitionKind Kind,
    StructuredBlockId Block,
    int? InstructionOffset);

internal sealed record ManagedAggregateSsaLaneValue(
    ManagedAggregateSsaLane Lane,
    ManagedAggregateSsaValue Value);

internal sealed record ManagedAggregateSsaAccess(
    ManagedAggregateLaneAccess Access,
    ImmutableArray<ManagedAggregateSsaLaneValue> Reads,
    ImmutableArray<ManagedAggregateSsaLaneValue> Writes);

internal sealed record ManagedAggregateSsaPhi(
    StructuredBlockId Block,
    ManagedAggregateSsaLane Lane,
    ManagedAggregateSsaValue Result,
    ImmutableDictionary<StructuredBlockId, ManagedAggregateSsaValue> Incoming);

internal sealed record ManagedAggregateSsaEdgeCopy(
    StructuredBlockId From,
    StructuredBlockId To,
    ManagedAggregateSsaLane Lane,
    ManagedAggregateSsaValue Source,
    ManagedAggregateSsaValue Destination);

internal sealed record ManagedAggregateSsaMaterialization(
    ManagedAggregateMaterialization Materialization,
    ImmutableArray<ManagedAggregateSsaLaneValue> Values)
{
    public ImmutableArray<ManagedAggregateSsaLaneValue> Reloads { get; init; } = [];
}

internal sealed record ManagedAggregateSsaPlan(
    ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage> Storages,
    ImmutableArray<ManagedAggregateSsaLane> Lanes,
    ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue> InitialValues,
    ImmutableArray<ManagedAggregateSsaDefinition> Definitions,
    ImmutableDictionary<int, ManagedAggregateSsaAccess> Accesses,
    ImmutableArray<ManagedAggregateSsaPhi> Phis,
    ImmutableArray<ManagedAggregateSsaEdgeCopy> EdgeCopies,
    ImmutableArray<ManagedAggregateSsaMaterialization> Materializations,
    ImmutableArray<ManagedAggregateRootDependency> RootDependencies)
{
    public static ManagedAggregateSsaPlan Empty { get; } = new(
        [], [], [], [], [], [], [], [], []);
}

internal sealed record ManagedAggregateSsaPlanningRequest(
    StructuredMethod Method,
    ManagedAggregateValueAnalysis Analysis)
{
    // Whether only storages with a lane read are selected.
    public bool References { get; init; }
}
