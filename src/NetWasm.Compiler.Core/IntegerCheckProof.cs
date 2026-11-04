using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

// The integer checks of one immutable method snapshot that cannot fail: the
// divisions and remainders whose divisor is never zero, and the instructions
// that cannot overflow, which are signed divisions and remainders whose
// operands are never MinValue and -1, checked arithmetic whose result always
// fits, and checked conversions whose value always fits its target. Offsets
// are meaningful only for that snapshot.
public sealed record IntegerCheckProof(
    ImmutableArray<CilInstruction> Instructions,
    ImmutableHashSet<int> NonZeroDivisors,
    ImmutableHashSet<int> NonOverflowing)
{
    public bool ProvesNonZeroDivisor(ImmutableArray<CilInstruction> instructions, CilInstruction instruction) =>
        Describes(instructions, instruction) && NonZeroDivisors.Contains(instruction.Offset);

    public bool ProvesNoOverflow(ImmutableArray<CilInstruction> instructions, CilInstruction instruction) =>
        Describes(instructions, instruction) && NonOverflowing.Contains(instruction.Offset);

    private bool Describes(ImmutableArray<CilInstruction> instructions, CilInstruction instruction) =>
        Instructions == instructions && InstructionSnapshots.Contains(Instructions, instruction);
}
