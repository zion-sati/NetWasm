// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

using static ControlFlowTestSupport;

public sealed class TerminalBranchJoinSelectorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerminatingGuardsProduceAFlatSequenceWithPrivateOrSharedReturns(bool sharedReturn)
    {
        const int count = 256;
        var instructions = new List<CilInstruction>();
        var stride = sharedReturn ? 6 : 5;
        var terminal = count * stride + 3;
        for (var index = 0; index < count; index++)
        {
            var offset = index * stride;
            instructions.Add(I(offset, CilOperation.LoadArgument, new CilOperand.Index(0)));
            instructions.Add(I(offset + 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(index)));
            instructions.Add(I(offset + 2, CilOperation.BranchIfNotEqual, new CilOperand.BranchTarget(offset + stride)));
            instructions.Add(I(offset + 3, CilOperation.LoadInt32, new CilOperand.ConstantI4(index)));
            if (sharedReturn)
            {
                instructions.Add(I(offset + 4, CilOperation.StoreLocal, new CilOperand.Index(0)));
                instructions.Add(I(offset + 5, CilOperation.Branch, new CilOperand.BranchTarget(terminal)));
            }
            else
                instructions.Add(I(offset + 4, CilOperation.Return));
        }
        instructions.Add(I(count * stride, CilOperation.LoadInt32, new CilOperand.ConstantI4(-1)));
        if (sharedReturn)
        {
            instructions.Add(I(count * stride + 1, CilOperation.StoreLocal, new CilOperand.Index(0)));
            instructions.Add(I(count * stride + 2, CilOperation.Branch, new CilOperand.BranchTarget(terminal)));
            instructions.Add(I(terminal, CilOperation.LoadLocal, new CilOperand.Index(0)));
            instructions.Add(I(terminal + 1, CilOperation.Return));
        }
        else
            instructions.Add(I(count * stride + 1, CilOperation.Return));

        var method = Structurize(Body(CliValueKind.I4, 2, sharedReturn ? [CliValueKind.I4] : [], [.. instructions]));

        Assert.Equal(count, method.Body.Regions.OfType<StructuredIf>().Count());
        Assert.All(method.Body.Regions.OfType<StructuredIf>(), conditional =>
        {
            Assert.All(conditional.WhenTrue.Regions, region => Assert.IsType<StructuredCode>(region));
            Assert.All(conditional.WhenFalse.Regions, region => Assert.IsType<StructuredCode>(region));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectsTheOtherArmWithoutChangingTheTerminatingArm(bool reverse)
    {
        var state = State(
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(3)),
            I(2, CilOperation.Return),
            I(3, CilOperation.Branch, new CilOperand.BranchTarget(3)));
        var fallback = new Fallback();
        var actor = Assert.IsAssignableFrom<IBranchJoinSelector>(new TerminalBranchJoinSelector(fallback));
        var result = actor.Select(new(state, reverse ? 2 : 1, reverse ? 1 : 2,
            null, state.Graph.ReachableBlocks, null, null));

        Assert.Equal(2, result.Join);
        Assert.Empty(result.DuplicatedPrefixBlocks);
        Assert.Equal(state.Graph.ReachableBlocks, result.AnalysisBlocks);
        Assert.Equal(0, fallback.Calls);
        Assert.Empty(state.ClaimedBlockBodies);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void PreservesFallbackForScopeBoundariesAndIdenticalSuccessors(int boundary)
    {
        var state = State(
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(3)),
            I(2, CilOperation.Return),
            I(3, CilOperation.Branch, new CilOperand.BranchTarget(3)));
        var fallback = new Fallback();
        var actor = Assert.IsAssignableFrom<IBranchJoinSelector>(new TerminalBranchJoinSelector(fallback));
        var request = new BranchJoinSelectionRequest(state, 1, boundary == 0 ? 1 : 2,
            boundary == 1 ? 1 : null,
            boundary == 2 ? state.Graph.ReachableBlocks.Remove(1) : state.Graph.ReachableBlocks,
            boundary == 3 ? 1 : null, boundary == 4 ? 1 : null);

        Assert.Same(fallback.Result, actor.Select(request));
        Assert.Same(request, fallback.Request);
        Assert.Equal(1, fallback.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OwnedRegionsAndExceptionalTransfersRemainWithTheFallback(int boundary)
    {
        var state = State(
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(3)),
            I(2, CilOperation.Return),
            I(3, CilOperation.Branch, new CilOperand.BranchTarget(3)));
        switch (boundary)
        {
            case 0:
                state.ExceptionGroupsByEntry.Add(1, new StructuredExceptionGroupDraft(2, 1, [], [], []));
                break;
            case 1:
                state.LoopsByHeader.Add(1, new LoopRegion(1, 1, 1, 2, 2, [1], [1], []));
                break;
            case 2:
                state.DispatchersByNode.Add(1, [1]);
                break;
            case 3:
                var graph = state.Graph;
                state = new ControlFlowStructuringState(new ValidatedControlFlowGraph(
                    new ControlFlowGraph(graph.MethodBody, graph.Blocks, graph.Successors, graph.Predecessors,
                        graph.ExceptionalSuccessors.SetItem(1, [2]),
                        graph.ExceptionalPredecessors.SetItem(2, [1]), graph.ReachableBlocks),
                    ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
                    ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
                break;
        }
        var fallback = new Fallback();
        var actor = Assert.IsAssignableFrom<IBranchJoinSelector>(new TerminalBranchJoinSelector(fallback));
        var request = new BranchJoinSelectionRequest(state, 1, 2, null,
            state.Graph.ReachableBlocks, null, null);

        Assert.Same(fallback.Result, actor.Select(request));
        Assert.Same(request, fallback.Request);
        Assert.Equal(1, fallback.Calls);
        Assert.Empty(state.ClaimedBlockBodies);
    }

    [Fact]
    public void DoesNotDuplicateASharedEffectfulReturn()
    {
        var state = State(
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfFalse, new CilOperand.BranchTarget(3)),
            I(2, CilOperation.Branch, new CilOperand.BranchTarget(4)),
            I(3, CilOperation.Branch, new CilOperand.BranchTarget(4)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(5, CilOperation.StoreStaticField, new CilOperand.Entity(StaticFieldKey)),
            I(6, CilOperation.Return));
        var fallback = new Fallback();
        var actor = Assert.IsAssignableFrom<IBranchJoinSelector>(new TerminalBranchJoinSelector(fallback));

        Assert.Same(fallback.Result, actor.Select(new(state, 1, 2, null,
            state.Graph.ReachableBlocks, null, null)));
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public void RejectsMissingDependenciesAndRequests()
    {
        Assert.Throws<ArgumentNullException>(() => new TerminalBranchJoinSelector(null!));
        var fallback = new Fallback();
        var actor = Assert.IsAssignableFrom<IBranchJoinSelector>(new TerminalBranchJoinSelector(fallback));
        Assert.Throws<ArgumentNullException>(() => actor.Select(null!));
        var state = State(I(0, CilOperation.Return));
        Assert.Throws<ArgumentNullException>(() => actor.Select(new(null!, 0, 0, null, [], null, null)));
        Assert.Throws<ArgumentNullException>(() => actor.Select(new(state, 0, 0, null, null!, null, null)));
        Assert.Equal(0, fallback.Calls);
    }

    private static ControlFlowStructuringState State(params CilInstruction[] instructions) =>
        new(Validate(Body(CliValueKind.Void, 1, [], instructions)));

    private sealed class Fallback : IBranchJoinSelector
    {
        public int Calls { get; private set; }
        public BranchJoinSelectionRequest? Request { get; private set; }
        public BranchJoinSelection Result { get; } = new(null, [], [], []);
        public BranchJoinSelection Select(BranchJoinSelectionRequest request)
        {
            Calls++;
            Request = request;
            return Result;
        }
    }
}
