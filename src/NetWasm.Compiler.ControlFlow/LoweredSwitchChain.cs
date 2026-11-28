// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow;

// A CIL switch is decoded into a chain: each case k is dup, ldc.i4 k, bne.un to the next case, pop, br to its target,
// and a final pop falls into the default. This reads such a chain back.
public static class LoweredSwitchChain
{
    // The instructions one case takes.
    public const int CaseLength = 5;

    // The number of cases the chain starting at the instruction has, if a final pop follows them; otherwise zero.
    public static int CaseCount(IReadOnlyList<CilInstruction> instructions, int start)
    {
        var cases = 0;
        while (IsCase(instructions, start + cases * CaseLength, cases))
        {
            cases++;
        }
        // A matched case guarantees an instruction after it.
        return cases > 0 && instructions[start + cases * CaseLength].Operation == CilOperation.Pop ? cases : 0;
    }

    // Case `index`'s target in the chain starting at the instruction.
    public static int Target(IReadOnlyList<CilInstruction> instructions, int start, int index) =>
        ((CilOperand.BranchTarget)instructions[start + index * CaseLength + 4].Operand).Offset;

    private static bool IsCase(IReadOnlyList<CilInstruction> instructions, int at, int value) =>
        at >= 0 && at + CaseLength < instructions.Count &&
        instructions[at].Operation == CilOperation.Duplicate &&
        instructions[at + 1] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 constant } &&
        constant.Value == value &&
        instructions[at + 2] is { Operation: CilOperation.BranchIfNotEqual, Operand: CilOperand.BranchTarget next } &&
        next.Offset == instructions[at + CaseLength].Offset &&
        instructions[at + 3].Operation == CilOperation.Pop &&
        instructions[at + 4] is { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget };
}
