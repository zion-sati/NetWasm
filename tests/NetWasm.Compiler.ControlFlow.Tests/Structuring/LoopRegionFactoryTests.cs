using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class LoopRegionFactoryTests
{
    [Fact]
    public void CreateRejectsOverlappingBodyAndConditionExitDomains()
    {
        var state = CreateState();
        var overlaps = new RecordingOverlapClassifier(result: true);
        ILoopExitExtensionCollector extensions = new OrderedExitExtensionCollector(
            ImmutableHashSet.Create(4),
            ImmutableHashSet.Create(4));
        var continues = new RecordingContinueTargetFinder(1);
        var factory = new LoopRegionFactory(
            overlaps,
            new FixedLoopConditionChooser(1),
            new FixedCommonReachableBlockFinder(5),
            new EmptyReachableBlockFinder(),
            new UnexpectedIrreducibleControlFlowExceptionFactory(),
            new LoopExitPartitioner(extensions, overlaps),
            continues);

        var result = Create(
            factory,
            state,
            state.Graph,
            ImmutableHashSet.Create(1, 2),
            new ControlFlowDomain(0, state.Graph.ReachableBlocks),
            [new ControlFlowDomain(0, state.Graph.ReachableBlocks)]);

        Assert.Null(result.Region);
        Assert.NotEmpty(result.DispatcherOwnedBlocks);
        Assert.Equal(
            overlaps.LastSets.SelectMany(blocks => blocks).ToImmutableHashSet(),
            result.DispatcherOwnedBlocks);
        Assert.Equal(1, overlaps.CallCount);
        Assert.Equal(0, continues.CallCount);
    }

    [Fact]
    public void CreateRejectsAnExternalEntryIntoTheMethodEntryComponent()
    {
        var state = CreateState();
        var overlaps = new RecordingOverlapClassifier(result: false);
        var factory = new LoopRegionFactory(
            overlaps,
            new FixedLoopConditionChooser(1),
            new FixedCommonReachableBlockFinder(5),
            new EmptyReachableBlockFinder(),
            new UnexpectedIrreducibleControlFlowExceptionFactory(),
            new LoopExitPartitioner(
                new OrderedExitExtensionCollector([], []),
                overlaps),
            new RecordingContinueTargetFinder(1));

        Assert.Throws<InvalidOperationException>(() => Create(
            factory,
            state,
            state.Graph,
            ImmutableHashSet.Create(1, 2),
            new ControlFlowDomain(1, state.Graph.ReachableBlocks),
            [new ControlFlowDomain(1, state.Graph.ReachableBlocks)]));
    }

    [Fact]
    public void CreateUsesAWholeComponentDispatcherWhenLoopHalvesOverlap()
    {
        var state = CreateState();
        var overlaps = new RecordingOverlapClassifier(result: true);
        var factory = new LoopRegionFactory(
            overlaps,
            new FixedLoopConditionChooser(2),
            new FixedCommonReachableBlockFinder(5),
            new EmptyReachableBlockFinder(),
            new UnexpectedIrreducibleControlFlowExceptionFactory(),
            new LoopExitPartitioner(
                new OrderedExitExtensionCollector([], []),
                overlaps),
            new RecordingContinueTargetFinder(1));
        var component = ImmutableHashSet.Create(1, 2);

        var result = Create(
            factory,
            state,
            state.Graph,
            component,
            new ControlFlowDomain(0, state.Graph.ReachableBlocks),
            [new ControlFlowDomain(0, state.Graph.ReachableBlocks)]);

        Assert.Null(result.Region);
        Assert.Equal(component, result.DispatcherOwnedBlocks);
    }

    [Fact]
    public void CreateKeepsDisjointBodyAndConditionExitDomains()
    {
        var state = CreateState();
        var overlaps = new RecordingOverlapClassifier(result: false);
        ILoopExitExtensionCollector extensions = new OrderedExitExtensionCollector(
            ImmutableHashSet.Create(4),
            ImmutableHashSet.Create(3));
        var continues = new RecordingContinueTargetFinder(2);
        var factory = new LoopRegionFactory(
            overlaps,
            new FixedLoopConditionChooser(1),
            new FixedCommonReachableBlockFinder(5),
            new EmptyReachableBlockFinder(),
            new UnexpectedIrreducibleControlFlowExceptionFactory(),
            new LoopExitPartitioner(extensions, overlaps),
            continues);

        var creation = Create(
            factory,
            state,
            state.Graph,
            ImmutableHashSet.Create(1, 2),
            new ControlFlowDomain(0, state.Graph.ReachableBlocks),
            [new ControlFlowDomain(0, state.Graph.ReachableBlocks)]);
        var result = Assert.IsType<LoopRegion>(creation.Region);
        Assert.Empty(creation.DispatcherOwnedBlocks);

        Assert.Equal(1, result.Header);
        Assert.Equal(1, result.Condition);
        Assert.Equal(2, result.Continue);
        Assert.Equal(3, result.ConditionExit);
        Assert.Equal(5, result.Exit);
        Assert.Equal(ImmutableHashSet.Create(1, 2, 4), result.BodyComponent);
        Assert.Equal(ImmutableHashSet.Create(3), result.ConditionExitComponent);
        Assert.Equal(1, continues.CallCount);
    }

    private static ControlFlowStructuringState CreateState()
    {
        var template = ControlFlowStructuringStateTestFactory.Create();
        var blocks = Enumerable.Range(0, 6)
            .Select(index => new BasicBlock(index, index, []))
            .ToImmutableArray();
        var successors = ImmutableDictionary<int, ImmutableArray<int>>.Empty
            .Add(0, [1])
            .Add(1, [2, 3])
            .Add(2, [1, 4])
            .Add(3, [5])
            .Add(4, [5])
            .Add(5, []);
        var predecessors = ImmutableDictionary<int, ImmutableArray<int>>.Empty
            .Add(0, [])
            .Add(1, [0, 2])
            .Add(2, [1])
            .Add(3, [1])
            .Add(4, [2])
            .Add(5, [3, 4]);
        var emptyEdges = Enumerable.Range(0, 6)
            .ToImmutableDictionary(index => index, _ => ImmutableArray<int>.Empty);
        var graph = new ControlFlowGraph(
            template.Graph.MethodBody,
            blocks,
            successors,
            predecessors,
            emptyEdges,
            emptyEdges,
            Enumerable.Range(0, 6).ToImmutableHashSet());
        return new ControlFlowStructuringState(new ValidatedControlFlowGraph(
            graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
    }

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var overlaps = new RecordingOverlapClassifier(result: false);
        var conditions = new FixedLoopConditionChooser(condition: 1);
        var joins = new FixedCommonReachableBlockFinder(result: null);
        var reachable = new EmptyReachableBlockFinder();
        var errors = new UnexpectedIrreducibleControlFlowExceptionFactory();
        var partitions = new LoopExitPartitioner(
            new OrderedExitExtensionCollector(
                ImmutableHashSet<int>.Empty,
                ImmutableHashSet<int>.Empty),
            overlaps);
        var continues = new RecordingContinueTargetFinder(result: 1);

        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            null!, conditions, joins, reachable, errors, partitions, continues));
        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            overlaps, null!, joins, reachable, errors, partitions, continues));
        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            overlaps, conditions, null!, reachable, errors, partitions, continues));
        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            overlaps, conditions, joins, null!, errors, partitions, continues));
        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            overlaps, conditions, joins, reachable, null!, partitions, continues));
        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            overlaps, conditions, joins, reachable, errors, null!, continues));
        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            overlaps, conditions, joins, reachable, errors, partitions, null!));
    }

    private static LoopRegionCreation Create<TFactory>(
        TFactory factory,
        ControlFlowStructuringState state,
        ControlFlowGraph graph,
        ImmutableHashSet<int> component,
        ControlFlowDomain domain,
        ImmutableArray<ControlFlowDomain> orderedChildDomains)
        where TFactory : ILoopRegionFactory
        => factory.Create(state, graph, component, domain, orderedChildDomains);

    private sealed class RecordingOverlapClassifier(bool result) :
        IReachableSetOverlapClassifier
    {
        public int CallCount { get; private set; }

        public ImmutableArray<ImmutableHashSet<int>> LastSets { get; private set; }

        public bool Overlaps(ImmutableArray<ImmutableHashSet<int>> reachableSets)
        {
            CallCount++;
            LastSets = reachableSets;
            return result;
        }
    }

    private sealed class FixedLoopConditionChooser(int condition) :
        ILoopConditionChooser
    {
        public int? Choose(LoopConditionSelection selection) => condition;
    }

    private sealed class FixedCommonReachableBlockFinder(int? result) :
        ICommonReachableBlockFinder
    {
        public int? Find(
            ControlFlowStructuringState state,
            int first,
            int second,
            int? stop,
            ImmutableHashSet<int> allowed) => result;

        public int? Find(
            ControlFlowStructuringState state,
            int[] starts,
            int? stop,
            ImmutableHashSet<int> allowed) => result;
    }

    private sealed class EmptyReachableBlockFinder : IReachableBlockFinder
    {
        public ImmutableHashSet<int> Find(
            ControlFlowStructuringState state,
            int start,
            int? stop,
            ImmutableHashSet<int> allowed) => [];

        public ImmutableHashSet<int> Find(
            ControlFlowGraph graph,
            int entry,
            ImmutableHashSet<int> allowed) => [];

        public ImmutableHashSet<int> Find(
            ControlFlowGraph graph,
            int entry,
            ImmutableHashSet<int> stops,
            ImmutableHashSet<int> allowed) => [];
    }

    private sealed class UnexpectedIrreducibleControlFlowExceptionFactory :
        IIrreducibleControlFlowExceptionFactory
    {
        public CompilerException Create(
            ControlFlowStructuringState state,
            string message) => throw new InvalidOperationException(
                "The test fixture must not request an irreducible-flow error.");
    }

    private sealed class OrderedExitExtensionCollector(
        ImmutableHashSet<int> first,
        ImmutableHashSet<int> second) : ILoopExitExtensionCollector
    {
        public int CallCount { get; private set; }

        public ImmutableHashSet<int> Collect(
            ControlFlowGraph graph,
            ImmutableHashSet<int> component,
            int primaryExit,
            ImmutableHashSet<int> additionalExits) =>
            CallCount++ == 0 ? first : second;
    }

    private sealed class RecordingContinueTargetFinder(int result) :
        ILoopContinueTargetFinder
    {
        public int CallCount { get; private set; }

        public int Find(
            ControlFlowStructuringState state,
            ControlFlowGraph graph,
            ImmutableHashSet<int> component,
            int header)
        {
            CallCount++;
            return result;
        }
    }
}
