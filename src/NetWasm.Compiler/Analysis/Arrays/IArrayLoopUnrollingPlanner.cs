// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

// A loop bounded by ArrayLocal's length, or by LengthLocal where that is -1.
internal sealed record ArrayLoopUnrollingPlan(
    int IndexLocal,
    int ArrayLocal,
    int GuardOffset,
    ImmutableArray<int> EntryBranches,
    ImmutableArray<CilInstruction> Body,
    int Factor)
{
    public int LengthLocal { get; init; } = -1;
}

internal interface IArrayLoopUnrollingPlanner
{
    ImmutableArray<ArrayLoopUnrollingPlan> Plan(ValidatedControlFlowGraph controlFlow);
}
