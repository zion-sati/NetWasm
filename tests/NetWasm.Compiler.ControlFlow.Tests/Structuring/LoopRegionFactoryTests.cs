// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class LoopRegionFactoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MethodEntryLoopsUseTheEntryHeaderForBothConditionPlacements(bool bottomTest)
    {
        var template = CreateState();
        var blocks = Enumerable.Range(0, 3).Select(index => new BasicBlock(index, index, [])).ToImmutableArray();
        var successors = ImmutableDictionary<int, ImmutableArray<int>>.Empty
            .Add(0, bottomTest ? [1] : [1, 2]).Add(1, bottomTest ? [0, 2] : [0]).Add(2, []);
        var predecessors = ImmutableDictionary<int, ImmutableArray<int>>.Empty
            .Add(0, [1]).Add(1, [0]).Add(2, bottomTest ? [1] : [0]);
        var empty = Enumerable.Range(0, 3).ToImmutableDictionary(index => index, _ => ImmutableArray<int>.Empty);
        var graph = new ControlFlowGraph(template.Graph.MethodBody, blocks, successors, predecessors,
            empty, empty, [0, 1, 2]);
        var state = new ControlFlowStructuringState(new ValidatedControlFlowGraph(graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
        var overlaps = new RecordingOverlapClassifier(false);
        var continues = new RecordingContinueTargetFinder(0);
        var factory = new LoopRegionFactory(overlaps, new FixedLoopConditionChooser(bottomTest ? 1 : 0),
            new FixedCommonReachableBlockFinder(2), new EmptyReachableBlockFinder(),
            new UnexpectedIrreducibleControlFlowExceptionFactory(),
            new LoopExitPartitioner(new OrderedExitExtensionCollector([], []), overlaps),
            continues, CreateEligibilityClassifier(), new LoopEntryTargetFinder());

        var result = Create(factory, state, graph, [0, 1], new(0, graph.ReachableBlocks), [new(0, graph.ReachableBlocks)]);

        var loop = Assert.IsType<LoopRegion>(result.Region);
        Assert.Equal(0, loop.Header);
        Assert.Equal(bottomTest ? 1 : 0, loop.Condition);
        Assert.Equal(0, loop.Continue);
        Assert.Equal(2, loop.Exit);
        Assert.Empty(result.DispatcherOwnedBlocks);
        Assert.Equal(bottomTest ? 0 : 1, continues.CallCount);
    }

    // A loop that starts a finally handler: its header is the handler's entry, not the method's, so the condition in
    // the handler is enclosed by the header and the loop is structured rather than left to a dispatcher.
    [Fact]
    public void AHandlerEntryLoopUsesTheHandlerEntryAsItsHeader()
    {
        var template = ControlFlowStructuringStateTestFactory.Create();
        // 0: try, leaves to 4. 1: handler entry and loop header. 2: condition, back to 1 or on to 3. 3: endfinally.
        var blocks = Enumerable.Range(0, 5).Select(index => new BasicBlock(index, index, [])).ToImmutableArray();
        var successors = ImmutableDictionary<int, ImmutableArray<int>>.Empty
            .Add(0, [4]).Add(1, [2]).Add(2, [1, 3]).Add(3, []).Add(4, []);
        var predecessors = ImmutableDictionary<int, ImmutableArray<int>>.Empty
            .Add(0, []).Add(1, [2]).Add(2, [1]).Add(3, [2]).Add(4, [0]);
        var empty = Enumerable.Range(0, 5).ToImmutableDictionary(index => index, _ => ImmutableArray<int>.Empty);
        var body = template.Graph.MethodBody with
        {
            ExceptionRegions = [new CilExceptionRegion(CilExceptionRegionKind.Finally, 0, 1, 1, 3, null, null)],
        };
        var graph = new ControlFlowGraph(body, blocks, successors, predecessors, empty, empty, [0, 1, 2, 3, 4]);
        var state = new ControlFlowStructuringState(new ValidatedControlFlowGraph(graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
        var overlaps = new RecordingOverlapClassifier(false);
        var factory = new LoopRegionFactory(overlaps, new FixedLoopConditionChooser(2),
            new FixedCommonReachableBlockFinder(3), new EmptyReachableBlockFinder(),
            new UnexpectedIrreducibleControlFlowExceptionFactory(),
            new LoopExitPartitioner(new OrderedExitExtensionCollector([], []), overlaps),
            new RecordingContinueTargetFinder(1), CreateEligibilityClassifier(), new LoopEntryTargetFinder());
        var handler = new ControlFlowDomain(1, [1, 2, 3]);

        var result = Create(factory, state, graph, [1, 2], handler, [new(0, [0, 4]), handler]);

        var loop = Assert.IsType<LoopRegion>(result.Region);
        Assert.Equal(1, loop.Header);
        Assert.Equal(2, loop.Condition);
        Assert.Equal(1, loop.Continue);
        Assert.Equal(3, loop.Exit);
        Assert.Empty(result.DispatcherOwnedBlocks);
    }

    [Fact]
    public void AComponentWithoutASelectedConditionRemainsDispatcherOwned()
    {
        var state = CreateState();
        var partitions = new RecordingExitPartitioner();
        var continues = new RecordingContinueTargetFinder(1);
        var factory = new LoopRegionFactory(new RecordingOverlapClassifier(false), new FixedLoopConditionChooser(null),
            new FixedCommonReachableBlockFinder(5), new EmptyReachableBlockFinder(),
            new UnexpectedIrreducibleControlFlowExceptionFactory(), partitions, continues, CreateEligibilityClassifier(), new LoopEntryTargetFinder());

        var result = Create(factory, state, state.Graph, [1, 2], new(0, state.Graph.ReachableBlocks), [new(0, state.Graph.ReachableBlocks)]);

        Assert.Null(result.Region);
        Assert.Equal(ImmutableHashSet.Create(1, 2), result.DispatcherOwnedBlocks);
        Assert.Null(partitions.Graph);
        Assert.Equal(0, continues.CallCount);
    }

    [Fact]
    public void AnIneligibleNaturalRegionRetainsTheOriginalDispatcherComponent()
    {
        var state = CreateState();
        var eligibility = new DecliningEligibility();
        var overlaps = new RecordingOverlapClassifier(false);
        var factory = new LoopRegionFactory(overlaps, new FixedLoopConditionChooser(1),
            new FixedCommonReachableBlockFinder(5), new EmptyReachableBlockFinder(),
            new UnexpectedIrreducibleControlFlowExceptionFactory(),
            new LoopExitPartitioner(new OrderedExitExtensionCollector([4], [3]), overlaps),
            new RecordingContinueTargetFinder(1), eligibility, new LoopEntryTargetFinder());

        var result = Create(factory, state, state.Graph, [1, 2], new(0, state.Graph.ReachableBlocks), [new(0, state.Graph.ReachableBlocks)]);

        Assert.Null(result.Region);
        Assert.Equal(ImmutableHashSet.Create(1, 2), result.DispatcherOwnedBlocks);
        Assert.Equal(1, eligibility.Request!.Region.Header);
        Assert.Contains(4, eligibility.Request.Region.BodyComponent);
    }

    private sealed class DecliningEligibility : ILoopRegionEligibilityClassifier
    {
        public LoopRegionEligibilityRequest? Request { get; private set; }
        public bool Classify(LoopRegionEligibilityRequest request)
        {
            Request = request;
            return false;
        }
    }

    [Fact]
    public void UnreachablePredecessorDoesNotCreateAnAdditionalLoopEntry()
    {
        var original = CreateState();
        var graph = new ControlFlowGraph(original.Graph.MethodBody,
            original.Graph.Blocks.Add(new BasicBlock(6, 6, [])),
            original.Graph.Successors.Add(6, [2]),
            original.Graph.Predecessors.Add(6, []).SetItem(2, [1, 6]),
            original.Graph.ExceptionalSuccessors.Add(6, []),
            original.Graph.ExceptionalPredecessors.Add(6, []),
            original.Graph.ReachableBlocks);
        var state = new ControlFlowStructuringState(new ValidatedControlFlowGraph(
            graph, ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
        var overlaps = new RecordingOverlapClassifier(false);
        var factory = new LoopRegionFactory(overlaps,
            new FixedLoopConditionChooser(1), new FixedCommonReachableBlockFinder(5),
            new EmptyReachableBlockFinder(), new UnexpectedIrreducibleControlFlowExceptionFactory(),
            new LoopExitPartitioner(new OrderedExitExtensionCollector([4], [3]), overlaps),
            new RecordingContinueTargetFinder(2), CreateEligibilityClassifier(), new LoopEntryTargetFinder());

        var result = Create(factory, state, graph, [1, 2],
            new ControlFlowDomain(0, graph.ReachableBlocks),
            [new ControlFlowDomain(0, graph.ReachableBlocks)]);

        Assert.Equal(1, Assert.IsType<LoopRegion>(result.Region).Header);
        Assert.Empty(result.DispatcherOwnedBlocks);
    }

    [Fact]
    public void ExitPlanningIncludesExceptionContinuationSummaryEdges()
    {
        var original = CreateState();
        var successors = original.Graph.Successors.SetItem(2, [1, 4, 5]);
        var summary = new ControlFlowGraph(original.Graph.MethodBody, original.Graph.Blocks,
            successors, original.Graph.Predecessors.SetItem(5, [2, 3, 4]),
            original.Graph.ExceptionalSuccessors, original.Graph.ExceptionalPredecessors,
            original.Graph.ReachableBlocks);
        var state = new ControlFlowStructuringState(original.Validated) { ContinuationGraph = summary };
        var partitions = new RecordingExitPartitioner();
        var factory = new LoopRegionFactory(new RecordingOverlapClassifier(false),
            new FixedLoopConditionChooser(1), new FixedCommonReachableBlockFinder(5),
            new EmptyReachableBlockFinder(), new UnexpectedIrreducibleControlFlowExceptionFactory(),
            partitions, new RecordingContinueTargetFinder(1), CreateEligibilityClassifier(), new LoopEntryTargetFinder());

        var result = Create(factory, state, state.Graph, [1, 2],
            new ControlFlowDomain(0, state.Graph.ReachableBlocks),
            [new ControlFlowDomain(0, state.Graph.ReachableBlocks)]);

        Assert.NotNull(result.Region);
        Assert.Same(summary, partitions.Graph);
        Assert.Equal(ImmutableHashSet.Create(4, 5), partitions.AdditionalExits);
        Assert.Equal(3, partitions.ConditionExit);
        Assert.Equal(5, partitions.Exit);
    }

    [Fact]
    public void AnUnconditionalLoopWhoseRegionsNeverLeaveItRemainsDispatcherOwned()
    {
        var state = CreateProtectedLoopState(1);
        var factory = CreateUnconditionalFactory(
            new FixedCommonReachableBlockFinder(null), new RecordingExitPartitioner());

        var result = CreateProtectedLoop(factory, state);

        Assert.Null(result.Region);
        Assert.Equal(ImmutableHashSet.Create(1, 2), result.DispatcherOwnedBlocks);
    }

    [Fact]
    public void AnUnconditionalLoopWithoutAJoinExitsAtItsFirstExit()
    {
        var state = CreateProtectedLoopState(1, 4, 5);
        var partitions = new RecordingExitPartitioner();
        var factory = CreateUnconditionalFactory(new FixedCommonReachableBlockFinder(null), partitions);

        CreateProtectedLoop(factory, state);

        // Every exit leaves through the protected region, so each is an early exit.
        Assert.Equal(4, partitions.Exit);
        Assert.Equal(4, partitions.ConditionExit);
        Assert.Equal(ImmutableHashSet.Create(4, 5), partitions.AdditionalExits);
    }

    [Fact]
    public void AnUnconditionalLoopWhoseExitPartitionOverlapsRemainsDispatcherOwned()
    {
        var state = CreateProtectedLoopState(1, 4);
        var factory = CreateUnconditionalFactory(
            new FixedCommonReachableBlockFinder(4), new OverlappingExitPartitioner());

        var result = CreateProtectedLoop(factory, state);

        Assert.Null(result.Region);
        Assert.Equal(ImmutableHashSet.Create(1, 2), result.DispatcherOwnedBlocks);
    }

    private static LoopRegionFactory CreateUnconditionalFactory(
        ICommonReachableBlockFinder joins,
        ILoopExitPartitioner partitions) =>
        new(new RecordingOverlapClassifier(false), new FixedLoopConditionChooser(null), joins,
            new EmptyReachableBlockFinder(), new UnexpectedIrreducibleControlFlowExceptionFactory(),
            partitions, new RecordingContinueTargetFinder(1), CreateEligibilityClassifier(), new LoopEntryTargetFinder());

    private static LoopRegionCreation CreateProtectedLoop(
        LoopRegionFactory factory,
        ControlFlowStructuringState state)
    {
        var domain = new ControlFlowDomain(0, state.Graph.ReachableBlocks);
        return Create(factory, state, state.Graph, [1, 2], domain, [domain]);
    }

    // Header 1 enters a protected region at block 2 that leaves to the given continuations.
    private static ControlFlowStructuringState CreateProtectedLoopState(params int[] continuations)
    {
        var template = ControlFlowStructuringStateTestFactory.Create();
        var body = template.Graph.MethodBody with
        {
            ExceptionRegions = [new CilExceptionRegion(CilExceptionRegionKind.Catch, 2, 1, 3, 1, null, null)],
        };
        var blocks = Enumerable.Range(0, 6)
            .Select(index => new BasicBlock(index, index, []))
            .ToImmutableArray();
        var successors = ImmutableDictionary<int, ImmutableArray<int>>.Empty
            .Add(0, [1]).Add(1, [2]).Add(2, [1]).Add(3, []).Add(4, []).Add(5, []);
        var predecessors = ImmutableDictionary<int, ImmutableArray<int>>.Empty
            .Add(0, []).Add(1, [0, 2]).Add(2, [1]).Add(3, []).Add(4, []).Add(5, []);
        var emptyEdges = Enumerable.Range(0, 6)
            .ToImmutableDictionary(index => index, _ => ImmutableArray<int>.Empty);
        var reachable = Enumerable.Range(0, 6).ToImmutableHashSet();
        var graph = new ControlFlowGraph(
            body, blocks, successors, predecessors, emptyEdges, emptyEdges, reachable);
        return new ControlFlowStructuringState(new ValidatedControlFlowGraph(
            graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty))
        {
            ContinuationGraph = new ControlFlowGraph(body, blocks,
                successors.SetItem(2, [.. continuations]), predecessors, emptyEdges, emptyEdges,
                reachable),
        };
    }

    private sealed class OverlappingExitPartitioner : ILoopExitPartitioner
    {
        public LoopExitPartition Partition(ControlFlowGraph graph, ImmutableHashSet<int> component,
            int exit, int conditionExit, ImmutableHashSet<int> additionalExits) =>
            new([], component, true);
    }

    private sealed class RecordingExitPartitioner : ILoopExitPartitioner
    {
        public ControlFlowGraph? Graph { get; private set; }
        public ImmutableHashSet<int> AdditionalExits { get; private set; } = [];
        public int ConditionExit { get; private set; }
        public int Exit { get; private set; }

        public LoopExitPartition Partition(ControlFlowGraph graph, ImmutableHashSet<int> component,
            int exit, int conditionExit, ImmutableHashSet<int> additionalExits)
        {
            Graph = graph;
            AdditionalExits = additionalExits;
            ConditionExit = conditionExit;
            Exit = exit;
            return new([3], component.Union([4]), false);
        }
    }

    [Theory]
    [InlineData(BranchPrefixDuplicationPolicy.Disabled, false)]
    [InlineData(BranchPrefixDuplicationPolicy.SizeAware, false)]
    [InlineData(BranchPrefixDuplicationPolicy.Speed, true)]
    public void SharedExitExtensionsFollowTheDuplicationPolicy(
        BranchPrefixDuplicationPolicy policy, bool expected)
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
            continues,
            CreateEligibilityClassifier(), new LoopEntryTargetFinder(), policy);

        var result = Create(
            factory,
            state,
            state.Graph,
            ImmutableHashSet.Create(1, 2),
            new ControlFlowDomain(0, state.Graph.ReachableBlocks),
            [new ControlFlowDomain(0, state.Graph.ReachableBlocks)]);

        Assert.Null(result.Region);
        if (expected)
        {
            Assert.Equal(ImmutableHashSet.Create(1, 2), result.DispatcherOwnedBlocks);
        }
        else
        {
            Assert.Equal(
                overlaps.LastSets.SelectMany(blocks => blocks).ToImmutableHashSet(),
                result.DispatcherOwnedBlocks);
        }
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
            new RecordingContinueTargetFinder(1),
            CreateEligibilityClassifier(), new LoopEntryTargetFinder());

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
            new RecordingContinueTargetFinder(1),
            CreateEligibilityClassifier(), new LoopEntryTargetFinder());
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
            continues,
            CreateEligibilityClassifier(), new LoopEntryTargetFinder());

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
        var eligibility = CreateEligibilityClassifier();
        var entryTargets = new LoopEntryTargetFinder();

        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            null!, conditions, joins, reachable, errors, partitions, continues,
            eligibility, entryTargets));
        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            overlaps, null!, joins, reachable, errors, partitions, continues,
            eligibility, entryTargets));
        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            overlaps, conditions, null!, reachable, errors, partitions, continues,
            eligibility, entryTargets));
        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            overlaps, conditions, joins, null!, errors, partitions, continues,
            eligibility, entryTargets));
        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            overlaps, conditions, joins, reachable, null!, partitions, continues,
            eligibility, entryTargets));
        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            overlaps, conditions, joins, reachable, errors, null!, continues,
            eligibility, entryTargets));
        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            overlaps, conditions, joins, reachable, errors, partitions, null!,
            eligibility, entryTargets));
        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            overlaps, conditions, joins, reachable, errors, partitions, continues,
            null!, entryTargets));
        Assert.Throws<ArgumentNullException>(() => new LoopRegionFactory(
            overlaps, conditions, joins, reachable, errors, partitions, continues,
            eligibility, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoopRegionFactory(
            overlaps, conditions, joins, reachable, errors, partitions, continues,
            eligibility, entryTargets, (BranchPrefixDuplicationPolicy)int.MaxValue));
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

    private static LoopRegionEligibilityClassifier
        CreateEligibilityClassifier() =>
        new(new ControlFlowSubgraphCycleClassifier(
            new ControlFlowComponentAnalyzer(),
            new ControlFlowCycleClassifier()));

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

    private sealed class FixedLoopConditionChooser(int? condition) :
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
