using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values;

internal enum ManagedAggregateStorageKind
{
    Argument,
    Local,
}

internal readonly record struct ManagedAggregateStorageId(
    ManagedAggregateStorageKind Kind,
    int Index);

internal sealed record ManagedAggregateStorage(
    ManagedAggregateStorageId Id,
    CliTypeIdentity Type,
    int Size,
    bool Owned)
{
    // Lanes of an aggregate with reference or byref fields: one lane per such
    // field, of its own kind, and 4-byte integer lanes elsewhere. Empty means
    // the aggregate is all 4-byte integer lanes.
    public ImmutableArray<ManagedAggregateLaneShape> LaneShapes { get; init; } = [];

    public IEnumerable<ManagedAggregateLaneShape> Lanes => LaneShapes.IsDefaultOrEmpty
        ? Enumerable.Range(0, Size / sizeof(int)).Select(index =>
            new ManagedAggregateLaneShape(index * sizeof(int), sizeof(int), CliValueKind.I4))
        : LaneShapes;

    public ManagedAggregateLaneShape Lane(int byteOffset) =>
        Lanes.Single(lane => lane.ByteOffset == byteOffset);
}

internal readonly record struct ManagedAggregateLaneShape(
    int ByteOffset,
    int Size,
    CliValueKind Kind);

internal readonly record struct ManagedAggregateAddressRecipe(
    ManagedAggregateStorageId Storage,
    int ByteOffset,
    CliTypeIdentity Type);

internal sealed record ManagedAggregateLaneAccess(
    int InstructionOffset,
    StructuredBlockId Block,
    ManagedAggregateAddressRecipe Address,
    int Size,
    CliValueKind StackKind,
    bool Writes)
{
    // The lanes the access covers, when they are not the 4-byte integer
    // lanes of its byte range: a reference or byref field's lane, or the
    // lanes of the aggregate or field that initobj clears (StackKind ValueType).
    public ImmutableArray<int> LaneOffsets { get; init; } = [];
}

internal enum ManagedAggregateRootSetKind
{
    Ordinary,
    Constructor,
}

internal sealed record ManagedAggregateRootDependency(
    int SafepointOffset,
    RootSource Source,
    ManagedAggregateAddressRecipe Address,
    ManagedAggregateRootSetKind Set);

internal sealed record ManagedAggregateMaterialization(
    int InstructionOffset,
    ManagedAggregateStorageId Storage,
    bool ReloadAfter = false);

internal sealed record ManagedAggregateValueAnalysis(
    ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage> Storages,
    ImmutableDictionary<int, ManagedAggregateLaneAccess> LaneAccesses,
    ImmutableDictionary<StructuredBlockId, ImmutableHashSet<ManagedAggregateStorageId>>
        LiveInAddresses,
    ImmutableArray<ManagedAggregateRootDependency> RootDependencies,
    ImmutableArray<ManagedAggregateMaterialization> Materializations)
{
    public static ManagedAggregateValueAnalysis Empty { get; } =
        new([], [], [], [], []);
}

internal sealed record ManagedAggregateValueAnalysisRequest(
    StructuredMethod Method,
    MethodRootMap RootMap,
    ManagedByRefEffects ByRefEffects,
    bool Enabled)
{
    // Whether aggregates with reference and byref fields, and initobj, take part.
    public bool References { get; init; }

    // Locals another promotion owns. They keep no storage for lanes to be
    // stored to or reloaded from.
    public ImmutableHashSet<int> ExcludedLocals { get; init; } = [];
}
