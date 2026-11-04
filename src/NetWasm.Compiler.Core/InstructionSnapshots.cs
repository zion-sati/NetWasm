using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

internal static class InstructionSnapshots
{
    // Whether the instruction is one of the snapshot's own, found by its offset
    // in the offset-ordered instructions.
    public static bool Contains(ImmutableArray<CilInstruction> instructions, CilInstruction instruction)
    {
        var (low, high) = (0, instructions.Length - 1);
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var offset = instructions[middle].Offset;
            if (offset == instruction.Offset) return ReferenceEquals(instructions[middle], instruction);
            if (offset < instruction.Offset) low = middle + 1;
            else high = middle - 1;
        }
        return false;
    }
}
