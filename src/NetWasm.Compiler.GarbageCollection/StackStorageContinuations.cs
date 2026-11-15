// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.GarbageCollection;

public readonly record struct StackStorageFlowEdge(int Block, bool ResetStack);

public sealed record StackStorageContinuations(
    ImmutableDictionary<int, ImmutableArray<StackStorageFlowEdge>> Normal,
    ImmutableDictionary<int, ImmutableArray<StackStorageFlowEdge>> Exceptional);
