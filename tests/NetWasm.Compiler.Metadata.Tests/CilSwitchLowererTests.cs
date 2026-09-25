using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class CilSwitchLowererTests
{
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
