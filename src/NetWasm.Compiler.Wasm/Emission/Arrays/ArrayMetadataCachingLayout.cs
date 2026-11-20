// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Arrays;

internal sealed record ArrayMetadataSnapshotLocals(int Length, int Data);

internal readonly record struct ArrayDimensionSnapshotLocals(int LowerBound, int Length);

// The lower bound and length of each dimension of an array of rank two or more, and its data.
internal sealed record RectangularArraySnapshotLocals(ImmutableArray<ArrayDimensionSnapshotLocals> Dimensions, int Data);

internal sealed record ArrayMetadataCachingLayout(
    ArrayMetadataCachingPlan Plan,
    ImmutableDictionary<ArrayReferenceSlot, ArrayMetadataSnapshotLocals> Slots)
{
    public static ArrayMetadataCachingLayout Empty { get; } = new(ArrayMetadataCachingPlan.Empty, []);

    public ImmutableDictionary<ArrayReferenceSlot, RectangularArraySnapshotLocals> RectangularSlots { get; init; } = [];
}

// The run plan with the length local each of its slots keeps.
internal sealed record ArrayLengthRunLayout(
    ArrayLengthRunPlan Plan,
    ImmutableDictionary<ArrayReferenceSlot, int> Locals)
{
    public static ArrayLengthRunLayout Empty { get; } = new(ArrayLengthRunPlan.Empty, []);
}
