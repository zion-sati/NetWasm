// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Draft;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class DispatcherSequenceStepExecutorTests
{
    [Fact]
    public void ExecuteReturnsTheUnchangedCursorWhenNoDispatcherMatches()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        var command = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(
            new DispatcherSequenceStepExecutor(
            new ActiveControlFlowBlockClipper(),
            new EmptyLoopActiveBlockRetentionSelector()));
        var recursiveBuilder = new StructuredControlFlowBuilderProbe();
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        const int cursor = int.MaxValue;

        var result = ((IStructuredSequenceStepExecutor)command).Execute(
            recursiveBuilder,
            state,
            cursor,
            null,
            ImmutableHashSet<int>.Empty,
            [],
            regions);

        Assert.Equal(StructuredSequenceStepDisposition.NotHandled, result.Disposition);
        Assert.Equal(cursor, result.NextBlockOffset);
        Assert.Empty(regions);
        Assert.Equal(0, recursiveBuilder.CallCount);
    }

    [Fact]
    public void PostTestContinuationRetainsLoopTransfersAndConditionExitBlocks()
    {
        var template = ControlFlowStructuringStateTestFactory.Create().Graph;
        var condition = new BasicBlock(2, 2,
            [new CilInstruction(2, 1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1))]);
        var graph = new ControlFlowGraph(template.MethodBody,
            template.Blocks.SetItem(2, condition),
            template.Successors.SetItem(2, [1, 3]),
            template.Predecessors, template.ExceptionalSuccessors,
            template.ExceptionalPredecessors, [0, 1, 2, 3, 4, 5]);
        var state = new ControlFlowStructuringState(new ValidatedControlFlowGraph(
            graph, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
        state.LoopsByHeader.Add(0, new LoopRegion(0, 2, 0, 3, 5,
            [0, 1, 2], [0, 1, 2, 4], [3]));
        state.LoopExitTargets.Push(99);
        state.LoopContinueTargets.Push(101);
        var builder = new RecordingLoopBuilder();
        var command = new DispatcherSequenceStepExecutor(
            new ActiveControlFlowBlockClipper(), new EmptyLoopActiveBlockRetentionSelector());
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();

        var result = ((IStructuredSequenceStepExecutor)command).Execute(builder, state, 0, null,
            [0, 1, 2, 3, 4, 5], [], regions);

        Assert.Equal(5, result.NextBlockOffset);
        Assert.IsType<StructuredPostTestLoopDraft>(Assert.Single(regions));
        Assert.Equal(3, builder.Calls.Count);
        var continuation = builder.Calls[1];
        Assert.Equal(1, continuation.Start);
        Assert.Equal(0, continuation.Stop);
        Assert.Equal(5, continuation.BreakTarget);
        Assert.Equal(0, continuation.ContinueTarget);
        Assert.Contains(3, continuation.Allowed);
        Assert.Equal(99, builder.Calls[2].BreakTarget);
        Assert.Equal(101, builder.Calls[2].ContinueTarget);
        Assert.Equal(99, Assert.Single(state.LoopExitTargets));
        Assert.Equal(101, Assert.Single(state.LoopContinueTargets));
        Assert.Empty(state.SuppressedLoopHeaders);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnconditionalLoopBreaksToItsExitAndOwnsABodyThatCrossesTheBoundary(bool crossesBoundary)
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        state.LoopsByHeader.Add(0, new LoopRegion(0, null, 0, 3, 3, [0, 1, 2], [0, 1, 2], []));
        state.LoopExitTargets.Push(99);
        state.LoopContinueTargets.Push(101);
        state.BoundaryClaimedBlockBodies.Add(1);
        var builder = new RecordingLoopBuilder();
        var command = new DispatcherSequenceStepExecutor(
            new ActiveControlFlowBlockClipper(), new EmptyLoopActiveBlockRetentionSelector());
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        ImmutableHashSet<int> allowed = crossesBoundary ? [0, 1] : [0, 1, 2, 3, 4, 5];

        var result = ((IStructuredSequenceStepExecutor)command).Execute(builder, state, 0, null,
            allowed, [], regions);

        // The loop has no condition; its body runs from the header and breaks to the exit.
        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Equal(3, result.NextBlockOffset);
        var loop = Assert.IsType<StructuredLoopDraft>(Assert.Single(regions));
        Assert.Null(loop.ConditionBlock);
        Assert.Empty(loop.ContinueBody.Regions);
        Assert.Empty(loop.ExitBody.Regions);
        var body = Assert.Single(builder.Calls);
        Assert.Equal(0, body.Start);
        Assert.Null(body.Stop);
        Assert.Equal(3, body.BreakTarget);
        Assert.Equal(0, body.ContinueTarget);
        // A body that crosses the caller's boundary is owned at that boundary, and blocks the
        // boundary already claimed leave the body's allowed set.
        int[] owned = crossesBoundary ? [0, 1, 2] : [];
        Assert.Equal(owned, state.BoundaryOwnedBlocks.Order());
        Assert.Equal(!crossesBoundary, body.Allowed.Contains(1));
        Assert.Equal(99, Assert.Single(state.LoopExitTargets));
        Assert.Equal(101, Assert.Single(state.LoopContinueTargets));
        Assert.Empty(state.SuppressedLoopHeaders);
    }

    private sealed class RecordingLoopBuilder : IStructuredControlFlowBuilder
    {
        internal List<Call> Calls { get; } = [];

        public StructuredSequenceDraft Build(ControlFlowStructuringState state,
            int? start, int? stop, ImmutableHashSet<int> allowed, HashSet<int> path)
        {
            Calls.Add(new(start, stop, allowed,
                state.LoopExitTargets.Peek(), state.LoopContinueTargets.Peek()));
            return new StructuredSequenceDraft([]);
        }

        public StructuredDispatcherDraft Build(ControlFlowStructuringState state,
            int? entry, ImmutableHashSet<int> component, ImmutableHashSet<int> allowed,
            int? exitStop, HashSet<int> path) => throw new InvalidOperationException();

        internal sealed record Call(int? Start, int? Stop, ImmutableHashSet<int> Allowed,
            int BreakTarget, int ContinueTarget);
    }

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var clipper = new ActiveControlFlowBlockClipper();
        var retention = new EmptyLoopActiveBlockRetentionSelector();

        Assert.Throws<ArgumentNullException>(() => new DispatcherSequenceStepExecutor(
            null!,
            retention));
        Assert.Throws<ArgumentNullException>(() => new DispatcherSequenceStepExecutor(
            clipper,
            null!));
    }

    private sealed class EmptyLoopActiveBlockRetentionSelector : ILoopActiveBlockRetentionSelector
    {
        public ImmutableHashSet<int> Select(
            ControlFlowStructuringState state,
            LoopRegion loop)
        {
            return ImmutableHashSet<int>.Empty;
        }
    }

}
