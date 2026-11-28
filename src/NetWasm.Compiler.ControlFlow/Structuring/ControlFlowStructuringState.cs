// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ControlFlowStructuringState(ValidatedControlFlowGraph validated)
{
    internal ValidatedControlFlowGraph Validated { get; } = validated;

    internal ControlFlowGraph Graph => Validated.Graph;

    internal ControlFlowGraph ContinuationGraph { get; init; } = validated.Graph;

    internal Dictionary<int, LoopRegion> LoopsByHeader { get; } = [];

    internal HashSet<int> SuppressedLoopHeaders { get; } = [];

    internal Stack<int> LoopExitTargets { get; } = new();

    internal Stack<int> LoopContinueTargets { get; } = new();

    internal Stack<ImmutableHashSet<int>> ActiveDispatchers { get; } = new();


    internal HashSet<int> ContinuationOwnedBlocks { get; } = [];

    internal HashSet<int> ClaimedBlockBodies { get; } = [];


    internal HashSet<int> BoundaryClaimedBlockBodies { get; } = [];

    internal HashSet<int> BoundaryOwnedBlocks { get; } = [];

    internal Dictionary<ImmutableHashSet<int>, Dictionary<int, DominatorTree>> PostDominators { get; } = [];

    internal Dictionary<ImmutableHashSet<int>, Dictionary<(int Start, int Stop), Dictionary<int, int>>> Distances { get; } = [];

    internal Dictionary<int, StructuredExceptionGroupDraft> ExceptionGroupsByEntry { get; set; } = [];

    internal Dictionary<int, ImmutableHashSet<int>> DispatchersByNode { get; set; } = [];
}
