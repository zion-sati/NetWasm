// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Xunit;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class DispatcherFallthroughExitClassifierTests
{
    [Fact]
    public void ClassifiesAFallthroughOutOfTheAllowedBlocksAsEndingTheSequence()
    {
        // Block 1 falls through to block 3, outside the allowed blocks, as a protected
        // code part falls into the nested try that follows it.
        var state = State(
            (CilOperation.BranchIfTrue, [1, 2]),
            (CilOperation.Nop, [3]),
            (CilOperation.Leave, [4]),
            (CilOperation.Return, []),
            (CilOperation.Return, []));
        var classifier = Assert.IsAssignableFrom<IDispatcherFallthroughExitClassifier>(
            new DispatcherFallthroughExitClassifier());

        Assert.True(classifier.Classify(state, [0, 1], [0, 1, 2]));
    }

    [Fact]
    public void LeavesAndSuccessorsInsideTheAllowedBlocksDoNotEndTheSequence()
    {
        var state = State(
            (CilOperation.BranchIfTrue, [1, 2]),
            (CilOperation.Branch, [0]),
            (CilOperation.Leave, [4]),
            (CilOperation.Return, []),
            (CilOperation.Return, []));
        var classifier = Assert.IsAssignableFrom<IDispatcherFallthroughExitClassifier>(
            new DispatcherFallthroughExitClassifier());

        // Block 0 exits to the allowed block 2 and block 1 returns to the dispatcher;
        // block 2's leave to block 4 runs through its exception region.
        Assert.False(classifier.Classify(state, [0, 1], [0, 1, 2]));
        Assert.False(classifier.Classify(state, [0, 1, 2], [0, 1, 2]));
    }

    [Fact]
    public void OnlyTheInnermostLoopBreakAndContinueTargetsKeepTheirTransfers()
    {
        var state = State(
            (CilOperation.BranchIfTrue, [1, 2]),
            (CilOperation.Nop, [3]),
            (CilOperation.Nop, [4]),
            (CilOperation.Return, []),
            (CilOperation.Return, []));
        var classifier = Assert.IsAssignableFrom<IDispatcherFallthroughExitClassifier>(
            new DispatcherFallthroughExitClassifier());
        state.LoopExitTargets.Push(3);
        state.LoopContinueTargets.Push(4);

        Assert.False(classifier.Classify(state, [0, 1, 2], [0, 1, 2]));

        // An outer loop's targets are not reached by a break or continue from here.
        state.LoopExitTargets.Push(2);
        Assert.True(classifier.Classify(state, [0, 1, 2], [0, 1, 2]));
        state.LoopExitTargets.Pop();
        state.LoopContinueTargets.Push(2);
        Assert.True(classifier.Classify(state, [0, 1, 2], [0, 1, 2]));
    }

    [Fact]
    public void ClassifyRejectsMissingArguments()
    {
        var state = State((CilOperation.Return, []));
        var classifier = Assert.IsAssignableFrom<IDispatcherFallthroughExitClassifier>(
            new DispatcherFallthroughExitClassifier());

        Assert.Equal("state", Assert.Throws<ArgumentNullException>(() =>
            classifier.Classify(null!, [], [])).ParamName);
        Assert.Equal("dispatcher", Assert.Throws<ArgumentNullException>(() =>
            classifier.Classify(state, null!, [])).ParamName);
        Assert.Equal("allowed", Assert.Throws<ArgumentNullException>(() =>
            classifier.Classify(state, [], null!)).ParamName);
    }

    private static ControlFlowStructuringState State(
        params (CilOperation Terminator, int[] Successors)[] blocks)
    {
        var instructions = blocks
            .Select((block, index) => new CilInstruction(
                index,
                1,
                block.Terminator,
                new CilOperand.None()))
            .ToArray();
        var graph = new ControlFlowGraph(
            Body(CliValueKind.Void, 0, [], instructions),
            [.. instructions.Select((instruction, index) =>
                new BasicBlock(index, index, [instruction]))],
            blocks
                .Select((block, index) => (index, block.Successors))
                .ToImmutableDictionary(
                    item => item.index,
                    item => item.Successors.ToImmutableArray()),
            ImmutableDictionary<int, ImmutableArray<int>>.Empty,
            ImmutableDictionary<int, ImmutableArray<int>>.Empty,
            ImmutableDictionary<int, ImmutableArray<int>>.Empty,
            [.. Enumerable.Range(0, blocks.Length)]);
        return new ControlFlowStructuringState(new ValidatedControlFlowGraph(
            graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
    }
}
