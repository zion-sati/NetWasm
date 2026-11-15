// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public sealed record ControlFlowGraphAnalysis(
    int Entry,
    ImmutableHashSet<int> Blocks,
    ImmutableDictionary<int, ImmutableHashSet<int>> Dominators,
    ImmutableDictionary<int, ImmutableHashSet<int>> PostDominators,
    ImmutableArray<ImmutableHashSet<int>> StronglyConnectedComponents,
    ImmutableArray<ImmutableHashSet<int>> NaturalLoops);
