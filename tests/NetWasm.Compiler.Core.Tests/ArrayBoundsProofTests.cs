using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.Tests;

public sealed class ArrayBoundsProofTests
{
    [Fact]
    public void ProvesNonNullForCheckedAndNonNullAccessesOfTheBodyItDescribes()
    {
        ImmutableArray<CilInstruction> instructions = [new(0, 1, CilOperation.Nop, new CilOperand.None())];
        ImmutableArray<CliValueKind> locals = [CliValueKind.I4];
        ImmutableArray<CliTypeIdentity> types = [CliTypeIdentity.FromStackKind(CliValueKind.I4)];
        var proof = new ArrayBoundsProof(instructions, locals, types, [1]) { NonNullOffsets = [2] };

        // A checked access also passed its null check.
        Assert.True(proof.ProvesNonNull(1));
        Assert.True(proof.ProvesNonNull(2));
        Assert.False(proof.ProvesNonNull(3));
        Assert.True(proof.Describes(instructions, locals, types));
        Assert.False(proof.Describes([.. instructions], locals, types));
        Assert.False(proof.Describes(instructions, [.. locals], types));
        Assert.False(proof.Describes(instructions, locals, [.. types]));
    }

    [Fact]
    public void ContainsOnlyTheSnapshotsOwnInstructions()
    {
        ImmutableArray<CilInstruction> instructions =
            [.. Enumerable.Range(0, 5).Select(index => new CilInstruction(index * 2, index * 2 + 2, CilOperation.Nop, new CilOperand.None()))];
        var proof = new ArrayBoundsProof(instructions, [], [], []);

        Assert.All(instructions, instruction => Assert.True(proof.Contains(instruction)));
        // An equal instruction at the same offset belongs to another snapshot.
        Assert.False(proof.Contains(instructions[3] with { }));
        Assert.False(proof.Contains(new CilInstruction(5, 6, CilOperation.Nop, new CilOperand.None())));
        Assert.False(proof.Contains(new CilInstruction(-1, 0, CilOperation.Nop, new CilOperand.None())));
        Assert.False(proof.Contains(new CilInstruction(10, 11, CilOperation.Nop, new CilOperand.None())));
        Assert.False(new ArrayBoundsProof([], [], [], []).Contains(instructions[0]));
    }
}
