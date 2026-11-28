// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

// A structured block's ordinary edges and the instruction offset its exit carries (a throw, a branch, a leave, a
// condition), which a safepoint may sit at.
internal static class StructuredBlockEdges
{
    public static IEnumerable<int> ExitOffset(StructuredBlockExit exit) => exit switch
    {
        StructuredTerminalExit terminal => [terminal.Instruction.Offset],
        StructuredBranchExit branch => [branch.InstructionOffset],
        StructuredLeaveExit leave => [leave.InstructionOffset],
        StructuredConditionalExit conditional => [conditional.Condition.InstructionOffset],
        _ => [],
    };

    public static IEnumerable<StructuredBlockId> Successors(StructuredBlockExit exit) => exit switch
    {
        StructuredFallthroughExit { Target: { } target } => [target],
        StructuredBranchExit branch => [branch.Target],
        StructuredConditionalExit conditional => [conditional.WhenTaken, conditional.WhenNotTaken],
        StructuredLeaveExit leave => [leave.Target],
        _ => [],
    };
}
