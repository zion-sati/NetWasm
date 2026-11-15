// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

// while (true) loops whose only conditional exit sits inside a protected region: the
// region's continuations break, continue or return, so the loop needs no condition.
public sealed class UnconditionalLoopStructuringTests
{
    [Fact]
    public void ALoopLeftOnlyThroughAFinallyIsUnconditional()
    {
        var method = Structurize(BreakOutOfFinally());

        var loop = Assert.Single(method.Body.Regions.OfType<StructuredLoop>());
        Assert.Null(loop.Condition);
        Assert.Empty(loop.ContinueBody.Regions);
        Assert.Empty(loop.ExitBody.Regions);
        var region = Assert.Single(loop.Body.Regions.OfType<StructuredExceptionRegion>());
        var continuations = method.ExceptionGroups[region.Group].NormalContinuations;
        Assert.Empty(continuations.Single(continuation => continuation.TargetOffset == 0).Body.Regions);
        Assert.IsType<StructuredLoopBreak>(Assert.Single(
            continuations.Single(continuation => continuation.TargetOffset == 7).Body.Regions));
    }

    [Fact]
    public void AnExitBeforeTheHeaderBreaksInsteadOfFallingBackIntoTheLoop()
    {
        // Debug code for `while (true) { try { if (...) continue; } finally { } return ...; }`
        // places the return before the loop header, so the exit is the first continuation.
        var method = Structurize(Body(CliValueKind.I4, 1, [],
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(10)),
            I(1, CilOperation.Nop),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(3, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)),
            I(4, CilOperation.Leave, new CilOperand.BranchTarget(8)),
            I(5, CilOperation.Leave, new CilOperand.BranchTarget(10)),
            I(6, CilOperation.Nop),
            I(7, CilOperation.EndFinally),
            I(8, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(9, CilOperation.Return),
            I(10, CilOperation.Nop),
            I(11, CilOperation.Branch, new CilOperand.BranchTarget(1))) with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 2, 4, 6, 2, null, null)],
        });

        var loop = Assert.Single(method.Body.Regions.OfType<StructuredLoop>());
        Assert.Null(loop.Condition);
        var region = Assert.Single(loop.Body.Regions.OfType<StructuredExceptionRegion>());
        var exit = method.ExceptionGroups[region.Group].NormalContinuations[0];
        Assert.Equal(8, exit.TargetOffset);
        Assert.IsType<StructuredLoopBreak>(Assert.Single(exit.Body.Regions));
    }

    [Fact]
    public void ALeaveFromANestedRegionBackToTheHeaderContinuesTheLoop()
    {
        var method = Structurize(NestedLeaveToHeader());

        var loop = Assert.Single(method.Body.Regions.OfType<StructuredLoop>());
        Assert.Null(loop.Condition);
        var outer = Assert.Single(loop.Body.Regions.OfType<StructuredExceptionRegion>());
        Assert.Contains(
            method.ExceptionGroups[outer.Group].NormalContinuations,
            continuation => continuation.TargetOffset == 0);
    }

    internal static CilMethodBody BreakOutOfFinally() => Body(CliValueKind.I4, 1, [],
        I(0, CilOperation.Nop),
        I(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
        I(2, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
        I(3, CilOperation.Leave, new CilOperand.BranchTarget(7)),
        I(4, CilOperation.Leave, new CilOperand.BranchTarget(0)),
        I(5, CilOperation.Nop),
        I(6, CilOperation.EndFinally),
        I(7, CilOperation.LoadArgument, new CilOperand.Index(0)),
        I(8, CilOperation.Return)) with
    {
        ExceptionRegions = [new(CilExceptionRegionKind.Finally, 1, 4, 5, 2, null, null)],
    };

    // Optimized code for `while (true) { lock (gate) { try { if (...) break; continue; } finally { } } }`
    // leaves both regions with one instruction, back to the header before the outer region.
    internal static CilMethodBody NestedLeaveToHeader() => Body(CliValueKind.I4, 1, [],
        I(0, CilOperation.Nop),
        I(1, CilOperation.Nop),
        I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
        I(3, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)),
        I(4, CilOperation.Leave, new CilOperand.BranchTarget(10)),
        I(5, CilOperation.Leave, new CilOperand.BranchTarget(0)),
        I(6, CilOperation.Nop),
        I(7, CilOperation.EndFinally),
        I(8, CilOperation.Nop),
        I(9, CilOperation.EndFinally),
        I(10, CilOperation.LoadArgument, new CilOperand.Index(0)),
        I(11, CilOperation.Return)) with
    {
        ExceptionRegions =
        [
            new(CilExceptionRegionKind.Finally, 2, 4, 6, 2, null, null),
            new(CilExceptionRegionKind.Finally, 1, 7, 8, 2, null, null),
        ],
    };
}
