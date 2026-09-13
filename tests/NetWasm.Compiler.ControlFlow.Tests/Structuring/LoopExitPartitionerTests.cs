using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ControlFlow.Structuring;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class LoopExitPartitionerTests
{
    [Fact]
    public void PartitionBuildsTheSingleBodyAndExitOwnershipDecision()
    {
        var extensions = new QueuedLoopExitExtensionCollector(
            ImmutableHashSet.Create(4, 5),
            ImmutableHashSet.Create(5, 6));
        var overlaps = new RecordingReachableSetOverlapClassifier(result: true);
        var partitioner = Assert.IsAssignableFrom<ILoopExitPartitioner>(
            new LoopExitPartitioner(extensions, overlaps));
        var component = ImmutableHashSet.Create(1, 2);
        var additionalExits = ImmutableHashSet.Create(7, 8);

        var partition = partitioner.Partition(
            ControlFlowStructuringStateTestFactory.Create().Graph,
            component,
            exit: 9,
            conditionExit: 10,
            additionalExits);

        Assert.True(partition.Overlaps);
        Assert.Equal(ImmutableHashSet.Create(1, 2, 4, 5), partition.BodyComponent);
        Assert.Equal(ImmutableHashSet.Create(5, 6), partition.ConditionExitExtension);
        Assert.Equal(2, extensions.CallCount);
        Assert.Equal(additionalExits, extensions.AdditionalExits[0]);
        Assert.Equal(ImmutableHashSet.Create(10), extensions.AdditionalExits[1]);
        Assert.Equal(1, overlaps.CallCount);
        Assert.Equal(partition.BodyComponent, overlaps.LastSets[0]);
        Assert.Equal(partition.ConditionExitExtension, overlaps.LastSets[1]);
    }

    [Fact]
    public void PartitionReportsDisjointOwnership()
    {
        var partitioner = Assert.IsAssignableFrom<ILoopExitPartitioner>(
            new LoopExitPartitioner(
                new QueuedLoopExitExtensionCollector(
                    ImmutableHashSet<int>.Empty,
                    ImmutableHashSet<int>.Empty),
                new RecordingReachableSetOverlapClassifier(result: false)));

        var partition = partitioner.Partition(
            ControlFlowStructuringStateTestFactory.Create().Graph,
            ImmutableHashSet.Create(1),
            exit: 2,
            conditionExit: 3,
            ImmutableHashSet<int>.Empty);

        Assert.False(partition.Overlaps);
        Assert.Equal(ImmutableHashSet.Create(1), partition.BodyComponent);
        Assert.Empty(partition.ConditionExitExtension);
    }

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var extensions = new QueuedLoopExitExtensionCollector(
            ImmutableHashSet<int>.Empty,
            ImmutableHashSet<int>.Empty);
        var overlaps = new RecordingReachableSetOverlapClassifier(result: false);

        Assert.Throws<ArgumentNullException>(() => new LoopExitPartitioner(null!, overlaps));
        Assert.Throws<ArgumentNullException>(() => new LoopExitPartitioner(extensions, null!));
    }

    [Fact]
    public void PartitionRejectsNullInputs()
    {
        var partitioner = Assert.IsAssignableFrom<ILoopExitPartitioner>(
            new LoopExitPartitioner(
                new QueuedLoopExitExtensionCollector(
                    ImmutableHashSet<int>.Empty,
                    ImmutableHashSet<int>.Empty),
                new RecordingReachableSetOverlapClassifier(result: false)));
        var graph = ControlFlowStructuringStateTestFactory.Create().Graph;
        var component = ImmutableHashSet.Create(1);
        var exits = ImmutableHashSet<int>.Empty;

        Assert.Throws<ArgumentNullException>(() => partitioner.Partition(
            null!, component, 2, 3, exits));
        Assert.Throws<ArgumentNullException>(() => partitioner.Partition(
            graph, null!, 2, 3, exits));
        Assert.Throws<ArgumentNullException>(() => partitioner.Partition(
            graph, component, 2, 3, null!));
    }

    private sealed class QueuedLoopExitExtensionCollector(
        params ImmutableHashSet<int>[] results) : ILoopExitExtensionCollector
    {
        private readonly Queue<ImmutableHashSet<int>> _results = new(results);

        public int CallCount { get; private set; }

        public List<ImmutableHashSet<int>> AdditionalExits { get; } = [];

        public ImmutableHashSet<int> Collect(
            ControlFlowGraph graph,
            ImmutableHashSet<int> component,
            int primaryExit,
            ImmutableHashSet<int> additionalExits)
        {
            CallCount++;
            AdditionalExits.Add(additionalExits);
            return _results.Dequeue();
        }
    }

    private sealed class RecordingReachableSetOverlapClassifier(bool result)
        : IReachableSetOverlapClassifier
    {
        public int CallCount { get; private set; }

        public ImmutableArray<ImmutableHashSet<int>> LastSets { get; private set; } = [];

        public bool Overlaps(ImmutableArray<ImmutableHashSet<int>> reachableSets)
        {
            CallCount++;
            LastSets = reachableSets;
            return result;
        }
    }
}
