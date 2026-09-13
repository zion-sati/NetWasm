using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal sealed class StructuredBlockDefinitionFactory : IStructuredBlockDefinitionFactory
{
    public ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> Create(
        ValidatedControlFlowGraph validated)
    {
        ArgumentNullException.ThrowIfNull(validated);
        return validated.Graph.ReachableBlocks
            .Order()
            .Select(index => CreateBlock(validated.Graph.GetBlock(index), validated))
            .ToImmutableDictionary(block => block.Id);
    }

    private static StructuredBlockDefinition CreateBlock(
        BasicBlock block,
        ValidatedControlFlowGraph validated)
    {
        var exit = CreateExit(block, validated);
        var instructions = ConsumesTerminator(exit)
            ? block.Instructions.RemoveAt(block.Instructions.Length - 1)
            : block.Instructions;
        return new(
            new(block.Index),
            block.StartOffset,
            instructions,
            validated.EntryStacks[block.Index],
            exit)
        {
            EndOffset = block.Instructions[^1].NextOffset,
        };
    }

    private static StructuredBlockExit CreateExit(
        BasicBlock block,
        ValidatedControlFlowGraph validated)
    {
        var terminator = block.Terminator;
        return terminator.Operation switch
        {
            CilOperation.Branch => new StructuredBranchExit(
                terminator.Offset,
                BranchTarget(terminator, validated.Graph)),
            CilOperation.Leave => new StructuredLeaveExit(
                terminator.Offset,
                BranchTarget(terminator, validated.Graph)),
            CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or
            CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
            CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned or
            CilOperation.BranchIfGreaterThanOrEqualSigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned or
            CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned or
            CilOperation.BranchIfLessThanOrEqualSigned or CilOperation.BranchIfLessThanOrEqualUnsigned =>
                ConditionalExit(terminator, block, validated),
            CilOperation.Switch => throw new InvalidOperationException(
                "Switch must be lowered before structured control-flow construction."),
            CilOperation.Return or CilOperation.Throw or CilOperation.Rethrow or
            CilOperation.EndFinally or CilOperation.EndFilter => new StructuredTerminalExit(terminator),
            _ => new StructuredFallthroughExit(Fallthrough(block, validated.Graph)),
        };
    }

    private static StructuredConditionalExit ConditionalExit(
        CilInstruction instruction,
        BasicBlock block,
        ValidatedControlFlowGraph validated)
    {
        var stack = validated.InstructionEntryStacks[instruction.Offset];
        var binary = IsBinaryCondition(instruction.Operation);
        var slot = stack.Length - (binary ? 2 : 1);
        if (slot < 0)
            throw new InvalidOperationException("A validated conditional block has insufficient stack facts.");
        var condition = new StructuredCondition(
            instruction.Offset,
            instruction.Operation,
            slot,
            stack[slot],
            binary ? stack[slot + 1] : null);
        return new(
            condition,
            BranchTarget(instruction, validated.Graph),
            Fallthrough(block, validated.Graph) ??
                throw new InvalidOperationException("A conditional block has no fallthrough target."));
    }

    private static StructuredBlockId BranchTarget(CilInstruction instruction, ControlFlowGraph graph) =>
        instruction.Operand is CilOperand.BranchTarget target
            ? new(graph.GetBlockAtOffset(target.Offset).Index)
            : throw new InvalidOperationException("A branch instruction has no target fact.");

    private static StructuredBlockId? Fallthrough(BasicBlock block, ControlFlowGraph graph) =>
        block.Index + 1 < graph.Blocks.Length ? new(block.Index + 1) : null;

    private static bool IsBinaryCondition(CilOperation operation) => operation is
        CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
        CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned or
        CilOperation.BranchIfGreaterThanOrEqualSigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned or
        CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned or
        CilOperation.BranchIfLessThanOrEqualSigned or CilOperation.BranchIfLessThanOrEqualUnsigned;

    private static bool ConsumesTerminator(StructuredBlockExit exit) => exit is not StructuredFallthroughExit;
}
