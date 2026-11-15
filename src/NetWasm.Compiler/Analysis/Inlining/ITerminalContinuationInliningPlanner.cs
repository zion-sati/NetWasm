// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

internal interface ITerminalContinuationInliningPlanner
{
    TerminalContinuationInliningPlan? Plan(
        TerminalContinuationInliningRequest request);
}

internal sealed record TerminalContinuationInliningRequest(
    CilMethodBody CallerBody,
    int CallIndex,
    ManagedInliningPlan Inlining,
    IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> InstructionEntryStacks);

internal sealed record TerminalContinuationInliningPlan(
    ImmutableArray<CilInstruction> Suffix,
    int ConsumedThroughIndex);
