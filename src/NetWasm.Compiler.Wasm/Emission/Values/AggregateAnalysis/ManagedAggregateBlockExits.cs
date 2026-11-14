using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

// What a structured block's exit continues to, and the stack kinds after each of its instructions.
internal static class ManagedAggregateBlockExits
{
    public static ImmutableArray<StructuredBlockId> Successors(
        StructuredBlockExit exit) => exit switch
        {
            StructuredFallthroughExit { Target: { } target } => [target],
            StructuredBranchExit branch => [branch.Target],
            StructuredConditionalExit conditional =>
                [conditional.WhenTaken, conditional.WhenNotTaken],
            StructuredLeaveExit leave => [leave.Target],
            _ => [],
        };

    // The offset of the instruction an exit is, if it is one.
    public static int? InstructionOffset(StructuredBlockExit exit) => exit switch
    {
        StructuredBranchExit branch => branch.InstructionOffset,
        StructuredConditionalExit conditional =>
            conditional.Condition.InstructionOffset,
        StructuredLeaveExit leave => leave.InstructionOffset,
        StructuredTerminalExit terminal => terminal.Instruction.Offset,
        _ => null,
    };

    public static ImmutableArray<CliValueKind> OutgoingKinds(
        StructuredMethod method,
        StructuredBlockDefinition block,
        int instructionIndex)
    {
        if (instructionIndex + 1 < block.Instructions.Length)
        {
            return method.InstructionEntryStacks[
                block.Instructions[instructionIndex + 1].Offset];
        }
        if (InstructionOffset(block.Exit) is { } offset)
        {
            return method.InstructionEntryStacks[offset];
        }
        var successors = Successors(block.Exit);
        return successors.Length == 0
            ? []
            : method.Blocks[successors[0]].EntryStack;
    }
}
