using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal sealed class StructuredStackContractValidator : IStructuredStackContractValidator
{
    public void Validate(StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (method.Header.MaxStack < 0)
            throw new InvalidOperationException("A structured method has a negative max stack.");
        var expectedInstructionOffsets = new HashSet<int>();
        foreach (var block in method.Blocks.Values)
        {
            RequireStackBound(block.EntryStack, method.Header.MaxStack);
            foreach (var instruction in block.Instructions)
            {
                expectedInstructionOffsets.Add(instruction.Offset);
                RequireInstructionStack(instruction.Offset, method);
            }
            ValidateExit(block.Exit, method, expectedInstructionOffsets);
        }
        if (!expectedInstructionOffsets.SetEquals(method.InstructionEntryStacks.Keys))
            throw new InvalidOperationException("Structured instruction-stack facts do not match the block instructions.");
    }

    private static void ValidateExit(
        StructuredBlockExit exit,
        StructuredMethod method,
        HashSet<int> expectedInstructionOffsets)
    {
        switch (exit)
        {
            case StructuredBranchExit branch:
                AddInstruction(branch.InstructionOffset, method, expectedInstructionOffsets);
                break;
            case StructuredConditionalExit conditional:
                AddInstruction(conditional.Condition.InstructionOffset, method, expectedInstructionOffsets);
                ValidateCondition(conditional.Condition, method);
                break;
            case StructuredLeaveExit leave:
                AddInstruction(leave.InstructionOffset, method, expectedInstructionOffsets);
                if (!method.InstructionEntryStacks[leave.InstructionOffset].IsEmpty)
                    throw new InvalidOperationException("A structured leave has a non-empty evaluation stack.");
                break;
            case StructuredTerminalExit terminal:
                AddInstruction(terminal.Instruction.Offset, method, expectedInstructionOffsets);
                break;
        }
    }

    private static void ValidateCondition(StructuredCondition condition, StructuredMethod method)
    {
        var stack = method.InstructionEntryStacks[condition.InstructionOffset];
        var binary = condition.Operation is
            CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
            CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned or
            CilOperation.BranchIfGreaterThanOrEqualSigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned or
            CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned or
            CilOperation.BranchIfLessThanOrEqualSigned or CilOperation.BranchIfLessThanOrEqualUnsigned;
        var unary = condition.Operation is CilOperation.BranchIfTrue or CilOperation.BranchIfFalse;
        if (!binary && !unary)
            throw new InvalidOperationException("A structured condition contains a non-conditional operation.");
        var arity = binary ? 2 : 1;
        if (condition.StackSlot != stack.Length - arity || condition.StackSlot < 0 ||
            stack[condition.StackSlot] != condition.LeftKind)
        {
            throw new InvalidOperationException("A structured condition has inconsistent left-stack facts.");
        }
        if (binary)
        {
            if (condition.RightKind is null || stack[condition.StackSlot + 1] != condition.RightKind.Value)
                throw new InvalidOperationException("A structured condition has inconsistent right-stack facts.");
        }
        else if (condition.RightKind is not null)
        {
            throw new InvalidOperationException("A unary structured condition carries a right-stack kind.");
        }
    }

    private static void AddInstruction(
        int offset,
        StructuredMethod method,
        HashSet<int> expectedInstructionOffsets)
    {
        if (!expectedInstructionOffsets.Add(offset))
            throw new InvalidOperationException("A structured instruction appears in more than one block.");
        RequireInstructionStack(offset, method);
    }

    private static void RequireInstructionStack(int offset, StructuredMethod method)
    {
        if (!method.InstructionEntryStacks.TryGetValue(offset, out var stack))
            throw new InvalidOperationException("A structured instruction has no verified entry-stack facts.");
        RequireStackBound(stack, method.Header.MaxStack);
    }

    private static void RequireStackBound(
        System.Collections.Immutable.ImmutableArray<CliValueKind> stack,
        int maxStack)
    {
        if (stack.Length > maxStack)
            throw new InvalidOperationException("A structured stack exceeds the method max stack.");
    }
}
