using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class CilSwitchLowererTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(71)]
    public void ExpansionPreservesOriginalSourceOffsets(int? previousOffset)
    {
        var instructions = ImmutableArray.Create(
            new CilInstruction(0, 5, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            new CilInstruction(5, 18, CilOperation.Switch, new CilOperand.SwitchTargets([18, 18]))
            {
                OriginalOffset = previousOffset,
            },
            new CilInstruction(18, 19, CilOperation.Return, new CilOperand.None())
            {
                OriginalOffset = previousOffset is null ? null : 95,
            });

        var lowered = ((ICilSwitchLowerer)new CilSwitchLowerer()).Lower(instructions, []);

        Assert.Equal(0, lowered.Instructions[0].SourceOffset);
        Assert.All(lowered.Instructions.Skip(1).SkipLast(1),
            instruction => Assert.Equal(previousOffset ?? 5, instruction.SourceOffset));
        Assert.Equal(previousOffset is null ? 18 : 95, lowered.Instructions[^1].SourceOffset);
        Assert.Equal(12, lowered.Instructions[^1].Offset);
    }

    [Fact]
    public void LoweredSwitchReservesTwoAdditionalEvaluationStackSlots()
    {
        var instructions = ImmutableArray.Create(
            new CilInstruction(
                0,
                13,
                CilOperation.Switch,
                new CilOperand.SwitchTargets([13, 13])),
            new CilInstruction(13, 14, CilOperation.Return, new CilOperand.None()));

        var lowered = ((ICilSwitchLowerer)new CilSwitchLowerer()).Lower(
            instructions,
            []);

        Assert.True(lowered.Changed);
        Assert.Equal(2, lowered.AdditionalMaxStack);
    }

    [Fact]
    public void BodyWithoutSwitchDoesNotIncreaseMaxStack()
    {
        var instructions = ImmutableArray.Create(
            new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None()));

        var lowered = ((ICilSwitchLowerer)new CilSwitchLowerer()).Lower(
            instructions,
            []);

        Assert.False(lowered.Changed);
        Assert.Equal(0, lowered.AdditionalMaxStack);
    }

    [Fact]
    public void LoweredSwitchRewritesExceptionRegionAndFilterOffsets()
    {
        var instructions = ImmutableArray.Create(
            new CilInstruction(
                0,
                13,
                CilOperation.Switch,
                new CilOperand.SwitchTargets([13])),
            new CilInstruction(
                13,
                14,
                CilOperation.Branch,
                new CilOperand.BranchTarget(14)),
            new CilInstruction(14, 15, CilOperation.Return, new CilOperand.None()));
        var region = new CilExceptionRegion(
            CilExceptionRegionKind.Filter,
            TryOffset: 0,
            TryLength: 13,
            HandlerOffset: 13,
            HandlerLength: 1,
            CatchType: null,
            FilterOffset: 13);

        var lowered = ((ICilSwitchLowerer)new CilSwitchLowerer()).Lower(
            instructions,
            [region]);

        var rewritten = Assert.Single(lowered.ExceptionRegions);
        Assert.Equal(0, rewritten.TryOffset);
        Assert.Equal(6, rewritten.TryLength);
        Assert.Equal(6, rewritten.HandlerOffset);
        Assert.Equal(1, rewritten.HandlerLength);
        Assert.Equal(6, rewritten.FilterOffset);
    }

    [Fact]
    public void LoweredSwitchLeavesNonFilterExceptionRegionsWithoutFilterOffsets()
    {
        var instructions = ImmutableArray.Create(
            new CilInstruction(
                0,
                13,
                CilOperation.Switch,
                new CilOperand.SwitchTargets([13])),
            new CilInstruction(13, 14, CilOperation.Return, new CilOperand.None()));
        var region = new CilExceptionRegion(
            CilExceptionRegionKind.Catch,
            TryOffset: 0,
            TryLength: 13,
            HandlerOffset: 13,
            HandlerLength: 1,
            CatchType: null,
            FilterOffset: null);

        var lowered = ((ICilSwitchLowerer)new CilSwitchLowerer()).Lower(
            instructions,
            [region]);

        Assert.Null(Assert.Single(lowered.ExceptionRegions).FilterOffset);
    }
}
