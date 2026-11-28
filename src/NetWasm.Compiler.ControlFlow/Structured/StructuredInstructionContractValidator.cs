// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal sealed class StructuredInstructionContractValidator :
    IStructuredInstructionContractValidator
{
    public void Validate(StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var source = method.Header.Instructions;
        var instructions = Index(source);
        // Index checked the offsets ascend, so a block's source range is found by search, not by a scan of the
        // method per block.
        var offsets = source.Select(instruction => instruction.Offset).ToArray();
        foreach (var block in method.Blocks.Values)
        {
            foreach (var instruction in block.Instructions)
                Match(instruction, instructions);
            var exitInstruction = ValidateExit(block.Exit, method, instructions);
            var first = FirstAtOrAfter(offsets, block.StartOffset);
            var sourceBlock = source.AsSpan(first, Math.Max(0, FirstAtOrAfter(offsets, block.EndOffset) - first));
            CilInstruction[] represented = exitInstruction is null
                ? block.Instructions.ToArray()
                : [.. block.Instructions, exitInstruction];
            if (block.EndOffset <= block.StartOffset ||
                !sourceBlock.SequenceEqual(represented))
            {
                throw new InvalidOperationException(
                    "A structured block does not exactly represent its source instruction range.");
            }
        }
    }


    // The index of the first offset at or after the given one.
    private static int FirstAtOrAfter(int[] offsets, int offset)
    {
        var index = Array.BinarySearch(offsets, offset);
        return index >= 0 ? index : ~index;
    }

    private static Dictionary<int, CilInstruction> Index(
        IEnumerable<CilInstruction> source)
    {
        var result = new Dictionary<int, CilInstruction>();
        var previous = -1;
        foreach (var instruction in source)
        {
            if (instruction.Offset <= previous ||
                instruction.NextOffset <= instruction.Offset ||
                !result.TryAdd(instruction.Offset, instruction))
            {
                throw new InvalidOperationException(
                    "Structured source instructions are not strictly ordered and unique.");
            }
            previous = instruction.Offset;
        }
        return result;
    }

    private static CilInstruction? ValidateExit(
        StructuredBlockExit exit,
        StructuredMethod method,
        IReadOnlyDictionary<int, CilInstruction> instructions)
    {
        switch (exit)
        {
            case StructuredFallthroughExit:
                return null;
            case StructuredBranchExit branch:
                return MatchBranch(
                    branch.InstructionOffset,
                    CilOperation.Branch,
                    branch.Target,
                    method,
                    instructions);
            case StructuredLeaveExit leave:
                return MatchBranch(
                    leave.InstructionOffset,
                    CilOperation.Leave,
                    leave.Target,
                    method,
                    instructions);
            case StructuredConditionalExit conditional:
                var instruction = Match(
                    conditional.Condition.InstructionOffset,
                    instructions);
                if (instruction.Operation != conditional.Condition.Operation)
                    throw new InvalidOperationException(
                        "A structured condition does not match its source operation.");
                MatchTarget(instruction, conditional.WhenTaken, method);
                return instruction;
            case StructuredTerminalExit terminal:
                Match(terminal.Instruction, instructions);
                return terminal.Instruction;
            default:
                throw new InvalidOperationException(
                    $"Unsupported structured block exit {exit.GetType().FullName}.");
        }
    }

    private static CilInstruction MatchBranch(
        int offset,
        CilOperation operation,
        StructuredBlockId target,
        StructuredMethod method,
        IReadOnlyDictionary<int, CilInstruction> instructions)
    {
        var instruction = Match(offset, instructions);
        if (instruction.Operation != operation)
            throw new InvalidOperationException(
                "A structured branch does not match its source operation.");
        MatchTarget(instruction, target, method);
        return instruction;
    }

    private static void MatchTarget(
        CilInstruction instruction,
        StructuredBlockId target,
        StructuredMethod method)
    {
        if (instruction.Operand is not CilOperand.BranchTarget sourceTarget ||
            !method.Blocks.TryGetValue(target, out var targetBlock) ||
            sourceTarget.Offset != targetBlock.StartOffset)
        {
            throw new InvalidOperationException(
                "A structured branch does not match its resolved target.");
        }
    }

    private static void Match(
        CilInstruction instruction,
        IReadOnlyDictionary<int, CilInstruction> instructions)
    {
        if (Match(instruction.Offset, instructions) != instruction)
            throw new InvalidOperationException(
                "A structured block instruction does not match its source instruction.");
    }

    private static CilInstruction Match(
        int offset,
        IReadOnlyDictionary<int, CilInstruction> instructions) =>
        instructions.TryGetValue(offset, out var instruction)
            ? instruction
            : throw new InvalidOperationException(
                "A structured instruction is absent from the source instruction stream.");
}
