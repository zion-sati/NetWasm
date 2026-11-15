// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class LoopContinueTargetFinderTests
{
    [Fact]
    public void RequiresBothAnalysisCapabilities()
    {
        Assert.Throws<ArgumentNullException>(() => new LoopContinueTargetFinder(null!, new Reachability(false)));
        Assert.Throws<ArgumentNullException>(() => new LoopContinueTargetFinder(new Joins(4), null!));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 4 })]
    [InlineData(new[] { 4, 4 })]
    [InlineData(new[] { 7, 8 })]
    public void KeepsHeaderWithoutDistinctInternalContinuationChoices(int[] leaves)
    {
        var state = CreateState(leaves);
        var joins = new Joins(4);
        var reachable = new Reachability(false);
        var finder = AsFinder(new LoopContinueTargetFinder(joins, reachable));

        Assert.Equal(0, finder.Find(state, state.Graph, [0, 1, 2, 3, 4, 5, 6], 0));
        Assert.Equal(0, joins.Calls);
        Assert.Empty(reachable.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void KeepsHeaderWhenNoSeparateJoinIsSelected(int? join)
    {
        var state = CreateState([4, 6, 7]);
        var joins = new Joins(join);
        var reachable = new Reachability(false);
        var finder = AsFinder(new LoopContinueTargetFinder(joins, reachable));

        Assert.Equal(0, finder.Find(state, state.Graph, [0, 1, 2, 3, 4, 5, 6], 0));
        Assert.Equal([4, 6], joins.Starts);
        Assert.Equal(1, joins.Calls);
        Assert.Empty(reachable.Calls);
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 0)]
    public void SeparateContinuePointMustCutEveryBackEdgeIncludingExceptionSummaries(bool bypass, int expected)
    {
        var state = CreateState([4, 6, 7]);
        var joins = new Joins(4);
        var reachable = new Reachability(bypass);
        var finder = AsFinder(new LoopContinueTargetFinder(joins, reachable));
        var component = ImmutableHashSet.Create(0, 1, 2, 3, 4, 5, 6);

        Assert.Equal(expected, finder.Find(state, state.Graph, component, 0));
        Assert.Equal([1, 2], reachable.Calls.Select(call => call.Start));
        Assert.All(reachable.Calls, call =>
        {
            Assert.Same(state.ContinuationGraph, call.Graph);
            Assert.Equal([4], call.Stops);
            Assert.Same(component, call.Allowed);
        });
        Assert.Same(state, joins.State);
        Assert.Same(component, joins.Allowed);
        Assert.Equal(0, joins.Stop);
    }

    private static ILoopContinueTargetFinder AsFinder(ILoopContinueTargetFinder finder) => finder;

    private static ControlFlowStructuringState CreateState(int[] leaves)
    {
        var instructions = Enumerable.Range(0, 9).Select(index => new CilInstruction(index, index + 1,
            index >= 3 && index < 3 + leaves.Length ? CilOperation.Leave : index == 8 ? CilOperation.Return : CilOperation.Nop,
            index >= 3 && index < 3 + leaves.Length ? new CilOperand.BranchTarget(leaves[index - 3]) : new CilOperand.None())).ToImmutableArray();
        var method = new MethodDefinitionModel(new EntityKey(default, 1), new EntityKey(default, 2),
            "Loop", true, MethodSignatureModel.Create(CliValueKind.Void), 1);
        var body = new CilMethodBody(method, 0, [], instructions);
        var blocks = instructions.Select((instruction, index) => new BasicBlock(index, index, [instruction])).ToImmutableArray();
        var edges = blocks.ToImmutableDictionary(block => block.Index, _ => ImmutableArray<int>.Empty);
        var reachable = blocks.Select(block => block.Index).ToImmutableHashSet();
        var graph = new ControlFlowGraph(body, blocks, edges.SetItem(0, [1, 8]), edges, edges, edges, reachable);
        var summary = new ControlFlowGraph(body, blocks, edges.SetItem(0, [1, 2, 8]), edges, edges, edges, reachable);
        return new ControlFlowStructuringState(new ValidatedControlFlowGraph(graph, [], [])) { ContinuationGraph = summary };
    }

    private sealed class Joins(int? result) : ICommonReachableBlockFinder
    {
        public int Calls { get; private set; }
        public int[] Starts { get; private set; } = [];
        public ControlFlowStructuringState? State { get; private set; }
        public ImmutableHashSet<int>? Allowed { get; private set; }
        public int? Stop { get; private set; }

        public int? Find(ControlFlowStructuringState state, int[] starts, int? stop, ImmutableHashSet<int> allowed)
        {
            Calls++;
            Starts = starts;
            State = state;
            Stop = stop;
            Allowed = allowed;
            return result;
        }

        public int? Find(ControlFlowStructuringState state, int first, int second, int? stop, ImmutableHashSet<int> allowed) =>
            throw new InvalidOperationException();
    }

    private sealed class Reachability(bool bypass) : IReachableBlockFinder
    {
        public List<(ControlFlowGraph Graph, int Start, ImmutableHashSet<int> Stops, ImmutableHashSet<int> Allowed)> Calls { get; } = [];

        public ImmutableHashSet<int> Find(ControlFlowGraph graph, int entry, ImmutableHashSet<int> stops, ImmutableHashSet<int> allowed)
        {
            Calls.Add((graph, entry, stops, allowed));
            return bypass && entry == 2 ? [0] : [];
        }

        public ImmutableHashSet<int> Find(ControlFlowGraph graph, int entry, ImmutableHashSet<int> allowed) => throw new InvalidOperationException();
        public ImmutableHashSet<int> Find(ControlFlowStructuringState state, int start, int? stop, ImmutableHashSet<int> allowed) => throw new InvalidOperationException();
    }
}
