// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection.Emit;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class RawCilInstructionReaderTests
{
    [Fact]
    public void ReadsFieldTokensWithoutInterpretingOperandBytesAsInstructions()
    {
        var bytes = new byte[] { 0x20, 0x7d, 0x7c, 0x7e, 0x80, 0x7d, 1, 0, 0, 4, 0x2a };

        var instructions = Read(new RawCilInstructionReader(), bytes);

        Assert.Equal([0, 5, 10], instructions.Select(instruction => instruction.Offset));
        Assert.Equal([OpCodes.Ldc_I4, OpCodes.Stfld, OpCodes.Ret], instructions.Select(instruction => instruction.OpCode));
        Assert.Null(instructions[0].Token);
        Assert.Equal(0x04000001, instructions[1].Token);
        Assert.Null(instructions[2].Token);
    }

    [Theory]
    [InlineData(0x0e, 1)]
    [InlineData(0x1f, 1)]
    [InlineData(0x2b, 1)]
    [InlineData(0x20, 4)]
    [InlineData(0x22, 4)]
    [InlineData(0x23, 8)]
    [InlineData(0x21, 8)]
    [InlineData(0xfe09, 2)]
    [InlineData(0xfe12, 1)]
    [InlineData(0xfe00, 0)]
    public void ReadsAllOperandWidthsIncludingInstructionsOutsideTheExecutableSubset(int code, int size)
    {
        var prefix = code > byte.MaxValue ? new byte[] { 0xfe, (byte)code } : [(byte)code];
        byte[] bytes = [.. prefix, .. new byte[size], 0x2a];

        var instructions = Read(new RawCilInstructionReader(), bytes);

        Assert.Equal(2, instructions.Length);
        Assert.Equal(unchecked((short)code), instructions[0].OpCode.Value);
        Assert.Equal(prefix.Length + size, instructions[1].Offset);
        Assert.Equal(OpCodes.Ret, instructions[1].OpCode);
    }

    [Fact]
    public void BranchTargetsUseSignedDisplacementsFromTheEndOfTheInstruction()
    {
        var reader = new RawCilInstructionReader();
        Assert.Equal<int>([0], Assert.Single(Read(reader, [0x2b, 0xfe])).BranchTargets);
        Assert.Equal<int>([3], Assert.Single(Read(reader, [0x2b, 1])).BranchTargets);
        Assert.Equal<int>([0], Assert.Single(Read(reader, [0x38, 0xfb, 0xff, 0xff, 0xff])).BranchTargets);
        Assert.Equal<int>([6], Assert.Single(Read(reader, [0xdd, 1, 0, 0, 0])).BranchTargets);
        Assert.Equal<int>([0, 14], Read(reader,
            [0x45, 2, 0, 0, 0, 0xf3, 0xff, 0xff, 0xff, 1, 0, 0, 0, 0x2a])[0].BranchTargets);
        Assert.Empty(Assert.Single(Read(reader, [0x45, 0, 0, 0, 0])).BranchTargets);
        Assert.Empty(Assert.Single(Read(reader, [0x2a])).BranchTargets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrepresentableBranchTargetsAreRejected(bool overflow)
    {
        byte[] bytes = overflow ? [0x38, 0xff, 0xff, 0xff, 0x7f] : [0x2b, 0x80];
        Assert.Throws<BadImageFormatException>(() => Read(new RawCilInstructionReader(), bytes));
    }

    [Fact]
    public void SkipsEverySwitchTarget()
    {
        var instructions = Read(new RawCilInstructionReader(),
            [0x45, 2, 0, 0, 0, 0x7d, 0, 0, 0, 0x7c, 0, 0, 0, 0x2a]);

        Assert.Equal(2, instructions.Length);
        Assert.Equal(OpCodes.Switch, instructions[0].OpCode);
        Assert.Equal(13, instructions[1].Offset);
    }

    [Theory]
    [InlineData(0x28)]
    [InlineData(0x8d)]
    [InlineData(0xd0)]
    public void PreservesMethodTypeAndGeneralMetadataTokens(int code)
    {
        var instruction = Assert.Single(Read(new RawCilInstructionReader(), [(byte)code, 1, 2, 3, 4]));

        Assert.Equal(0x04030201, instruction.Token);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RejectsUnknownTruncatedAndMalformedOperands(int shape)
    {
        byte[] bytes = shape switch
        {
            0 => [0xfe],
            1 => [0xfe, 0xff],
            2 => [0x7d, 1],
            3 => [0x45, 1],
            4 => [0x45, 0xff, 0xff, 0xff, 0xff],
            _ => [0x45, 1, 0, 0, 0],
        };

        Assert.Throws<BadImageFormatException>(() => Read(new RawCilInstructionReader(), bytes));
    }

    [Fact]
    public void EmptyBodyHasNoInstructions() => Assert.Empty(Read(new RawCilInstructionReader(), []));

    private static ImmutableArray<RawCilInstruction> Read<TReader>(TReader reader, byte[] bytes)
        where TReader : IRawCilInstructionReader => reader.Read(bytes);
}
