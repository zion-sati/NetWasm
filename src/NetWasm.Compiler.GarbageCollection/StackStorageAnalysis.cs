// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.GarbageCollection;

public sealed record StackStorageAnalysis(
    ImmutableDictionary<int, ImmutableArray<StackStorageType>> Entries,
    ImmutableDictionary<int, StackStorageTransfer> Transfers)
{
    public ImmutableDictionary<int, ImmutableArray<StackStorageType>> Locals { get; init; } =
        ImmutableDictionary<int, ImmutableArray<StackStorageType>>.Empty;

    public static StackStorageAnalysis Empty { get; } = new(
        ImmutableDictionary<int, ImmutableArray<StackStorageType>>.Empty,
        ImmutableDictionary<int, StackStorageTransfer>.Empty);
}
