// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ControlFlowSubgraphCycleClassifierTests
{
    [Theory]
    [InlineData(-1, false, 3)]
    [InlineData(0, true, 1)]
    [InlineData(1, true, 2)]
    [InlineData(2, true, 3)]
    public void ClassifyForwardsTheSelectedSubgraphAndStopsAtTheFirstCycle(
        int cyclicIndex, bool expected, int inspected)
    {
        var graph = CreateGraph();
        ImmutableHashSet<int> allowed = [1, 2, 3];
        ImmutableArray<ImmutableHashSet<int>> partitions = [[1], [2], [3]];
        var components = new Components(graph, allowed, partitions);
        var cycles = new Cycles(graph, partitions, cyclicIndex);

        Assert.Equal(expected, new ControlFlowSubgraphCycleClassifier(components, cycles).Classify(graph, allowed));
        Assert.Equal(1, components.Calls);
        Assert.Equal(inspected, cycles.Calls);
    }

    [Fact]
    public void EmptySubgraphDoesNotInvokeCycleClassification()
    {
        var graph = CreateGraph();
        var components = new Components(graph, [], []);
        var cycles = new Cycles(graph, [], -1);

        Assert.False(new ControlFlowSubgraphCycleClassifier(components, cycles).Classify(graph, []));
        Assert.Equal(1, components.Calls);
        Assert.Equal(0, cycles.Calls);
    }

    [Fact]
    public void MissingDependenciesAreRejected()
    {
        var graph = CreateGraph();
        Assert.Throws<ArgumentNullException>(() => new ControlFlowSubgraphCycleClassifier(null!, new Cycles(graph, [], -1)));
        Assert.Throws<ArgumentNullException>(() => new ControlFlowSubgraphCycleClassifier(new Components(graph, [], []), null!));
    }

    [Fact]
    public void MissingInputsAreRejectedBeforeAnalysis()
    {
        var graph = CreateGraph();
        var components = new Components(graph, [], []);
        var cycles = new Cycles(graph, [], -1);
        var actor = new ControlFlowSubgraphCycleClassifier(components, cycles);

        Assert.Throws<ArgumentNullException>(() => actor.Classify(null!, []));
        Assert.Throws<ArgumentNullException>(() => actor.Classify(graph, null!));
        Assert.Equal(0, components.Calls);
        Assert.Equal(0, cycles.Calls);
    }

    private static ControlFlowGraph CreateGraph() => new(
        ControlFlowTestSupport.Body(CliValueKind.Void, 0, [], ControlFlowTestSupport.I(0, CilOperation.Return)),
        [], ImmutableDictionary<int, ImmutableArray<int>>.Empty,
        ImmutableDictionary<int, ImmutableArray<int>>.Empty,
        ImmutableDictionary<int, ImmutableArray<int>>.Empty,
        ImmutableDictionary<int, ImmutableArray<int>>.Empty, []);

    private sealed class Components(ControlFlowGraph expectedGraph, ImmutableHashSet<int> expectedBlocks,
        ImmutableArray<ImmutableHashSet<int>> result) : IControlFlowComponentAnalyzer
    {
        public int Calls { get; private set; }

        public ImmutableArray<ImmutableHashSet<int>> Analyze(ControlFlowGraph graph, ImmutableHashSet<int> allowed)
        {
            Assert.Same(expectedGraph, graph);
            Assert.True(expectedBlocks.SetEquals(allowed));
            Calls++;
            return result;
        }
    }

    private sealed class Cycles(ControlFlowGraph expectedGraph, ImmutableArray<ImmutableHashSet<int>> partitions,
        int cyclicIndex) : IControlFlowCycleClassifier
    {
        public int Calls { get; private set; }

        public bool Classify(ControlFlowGraph graph, ImmutableHashSet<int> component)
        {
            Assert.Same(expectedGraph, graph);
            Assert.Same(partitions[Calls], component);
            return Calls++ == cyclicIndex;
        }
    }
}
