using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.Tests;

public sealed class IntegerCheckProofTests
{
    // A proof rules out a check only for an instruction of the snapshot it describes.
    [Fact]
    public void ProvesChecksOnlyForTheSnapshotsOwnInstructions()
    {
        ImmutableArray<CilInstruction> instructions =
            [.. Enumerable.Range(0, 3).Select(index => new CilInstruction(index, index + 1, CilOperation.Divide, new CilOperand.None()))];
        var proof = new IntegerCheckProof(instructions, [0, 2], [1]);

        Assert.True(proof.ProvesNonZeroDivisor(instructions, instructions[0]));
        Assert.True(proof.ProvesNonZeroDivisor(instructions, instructions[2]));
        Assert.False(proof.ProvesNonZeroDivisor(instructions, instructions[1]));
        Assert.True(proof.ProvesNoOverflow(instructions, instructions[1]));
        Assert.False(proof.ProvesNoOverflow(instructions, instructions[0]));
        // Another body's instructions, or an equal instruction that is not the snapshot's, are not described.
        Assert.False(proof.ProvesNonZeroDivisor([.. instructions], instructions[0]));
        Assert.False(proof.ProvesNoOverflow(instructions, instructions[1] with { }));
    }
}
