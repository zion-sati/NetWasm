// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

public sealed record StackStorageAnchorPlan(
    ImmutableArray<StackStorageAnchor> Anchors,
    ImmutableDictionary<int, ImmutableArray<StackStorageAnchorAssignment>> Assignments)
{
    public static StackStorageAnchorPlan Empty { get; } = new([], ImmutableDictionary<int, ImmutableArray<StackStorageAnchorAssignment>>.Empty);
}
