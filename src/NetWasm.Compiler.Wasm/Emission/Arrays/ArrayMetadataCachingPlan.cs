using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal readonly record struct ArrayReferenceSlot(bool IsArgument, int Index);

internal sealed record ArrayMetadataCachingPlan(
    ImmutableArray<ArrayReferenceSlot> Slots,
    ImmutableDictionary<int, ArrayReferenceSlot> Accesses)
{
    public static ArrayMetadataCachingPlan Empty { get; } = new([], []);

    public ImmutableDictionary<StructuredBlockId, ImmutableArray<ArrayReferenceSlot>> LoopEntries { get; init; } = [];

    // The rank of each slot holding an array of rank two or more; the others hold SZ arrays.
    public ImmutableDictionary<ArrayReferenceSlot, int> Ranks { get; init; } = [];

    // Arrays each loop checks for null on entry, keyed by its condition block.
    public ImmutableDictionary<StructuredBlockId, ImmutableArray<ArrayReferenceSlot>> EntryNullChecks { get; init; } = [];

    // Accesses whose array a loop entry has already checked for null.
    public ImmutableHashSet<int> CheckedAccesses { get; init; } = [];
}
