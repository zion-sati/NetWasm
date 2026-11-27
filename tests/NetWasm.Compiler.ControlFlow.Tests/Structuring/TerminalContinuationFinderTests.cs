// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

using static ControlFlowTestSupport;

// A protected block's continuation that cannot complete normally: closed, ending in returns or throws, owned by no
// other continuation.
public sealed class TerminalContinuationFinderTests
{
    private readonly TerminalContinuationFinder _finder = new();

    // try { if (a) leave 9; if (a) leave 11; leave 16 } finally { }; 9: nop; ret; 11: if (a) { nop; ret } ret; 16: ret
    private static CilMethodBody Body(int secondFalseTarget = 15, int secondThen = 13) =>
        ControlFlowTestSupport.Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(3)),
            I(2, CilOperation.Leave, new CilOperand.BranchTarget(9)),
            I(3, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(4, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(6)),
            I(5, CilOperation.Leave, new CilOperand.BranchTarget(11)),
            I(6, CilOperation.Leave, new CilOperand.BranchTarget(16)),
            I(7, CilOperation.Nop),
            I(8, CilOperation.EndFinally),
            I(9, CilOperation.Nop),
            I(10, CilOperation.Return),
            I(11, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(12, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(secondFalseTarget)),
            I(13, CilOperation.Nop),
            I(14, secondThen == 13 ? CilOperation.Return : CilOperation.Branch,
                secondThen == 13 ? null : new CilOperand.BranchTarget(secondThen)),
            I(15, CilOperation.Return),
            I(16, CilOperation.Return)) with
        {
            ExceptionRegions = [new CilExceptionRegion(CilExceptionRegionKind.Finally, 0, 7, 7, 2, null, null)],
        };

    [Fact]
    public void AClosedContinuationThatReturnsOnEveryPathIsTerminal()
    {
        var graph = ControlFlowGraphBuilder.Build(Body());

        var blocks = _finder.Find(graph, 11, 17, [9, 16]);

        Assert.NotNull(blocks);
        Assert.Equal([11, 13, 15], blocks.Select(block => graph.GetBlock(block).StartOffset).Order());
    }

    [Fact]
    public void AContinuationReachingAnotherOnesBlocksIsNot()
    {
        var graph = ControlFlowGraphBuilder.Build(Body(secondFalseTarget: 16));

        Assert.Null(_finder.Find(graph, 11, 17, [9, 16]));
    }

    [Fact]
    public void AContinuationLeavingItsRangeIsNot()
    {
        var graph = ControlFlowGraphBuilder.Build(Body());

        Assert.Null(_finder.Find(graph, 11, 15, [9, 16]));
    }

    [Fact]
    public void AContinuationBranchingBackBeforeItsTargetIsNot()
    {
        var graph = ControlFlowGraphBuilder.Build(Body(secondThen: 9));

        Assert.Null(_finder.Find(graph, 11, 17, [16]));
    }
}
