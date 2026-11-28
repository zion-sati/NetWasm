// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

using static ControlFlowTestSupport;

// Where a loop's exits meet once the paths that end the region are set aside.
public sealed class TerminatingExitJoinFinderTests
{
    private readonly TerminatingExitJoinFinder _finder = new();

    // try {
    //   0: br 3
    //   1: (loop) ldarg; brfalse 5     the loop's other exit goes straight to 7
    //   3: ldarg; brtrue 1             the condition: back to 1, or out to 5
    //   5: ldarg; brfalse 7            the condition's exit: on to 7, or leave the protected block
    //   ...: leave 11
    //   7: nop ... 9: leave 11
    // } finally { 11-12 }
    private static CilMethodBody Body(CilOperation conditionExitEnd) =>
        ControlFlowTestSupport.Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(3)),
            I(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(2, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(7)),
            I(3, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(4, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1)),
            I(5, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(6, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(9)),
            I(7, CilOperation.Nop),
            I(8, CilOperation.Branch, new CilOperand.BranchTarget(10)),
            I(9, conditionExitEnd, new CilOperand.BranchTarget(conditionExitEnd == CilOperation.Leave ? 13 : 1)),
            I(10, CilOperation.Leave, new CilOperand.BranchTarget(13)),
            I(11, CilOperation.Nop),
            I(12, CilOperation.EndFinally),
            I(13, CilOperation.Return)) with
        {
            ExceptionRegions = [new CilExceptionRegion(CilExceptionRegionKind.Finally, 0, 11, 11, 2, null, null)],
        };

    private static (ControlFlowGraph Graph, int[] Exits, ImmutableHashSet<int> Loop, ImmutableHashSet<int> Allowed)
        Shape(CilOperation conditionExitEnd = CilOperation.Leave)
    {
        var graph = ControlFlowGraphBuilder.Build(Body(conditionExitEnd));
        int Block(int offset) => graph.GetBlockAtOffset(offset).Index;
        var allowed = graph.Blocks.Where(block => block.StartOffset < 11).Select(block => block.Index).ToImmutableHashSet();
        return (graph, [Block(5), Block(7)], [Block(1), Block(3)], allowed);
    }

    [Fact]
    public void ExitsMeetWhereTheirPathsThatDoNotLeaveGo()
    {
        var (graph, exits, loop, allowed) = Shape();

        var join = _finder.Find(graph, exits, loop, allowed);

        Assert.Equal(7, graph.GetBlock(Assert.NotNull(join)).StartOffset);
    }

    // Without a protected block: the condition's exit goes on to 7 or returns.
    [Fact]
    public void APathThatReturnsEndsTheRegionToo()
    {
        var graph = ControlFlowGraphBuilder.Build(ControlFlowTestSupport.Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(3)),
            I(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(2, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(7)),
            I(3, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(4, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1)),
            I(5, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(6, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(9)),
            I(7, CilOperation.Nop),
            I(8, CilOperation.Return),
            I(9, CilOperation.Return)));
        int Block(int offset) => graph.GetBlockAtOffset(offset).Index;

        var join = _finder.Find(graph, [Block(5), Block(7)], [Block(1), Block(3)], graph.ReachableBlocks);

        Assert.Equal(7, graph.GetBlock(Assert.NotNull(join)).StartOffset);
    }

    [Fact]
    public void APathBackIntoTheLoopHasNoJoin()
    {
        var (graph, exits, loop, allowed) = Shape(CilOperation.Branch);

        Assert.Null(_finder.Find(graph, exits, loop, allowed));
    }

    [Fact]
    public void OneExitNeedsNoJoin()
    {
        var (graph, exits, loop, allowed) = Shape();

        Assert.Null(_finder.Find(graph, [exits[0]], loop, allowed));
    }
}
