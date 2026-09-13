using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Xunit;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class WholeRegionDispatcherBuilderTests
{
    [Theory]
    [InlineData(CilOperation.BranchIfTrue)]
    [InlineData(CilOperation.BranchIfFalse)]
    public void BuildMapsConditionalPolarityAndClaimsEachBodyOnce(CilOperation operation)
    {
        var state = CreateConditionalState(operation);
        var entry = state.Graph.Entry;
        var component = ImmutableHashSet.Create(entry.Index);
        var actor = new WholeRegionDispatcherBuilder(
            new CilConditionalBranchClassifier());

        var first = Build(actor, state, entry.Index, component);
        var second = Build(actor, state, entry.Index, component);

        var firstBlock = Assert.Single(first.Blocks);
        var secondBlock = Assert.Single(second.Blocks);
        var successors = state.Graph.Successors[entry.Index];
        var branchSelectsFirst = operation != CilOperation.BranchIfFalse;
        Assert.Equal(branchSelectsFirst ? successors[0] : successors[1], firstBlock.WhenTrue);
        Assert.Equal(branchSelectsFirst ? successors[1] : successors[0], firstBlock.WhenFalse);
        Assert.True(firstBlock.IsOriginal);
        Assert.False(secondBlock.IsOriginal);
        Assert.Equal(successors.Order(), first.Exits.Select(exit => exit.TargetBlock));
    }

    [Fact]
    public void BuildMapsAnUnconditionalBranchAndItsExit()
    {
        var state = CreateUnconditionalState();
        var entry = state.Graph.Entry;
        var actor = new WholeRegionDispatcherBuilder(
            new CilConditionalBranchClassifier());

        var result = Build(actor, state, entry.Index, [entry.Index]);

        var block = Assert.Single(result.Blocks);
        var successor = Assert.Single(state.Graph.Successors[entry.Index]);
        Assert.Equal(successor, block.WhenTrue);
        Assert.Null(block.WhenFalse);
        Assert.Equal(successor, Assert.Single(result.Exits).TargetBlock);
    }

    [Fact]
    public void BuildMapsATerminalBlockWithoutInventingAnExit()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var entry = state.Graph.Entry;
        var actor = new WholeRegionDispatcherBuilder(
            new CilConditionalBranchClassifier());

        var result = Build(actor, state, entry.Index, [entry.Index]);

        var block = Assert.Single(result.Blocks);
        Assert.Null(block.WhenTrue);
        Assert.Null(block.WhenFalse);
        Assert.Empty(result.Exits);
    }

    [Fact]
    public void ConstructorRejectsANullClassifier()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new WholeRegionDispatcherBuilder(null!));

        Assert.Equal("conditionalBranches", exception.ParamName);
    }

    private static ControlFlowStructuringState CreateConditionalState(CilOperation operation)
    {
        var body = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, operation, new CilOperand.BranchTarget(3)),
            I(2, CilOperation.Return, new CilOperand.None()),
            I(3, CilOperation.Return, new CilOperand.None()));
        return new ControlFlowStructuringState(Validate(body));
    }

    private static ControlFlowStructuringState CreateUnconditionalState()
    {
        var body = Body(
            CliValueKind.Void,
            0,
            [],
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(1)),
            I(1, CilOperation.Return, new CilOperand.None()));
        return new ControlFlowStructuringState(Validate(body));
    }

    private static StructuredDispatcherDraft Build<TBuilder>(
        TBuilder builder,
        ControlFlowStructuringState state,
        int? entry,
        ImmutableHashSet<int> component)
        where TBuilder : IWholeRegionDispatcherBuilder =>
        builder.Build(state, entry, component);
}
