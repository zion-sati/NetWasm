using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ExceptionContinuationGraphProjectorTests
{
    [Fact]
    public void ProjectRejectsNullGraph()
    {
        var projector = Assert.IsAssignableFrom<IExceptionContinuationGraphProjector>(
            new ExceptionContinuationGraphProjector());
        Assert.Throws<ArgumentNullException>(() => projector.Project(null!));
    }

    [Fact]
    public void ProjectSummarizesOnlyReachableLeavesExitingCatchAndFilterHandlers()
    {
        var graph = Graph(
            [new(CilExceptionRegionKind.Catch, 0, 2, 2, 5, TypeKey, null),
             new(CilExceptionRegionKind.Filter, 0, 2, 7, 1, null, 6)],
            [I(0, CilOperation.Branch, new CilOperand.BranchTarget(1)),
             I(1, CilOperation.Return),
             I(2, CilOperation.Leave, new CilOperand.BranchTarget(3)),
             I(3, CilOperation.Leave, new CilOperand.BranchTarget(1)),
             I(4, CilOperation.Leave, new CilOperand.BranchTarget(7)),
             I(5, CilOperation.Branch, new CilOperand.BranchTarget(8)),
             I(6, CilOperation.Leave, new CilOperand.BranchTarget(5)),
             I(7, CilOperation.Leave, new CilOperand.BranchTarget(8)),
             I(8, CilOperation.Return)],
            [0, 1, 2, 3, 4, 5, 7, 8]);

        var projector = Assert.IsAssignableFrom<IExceptionContinuationGraphProjector>(
            new ExceptionContinuationGraphProjector());
        var projected = projector.Project(graph);

        Assert.Equal<int>([1, 7, 8], projected.Successors[0]);
        Assert.Contains(0, projected.Predecessors[7]);
        Assert.Contains(0, projected.Predecessors[8]);
        Assert.Equal<int>([1], graph.Successors[0]);
        Assert.DoesNotContain(0, graph.Predecessors[7]);
        Assert.DoesNotContain(0, graph.Predecessors[8]);
        Assert.Same(graph.MethodBody, projected.MethodBody);
        Assert.Equal(graph.Blocks, projected.Blocks);
        Assert.Same(graph.ExceptionalSuccessors, projected.ExceptionalSuccessors);
        Assert.Same(graph.ExceptionalPredecessors, projected.ExceptionalPredecessors);
        Assert.Same(graph.ReachableBlocks, projected.ReachableBlocks);
        foreach (var block in graph.Blocks.Skip(1))
        {
            Assert.Equal(graph.Successors[block.Index], projected.Successors[block.Index]);
        }
    }

    [Theory]
    [InlineData(CilExceptionRegionKind.Finally)]
    [InlineData(CilExceptionRegionKind.Fault)]
    public void ProjectDoesNotTreatCleanupAsAnIndependentContinuation(CilExceptionRegionKind kind)
    {
        var graph = Graph(
            [new(kind, 0, 1, 1, 1, null, null)],
            [I(0, CilOperation.Return),
             I(1, CilOperation.Leave, new CilOperand.BranchTarget(0))],
            [0, 1]);

        var projector = Assert.IsAssignableFrom<IExceptionContinuationGraphProjector>(
            new ExceptionContinuationGraphProjector());
        var projected = projector.Project(graph);

        Assert.Empty(projected.Successors[0]);
        Assert.Equal<int>([0], projected.Successors[1]);
        Assert.Equal<int>([1], projected.Predecessors[0]);
    }

    [Fact]
    public void ProjectKeepsNestedHandlerContinuationsInTheirLexicalScope()
    {
        var graph = Graph(
            [new(CilExceptionRegionKind.Catch, 0, 2, 2, 5, TypeKey, null),
             new(CilExceptionRegionKind.Catch, 3, 1, 4, 1, TypeKey, null),
             new(CilExceptionRegionKind.Finally, 3, 2, 5, 1, null, null)],
            [I(0, CilOperation.Branch, new CilOperand.BranchTarget(1)),
             I(1, CilOperation.Return),
             I(2, CilOperation.Branch, new CilOperand.BranchTarget(3)),
             I(3, CilOperation.Throw),
             I(4, CilOperation.Leave, new CilOperand.BranchTarget(6)),
             I(5, CilOperation.EndFinally),
             I(6, CilOperation.Leave, new CilOperand.BranchTarget(7)),
             I(7, CilOperation.Return)],
            [0, 1, 2, 3, 4, 5, 6, 7]);

        var projector = Assert.IsAssignableFrom<IExceptionContinuationGraphProjector>(
            new ExceptionContinuationGraphProjector());
        var projected = projector.Project(graph);

        Assert.Equal<int>([1, 7], projected.Successors[0]);
        Assert.Equal<int>([6], projected.Successors[3]);
        Assert.Empty(projected.Successors[5]);
        Assert.DoesNotContain(6, projected.Successors[0]);
        Assert.Equal<int>([3, 4], projected.Predecessors[6].Order());
    }

    [Fact]
    public void ProjectPreservesAnOrdinaryGraph()
    {
        var graph = Graph([], [I(0, CilOperation.Return)], [0]);

        var projector = Assert.IsAssignableFrom<IExceptionContinuationGraphProjector>(
            new ExceptionContinuationGraphProjector());
        var projected = projector.Project(graph);

        Assert.Empty(projected.Successors[0]);
        Assert.Empty(projected.Predecessors[0]);
    }

    private static ControlFlowGraph Graph(
        ImmutableArray<CilExceptionRegion> regions,
        ImmutableArray<CilInstruction> instructions,
        ImmutableHashSet<int> reachable)
    {
        var blocks = instructions.Select(instruction =>
            new BasicBlock(instruction.Offset, instruction.Offset, [instruction])).ToImmutableArray();
        var successors = blocks.ToImmutableDictionary(
            block => block.Index,
            block => block.Terminator.Operand is CilOperand.BranchTarget target
                ? ImmutableArray.Create(target.Offset) : []);
        var predecessors = blocks.ToImmutableDictionary(
            block => block.Index,
            block => successors.Where(pair => pair.Value.Contains(block.Index))
                .Select(pair => pair.Key).ToImmutableArray());
        return new ControlFlowGraph(
            Body(CliValueKind.Void, 0, [], [.. instructions]) with { ExceptionRegions = regions },
            blocks, successors, predecessors,
            ImmutableDictionary<int, ImmutableArray<int>>.Empty,
            ImmutableDictionary<int, ImmutableArray<int>>.Empty,
            reachable);
    }
}
