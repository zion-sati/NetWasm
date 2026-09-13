using NetWasm.Compiler.Core;
using System.Collections.Immutable;
using New = NetWasm.Compiler.ControlFlow.Structured;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structured;

public sealed class StructuredBlockDefinitionFactoryTests
{
    [Fact]
    public void CreateSeparatesTerminalInstructionFromBlockCode()
    {
        var validated = Validate(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));

        var blocks = ((New.IStructuredBlockDefinitionFactory)new New.StructuredBlockDefinitionFactory()).Create(validated);

        var block = Assert.Single(blocks).Value;
        Assert.Empty(block.Instructions);
        Assert.Empty(block.EntryStack);
        Assert.IsType<New.StructuredTerminalExit>(block.Exit);
    }

    [Fact]
    public void CreateResolvesConditionalTargetsAndStackFacts()
    {
        var validated = Validate(Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
            I(2, CilOperation.Return),
            I(3, CilOperation.Return)));

        var blocks = ((New.IStructuredBlockDefinitionFactory)new New.StructuredBlockDefinitionFactory()).Create(validated);

        var entry = blocks[new(0)];
        var conditional = Assert.IsType<New.StructuredConditionalExit>(entry.Exit);
        Assert.Single(entry.Instructions);
        Assert.Equal(new New.StructuredBlockId(2), conditional.WhenTaken);
        Assert.Equal(new New.StructuredBlockId(1), conditional.WhenNotTaken);
        Assert.Equal(0, conditional.Condition.StackSlot);
        Assert.Equal(CliValueKind.I4, conditional.Condition.LeftKind);
        Assert.Null(conditional.Condition.RightKind);
    }

    [Fact]
    public void CreateResolvesUnconditionalTarget()
    {
        var validated = Validate(Body(
            CliValueKind.Void,
            0,
            [],
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(2)),
            I(1, CilOperation.Return),
            I(2, CilOperation.Return)));

        var blocks = ((New.IStructuredBlockDefinitionFactory)new New.StructuredBlockDefinitionFactory()).Create(validated);

        var branch = Assert.IsType<New.StructuredBranchExit>(blocks[new(0)].Exit);
        Assert.Equal(new New.StructuredBlockId(2), branch.Target);
    }

    [Fact]
    public void CreateResolvesFallthroughTargetAtAReferencedLeader()
    {
        var validated = Validate(Body(
            CliValueKind.Void,
            0,
            [],
            I(0, CilOperation.Nop),
            I(1, CilOperation.Return),
            I(2, CilOperation.Branch, new CilOperand.BranchTarget(1))));

        var blocks = ((New.IStructuredBlockDefinitionFactory)new New.StructuredBlockDefinitionFactory()).Create(validated);

        var fallthrough = Assert.IsType<New.StructuredFallthroughExit>(blocks[new(0)].Exit);
        Assert.Equal(new New.StructuredBlockId(1), fallthrough.Target);
    }

    [Fact]
    public void CreateRequiresAValidatedGraph()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((New.IStructuredBlockDefinitionFactory)new New.StructuredBlockDefinitionFactory()).Create(null!));
    }

    [Fact]
    public void CreateRecordsBinaryConditionStackKinds()
    {
        var validated = Validate(Body(
            CliValueKind.Void,
            2,
            [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(2, CilOperation.BranchIfEqual, new CilOperand.BranchTarget(4)),
            I(3, CilOperation.Return),
            I(4, CilOperation.Return)));

        var blocks = ((New.IStructuredBlockDefinitionFactory)new New.StructuredBlockDefinitionFactory())
            .Create(validated);

        var condition = Assert.IsType<New.StructuredConditionalExit>(blocks[new(0)].Exit).Condition;
        Assert.Equal(0, condition.StackSlot);
        Assert.Equal(CliValueKind.I4, condition.LeftKind);
        Assert.Equal(CliValueKind.I4, condition.RightKind);
    }

    [Fact]
    public void CreateRejectsInconsistentPrestructuredInputs()
    {
        var conditional = I(0, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(0));
        var insufficientStack = ManualValidated(
            [new BasicBlock(0, 0, [conditional])],
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, []));
        var missingFallthrough = ManualValidated(
            [new BasicBlock(0, 0, [conditional])],
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, [CliValueKind.I4]));
        var badBranch = ManualValidated(
            [new BasicBlock(0, 0, [I(0, CilOperation.Branch)])],
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, []));
        var unloweredSwitch = ManualValidated(
            [new BasicBlock(0, 0, [I(0, CilOperation.Switch, new CilOperand.SwitchTargets([0]))])],
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, [CliValueKind.I4]));

        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredBlockDefinitionFactory)new New.StructuredBlockDefinitionFactory())
                .Create(insufficientStack));
        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredBlockDefinitionFactory)new New.StructuredBlockDefinitionFactory())
                .Create(missingFallthrough));
        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredBlockDefinitionFactory)new New.StructuredBlockDefinitionFactory())
                .Create(badBranch));
        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredBlockDefinitionFactory)new New.StructuredBlockDefinitionFactory())
                .Create(unloweredSwitch));
    }

    private static ValidatedControlFlowGraph ManualValidated(
        ImmutableArray<BasicBlock> blocks,
        ImmutableDictionary<int, ImmutableArray<CliValueKind>> instructionStacks)
    {
        var body = Body(
            CliValueKind.Void,
            2,
            [],
            [.. blocks.SelectMany(block => block.Instructions)]);
        var emptyEdges = blocks.ToImmutableDictionary(
            block => block.Index,
            _ => ImmutableArray<int>.Empty);
        var graph = new ControlFlowGraph(
            body,
            blocks,
            emptyEdges,
            emptyEdges,
            emptyEdges,
            emptyEdges,
            blocks.Select(block => block.Index).ToImmutableHashSet());
        return new(
            graph,
            blocks.ToImmutableDictionary(block => block.Index, _ => ImmutableArray<CliValueKind>.Empty),
            instructionStacks);
    }
}
