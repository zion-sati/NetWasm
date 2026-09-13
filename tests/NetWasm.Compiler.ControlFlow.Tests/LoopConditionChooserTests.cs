using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests;

public sealed class LoopConditionChooserTests
{
    [Fact]
    public void RejectsMissingSelection() =>
        Assert.Throws<ArgumentNullException>(() => Choose(new LoopConditionChooser(), null!));

    [Fact]
    public void ChoosesHeaderCondition()
    {
        var graph = Graph(
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(3)),
            I(2, CilOperation.Branch, new CilOperand.BranchTarget(0)),
            I(3, CilOperation.Return));

        Assert.Equal(0, Choose(new LoopConditionChooser(), new(graph, [0, 1], 0)));
    }

    [Fact]
    public void ChoosesUniqueRotatedPostTest()
    {
        var graph = Graph(
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(3)),
            I(1, CilOperation.Nop),
            I(2, CilOperation.Branch, new CilOperand.BranchTarget(3)),
            I(3, CilOperation.Branch, new CilOperand.BranchTarget(4)),
            I(4, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(5, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1)),
            I(6, CilOperation.Return));

        Assert.Equal(3, Choose(new LoopConditionChooser(), new(graph, [1, 2, 3], 2)));
    }

    [Fact]
    public void ReturnsNoSingleConditionForMultipleBoundaryTests()
    {
        var graph = Graph(
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(3)),
            I(1, CilOperation.Nop),
            I(2, CilOperation.Branch, new CilOperand.BranchTarget(3)),
            I(3, CilOperation.Branch, new CilOperand.BranchTarget(4)),
            I(4, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(5, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(7)),
            I(6, CilOperation.Branch, new CilOperand.BranchTarget(8)),
            I(7, CilOperation.Return),
            I(8, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(9, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1)),
            I(10, CilOperation.Return));

        Assert.Null(Choose(new LoopConditionChooser(), new(graph, [1, 2, 3, 4, 6], 2)));
    }

    [Fact]
    public void ReturnsNoConditionForAnUnconditionalCycle()
    {
        var graph = Graph(
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(1)),
            I(1, CilOperation.Branch, new CilOperand.BranchTarget(1)));

        Assert.Null(Choose(new LoopConditionChooser(), new(graph, [1], 1)));
    }

    private static int? Choose<TChooser>(
        TChooser chooser,
        LoopConditionSelection selection)
        where TChooser : ILoopConditionChooser => chooser.Choose(selection);

    private static ControlFlowGraph Graph(params CilInstruction[] instructions) =>
        CreateGraphBuilder().Build(Body(CliValueKind.Void, 1, [], instructions));
}
