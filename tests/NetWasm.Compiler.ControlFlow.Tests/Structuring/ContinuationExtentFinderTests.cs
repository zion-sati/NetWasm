// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

using static ControlFlowTestSupport;

// Where a protected block's continuations end: at the next protected block, unless one of them runs past it.
public sealed class ContinuationExtentFinderTests
{
    private const int Boundary = 12;
    private const int MethodEnd = 17;

    private readonly ContinuationExtentFinder _finder = new(new ReachableBlockFinder(new ControlFlowDistanceFinder()));

    // try { if (a) leave 9; leave 6 } finally { }; 6: nop; br 11; 9: nop; <early>; 11: nop;
    // 12: try { leave 16 } finally { }; 16: ret. The early continuation at 9 ends with the given operation to the given
    // target.
    private static CilMethodBody Body(CilOperation early, int earlyTarget) =>
        ControlFlowTestSupport.Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(3)),
            I(2, CilOperation.Leave, new CilOperand.BranchTarget(9)),
            I(3, CilOperation.Leave, new CilOperand.BranchTarget(6)),
            I(4, CilOperation.Nop),
            I(5, CilOperation.EndFinally),
            I(6, CilOperation.Nop),
            I(7, CilOperation.Branch, new CilOperand.BranchTarget(11)),
            I(8, CilOperation.Nop),
            I(9, CilOperation.Nop),
            I(10, early, new CilOperand.BranchTarget(earlyTarget)),
            I(11, CilOperation.Nop),
            I(12, CilOperation.Nop),
            I(13, CilOperation.Leave, new CilOperand.BranchTarget(16)),
            I(14, CilOperation.Nop),
            I(15, CilOperation.EndFinally),
            I(16, CilOperation.Return)) with
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(CilExceptionRegionKind.Finally, 0, 4, 4, 2, null, null),
                new CilExceptionRegion(CilExceptionRegionKind.Finally, 12, 2, 14, 2, null, null),
            ],
        };

    // goto 16 from 9 jumps over the protected block at 12: ending at 12 would send it into that block.
    [Fact]
    public void AContinuationThatBranchesPastTheBoundaryRunsToTheMethodEnd()
    {
        var graph = ControlFlowGraphBuilder.Build(Body(CilOperation.Branch, 16));

        Assert.Equal(MethodEnd, _finder.End(graph, [6, 9], Boundary, MethodEnd));
        Assert.Equal(MethodEnd, _finder.End(graph, [9], Boundary, MethodEnd));
        Assert.Equal(Boundary, _finder.End(graph, [6], Boundary, MethodEnd));
    }

    [Fact]
    public void ContinuationsThatReachOnlyTheBoundaryEndThere()
    {
        var graph = ControlFlowGraphBuilder.Build(Body(CilOperation.Branch, 11));

        Assert.Equal(Boundary, _finder.End(graph, [6, 9], Boundary, MethodEnd));
    }

    // A leave ends the continuation's sequence; its target is the next block's to reach.
    [Fact]
    public void ALeavePastTheBoundaryIsNotFollowed()
    {
        var graph = ControlFlowGraphBuilder.Build(Body(CilOperation.Leave, 16));

        Assert.Equal(Boundary, _finder.End(graph, [6, 9], Boundary, MethodEnd));
    }

    [Fact]
    public void ATargetPastTheBoundaryOrABoundaryAtTheMethodEndNeedsNoWalk()
    {
        var graph = ControlFlowGraphBuilder.Build(Body(CilOperation.Branch, 11));

        Assert.Equal(MethodEnd, _finder.End(graph, [6, 16], Boundary, MethodEnd));
        Assert.Equal(MethodEnd, _finder.End(graph, [6, 9], MethodEnd, MethodEnd));
        Assert.Equal(Boundary, _finder.End(graph, [], Boundary, MethodEnd));
        Assert.Equal(Boundary, _finder.End(graph, [Boundary], Boundary, MethodEnd));
    }
}
