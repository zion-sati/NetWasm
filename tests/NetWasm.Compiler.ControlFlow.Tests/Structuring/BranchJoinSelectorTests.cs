// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class BranchJoinSelectorTests
{
    [Theory]
    [InlineData(9, null, 9)]
    [InlineData(4, 4, 9)]
    [InlineData(4, null, null)]
    public void SelectDistinguishesStopAndLoopTransfersFromDisallowedEntry(
        int first, int? continueTarget, int? expectedJoin)
    {
        var state = CreateState(Edges((1, [9]), (4, []), (9, [])));
        var distances = new ControlFlowDistanceFinder();
        var reachable = new ReachableBlockFinder(distances);
        var selector = Assert.IsAssignableFrom<IBranchJoinSelector>(new BranchJoinSelector(
            new FixedJoinFinder(null), reachable, distances,
            new BranchReachabilityOverlapClassifier(reachable), CreateCycleClassifier()));

        var result = selector.Select(new(state, first, 1, 9,
            state.Graph.ReachableBlocks.Remove(4), continueTarget, null));

        Assert.Equal(expectedJoin, result.Join);
        Assert.Empty(result.DuplicatedPrefixBlocks);
    }

    [Fact]
    public void SelectPreservesCallerStopWithoutDuplicatingASharedRegionExit()
    {
        var state = CreateState(Edges((1, [3]), (2, [3]), (3, [7, 6]), (6, []), (7, [])));
        var distances = new ControlFlowDistanceFinder();
        var reachable = new ReachableBlockFinder(distances);
        var selector = Assert.IsAssignableFrom<IBranchJoinSelector>(new BranchJoinSelector(
            new FixedJoinFinder(7), reachable,
            new MappedDistanceFinder((1, [(7, 2)]), (2, [(7, 2)])),
            new BranchReachabilityOverlapClassifier(reachable), CreateCycleClassifier()));

        var result = selector.Select(new(state, 1, 2, 7,
            state.Graph.ReachableBlocks.Remove(6), null, null));

        Assert.Equal(7, result.Join);
        Assert.Empty(result.DuplicatedPrefixBlocks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectPreservesTheStopWhenAnotherArmLeavesTheAllowedRegion(bool swapBranches)
    {
        var state = CreateState(Edges(
            (1, [4]),
            (2, [8]),
            (4, []),
            (8, [9]),
            (9, [1, 2])));
        var distances = new ControlFlowDistanceFinder();
        var reachable = new ReachableBlockFinder(distances);
        var selector = Assert.IsAssignableFrom<IBranchJoinSelector>(new BranchJoinSelector(
            new FixedJoinFinder(null),
            reachable,
            distances,
            new BranchReachabilityOverlapClassifier(reachable),
            CreateCycleClassifier()));
        var allowed = state.Graph.ReachableBlocks.Remove(4);

        var result = selector.Select(new(
            state,
            swapBranches ? 2 : 1,
            swapBranches ? 1 : 2,
            9,
            allowed,
            null,
            null));

        Assert.Equal(9, result.Join);
        Assert.Equal(allowed, result.AnalysisBlocks);
        Assert.Empty(result.DuplicatedPrefixBlocks);
        Assert.Empty(result.TerminalTransfers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectPreservesTheCallerStopWhenAnotherCandidateAbsorbsItsContinuation(
        bool swapBranches)
    {
        var state = CreateState(Edges(
            (1, [3, 5]),
            (3, []),
            (5, [7]),
            (7, [])));
        var distances = new ControlFlowDistanceFinder();
        var reachable = new ReachableBlockFinder(distances);
        var selector = Assert.IsAssignableFrom<IBranchJoinSelector>(new BranchJoinSelector(
            new FixedJoinFinder(null),
            reachable,
            distances,
            new BranchReachabilityOverlapClassifier(reachable),
            CreateCycleClassifier()));

        var result = selector.Select(new(
            state,
            swapBranches ? 1 : 3,
            swapBranches ? 3 : 1,
            5,
            state.Graph.ReachableBlocks,
            null,
            null));

        Assert.Equal(5, result.Join);
        Assert.Empty(result.DuplicatedPrefixBlocks);
    }

    [Fact]
    public void SelectUsesAnEarlierSafeConvergenceThanTheCallerStop()
    {
        var state = CreateState(Edges(
            (1, [7]),
            (2, [7]),
            (7, [8]),
            (8, [9]),
            (9, [])));
        var reachable = new MappedReachableBlockFinder(
            ((1, 9), [1, 7, 8]),
            ((2, 9), [2, 7, 8]),
            ((1, 7), [1]),
            ((2, 7), [2]),
            ((7, 9), [7, 8]));
        var selector = CreateSelector(
            join: 7,
            reachable,
            new MappedDistanceFinder((1, [(7, 1)]), (2, [(7, 1)])),
            overlaps: true);

        var result = selector.Select(new(
            state,
            1,
            2,
            9,
            state.Graph.ReachableBlocks,
            null,
            null));

        Assert.Equal(7, result.Join);
        Assert.Empty(result.DuplicatedPrefixBlocks);
    }

    [Fact]
    public void SelectAcceptsAStopWhenTheOtherArmTerminatesAtALoopTransfer()
    {
        var state = CreateState(Edges(
            (1, [4]),
            (2, [8]),
            (4, []),
            (8, [9]),
            (9, [])));
        var reachable = new MappedReachableBlockFinder(
            ((1, 9), [1]),
            ((2, 9), [2, 8]));
        var selector = CreateSelector(
            join: null,
            reachable,
            new MappedDistanceFinder(),
            overlaps: false);

        var result = selector.Select(new(
            state,
            1,
            2,
            9,
            state.Graph.ReachableBlocks,
            4,
            null));

        Assert.Equal(9, result.Join);
        Assert.DoesNotContain(4, result.AnalysisBlocks);
        Assert.Equal(ImmutableHashSet.Create(4), result.TerminalTransfers);
    }

    [Fact]
    public void SelectReturnsABoundedAcyclicSharedPrefix()
    {
        var state = CreateState(SharedPrefixEdges());
        var selector = CreateSharedPrefixSelector(state, hasExceptions: false);

        var result = selector.Select(Request(state));

        Assert.Equal(7, result.Join);
        Assert.Equal(ImmutableHashSet.Create(3, 4), result.DuplicatedPrefixBlocks);
    }

    [Fact]
    public void SelectKeepsTheExistingJoinWhenTheSharedPrefixIsCyclic()
    {
        var state = CreateState(Edges(
            (1, [3]),
            (2, [3]),
            (3, [4]),
            (4, [3]),
            (7, [9]),
            (9, [])));
        var selector = CreateSharedPrefixSelector(state, hasExceptions: false);

        var result = selector.Select(Request(state));

        Assert.Equal(9, result.Join);
        Assert.Empty(result.DuplicatedPrefixBlocks);
    }

    [Fact]
    public void SelectKeepsTheExistingJoinWhenTheSharedPrefixEscapes()
    {
        var state = CreateState(Edges(
            (1, [3]),
            (2, [3]),
            (3, [4]),
            (4, [6]),
            (6, []),
            (7, [9]),
            (9, [])));
        var selector = CreateSharedPrefixSelector(state, hasExceptions: false);

        var result = selector.Select(Request(state));

        Assert.Equal(9, result.Join);
        Assert.Empty(result.DuplicatedPrefixBlocks);
    }

    [Fact]
    public void SizeAwareSelectionKeepsTheExistingJoinWhenTheSharedPrefixExceedsTheBound()
    {
        var state = CreateState(Edges(
            (1, [3]),
            (2, [3]),
            (3, [4]),
            (4, [5]),
            (5, [6]),
            (6, [7]),
            (7, [9]),
            (9, [])));
        var selector = CreateSelector(
            join: 9,
            new MappedReachableBlockFinder(
                ((1, 7), [1, 3, 4, 5, 6]),
                ((2, 7), [2, 3, 4, 5, 6]),
                ((7, null), [7])),
            new MappedDistanceFinder((1, [(7, 5)]), (2, [(7, 5)])),
            overlaps: true,
            duplicationPolicy: BranchPrefixDuplicationPolicy.SizeAware);

        var result = selector.Select(Request(state));

        Assert.Equal(9, result.Join);
        Assert.Empty(result.DuplicatedPrefixBlocks);
    }

    [Fact]
    public void SpeedSelectionAcceptsAnyStructurallySafeSharedPrefix()
    {
        var state = CreateState(Edges(
            (1, [3]),
            (2, [3]),
            (3, [4]),
            (4, [5]),
            (5, [6]),
            (6, [7]),
            (7, [9]),
            (9, [])));
        var selector = CreateSelector(
            join: 9,
            new MappedReachableBlockFinder(
                ((1, 7), [1, 3, 4, 5, 6]),
                ((2, 7), [2, 3, 4, 5, 6]),
                ((7, null), [7])),
            new MappedDistanceFinder((1, [(7, 5)]), (2, [(7, 5)])),
            overlaps: true,
            duplicationPolicy: BranchPrefixDuplicationPolicy.Speed);

        var result = selector.Select(Request(state));

        Assert.Equal(7, result.Join);
        Assert.Equal(
            ImmutableHashSet.Create(3, 4, 5, 6),
            result.DuplicatedPrefixBlocks);
    }

    [Fact]
    public void DisabledSelectionRejectsAnOtherwiseSafeSharedPrefix()
    {
        var state = CreateState(SharedPrefixEdges());
        var selector = CreateSelector(
            join: 9,
            new MappedReachableBlockFinder(
                ((1, 7), [1, 3, 4]),
                ((2, 7), [2, 3, 4]),
                ((7, null), [7])),
            new MappedDistanceFinder((1, [(7, 3)]), (2, [(7, 3)])),
            overlaps: true,
            duplicationPolicy: BranchPrefixDuplicationPolicy.Disabled);

        var result = selector.Select(Request(state));

        Assert.Equal(9, result.Join);
        Assert.Empty(result.DuplicatedPrefixBlocks);
    }

    [Fact]
    public void SelectKeepsTheExistingJoinWhenTheSharedPrefixOwnsANestedLoop()
    {
        var state = CreateState(SharedPrefixEdges());
        state.LoopsByHeader[3] = new LoopRegion(
            3,
            3,
            3,
            7,
            9,
            [3],
            [3],
            []);
        var selector = CreateSharedPrefixSelector(state, hasExceptions: false);

        var result = selector.Select(Request(state));

        Assert.Equal(9, result.Join);
        Assert.Empty(result.DuplicatedPrefixBlocks);
    }

    [Fact]
    public void SelectKeepsTheExistingJoinAcrossExceptionRegions()
    {
        var state = CreateState(SharedPrefixEdges(), hasExceptions: true);
        var selector = CreateSharedPrefixSelector(state, hasExceptions: true);

        var result = selector.Select(Request(state));

        Assert.Equal(9, result.Join);
        Assert.Empty(result.DuplicatedPrefixBlocks);
    }

    [Fact]
    public void SelectBreaksEquivalentCandidateTiesByBlockIndex()
    {
        var state = CreateState(Edges(
            (1, []),
            (2, []),
            (7, [9]),
            (8, [9]),
            (9, [])));
        var reachable = new MappedReachableBlockFinder(
            ((1, 7), [1]),
            ((2, 7), [2]),
            ((7, null), [7]),
            ((1, 8), [1]),
            ((2, 8), [2]),
            ((8, null), [8]));
        var selector = CreateSelector(
            join: 9,
            reachable,
            new MappedDistanceFinder(
                (1, [(8, 1), (7, 1)]),
                (2, [(8, 1), (7, 1)])),
            overlaps: true);

        var result = selector.Select(Request(state));

        Assert.Equal(7, result.Join);
    }

    [Fact]
    public void SelectRejectsARealGraphPrefixWithAClippedEscape()
    {
        var state = CreateState(Edges(
            (1, [7, 6]),
            (2, [7]),
            (6, []),
            (7, [9]),
            (9, [])));
        var allowed = state.Graph.ReachableBlocks.Remove(6);
        var distances = new ControlFlowDistanceFinder();
        var reachable = new ReachableBlockFinder(distances);
        var selector = new BranchJoinSelector(
            new FixedJoinFinder(9),
            reachable,
            distances,
            new RecordingOverlapClassifier(result: true),
            CreateCycleClassifier());

        var result = selector.Select(new(
            state,
            1,
            2,
            null,
            allowed,
            null,
            null));

        Assert.Equal(9, result.Join);
        Assert.Empty(result.DuplicatedPrefixBlocks);
    }

    [Fact]
    public void SelectExcludesBothActiveLoopTransfersFromBranchAnalysis()
    {
        var state = CreateState(Edges(
            (1, [4]),
            (2, [5]),
            (4, []),
            (5, []),
            (9, [])));
        var overlaps = new RecordingOverlapClassifier(result: false);
        var selector = new BranchJoinSelector(
            new FixedJoinFinder(9),
            new MappedReachableBlockFinder(),
            new MappedDistanceFinder(),
            overlaps,
            CreateCycleClassifier());

        var result = selector.Select(new(
            state,
            1,
            2,
            null,
            state.Graph.ReachableBlocks,
            4,
            5));

        Assert.DoesNotContain(4, overlaps.LastAllowed);
        Assert.DoesNotContain(5, overlaps.LastAllowed);
        Assert.Equal(ImmutableHashSet.Create(4, 5), result.TerminalTransfers);
    }

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var joins = new FixedJoinFinder(9);
        var reachable = new MappedReachableBlockFinder();
        var distances = new MappedDistanceFinder();
        var overlaps = new RecordingOverlapClassifier(false);
        var cycles = CreateCycleClassifier();

        Assert.Throws<ArgumentNullException>(() =>
            new BranchJoinSelector(null!, reachable, distances, overlaps, cycles));
        Assert.Throws<ArgumentNullException>(() =>
            new BranchJoinSelector(joins, null!, distances, overlaps, cycles));
        Assert.Throws<ArgumentNullException>(() =>
            new BranchJoinSelector(joins, reachable, null!, overlaps, cycles));
        Assert.Throws<ArgumentNullException>(() =>
            new BranchJoinSelector(joins, reachable, distances, null!, cycles));
        Assert.Throws<ArgumentNullException>(() =>
            new BranchJoinSelector(
                joins,
                reachable,
                distances,
                overlaps,
                null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BranchJoinSelector(
                joins,
                reachable,
                distances,
                overlaps,
                cycles,
                (BranchPrefixDuplicationPolicy)255));
    }

    private static BranchJoinSelector CreateSharedPrefixSelector(
        ControlFlowStructuringState state,
        bool hasExceptions)
    {
        Assert.Equal(hasExceptions, !state.Graph.MethodBody.ExceptionRegions.IsEmpty);
        return CreateSelector(
            join: 9,
            new MappedReachableBlockFinder(
                ((1, 7), [1, 3, 4]),
                ((2, 7), [2, 3, 4]),
                ((7, null), [7])),
            new MappedDistanceFinder((1, [(7, 3)]), (2, [(7, 3)])),
            overlaps: true);
    }

    private static BranchJoinSelector CreateSelector(
        int? join,
        IReachableBlockFinder reachable,
        IControlFlowDistanceFinder distances,
        bool overlaps,
        BranchPrefixDuplicationPolicy duplicationPolicy =
            BranchPrefixDuplicationPolicy.SizeAware) =>
        new(
            new FixedJoinFinder(join),
            reachable,
            distances,
            new RecordingOverlapClassifier(overlaps),
            CreateCycleClassifier(),
            duplicationPolicy);

    private static ControlFlowSubgraphCycleClassifier CreateCycleClassifier() =>
        new(
            new ControlFlowComponentAnalyzer(),
            new ControlFlowCycleClassifier());

    private static BranchJoinSelectionRequest Request(
        ControlFlowStructuringState state) =>
        new(
            state,
            1,
            2,
            null,
            state.Graph.ReachableBlocks,
            null,
            null);

    private static ImmutableDictionary<int, ImmutableArray<int>>
        SharedPrefixEdges() =>
        Edges(
            (1, [3]),
            (2, [3]),
            (3, [4]),
            (4, [7]),
            (7, [9]),
            (9, []));

    private static ImmutableDictionary<int, ImmutableArray<int>> Edges(
        params (int Block, ImmutableArray<int> Successors)[] edges) =>
        edges.ToImmutableDictionary(edge => edge.Block, edge => edge.Successors);

    private static ControlFlowStructuringState CreateState(
        ImmutableDictionary<int, ImmutableArray<int>> successors,
        bool hasExceptions = false)
    {
        var nodeCount = successors.Keys
            .Concat(successors.Values.SelectMany(value => value))
            .Max() + 1;
        var normalizedSuccessors = Enumerable.Range(0, nodeCount)
            .ToImmutableDictionary(
                index => index,
                index => successors.TryGetValue(index, out var value)
                    ? value
                    : ImmutableArray<int>.Empty);
        var predecessors = Enumerable.Range(0, nodeCount)
            .ToImmutableDictionary(
                index => index,
                index => normalizedSuccessors
                    .Where(edge => edge.Value.Contains(index))
                    .Select(edge => edge.Key)
                    .ToImmutableArray());
        var blocks = Enumerable.Range(0, nodeCount)
            .Select(index => new BasicBlock(
                index,
                index,
                [new CilInstruction(
                    index,
                    index + 1,
                    CilOperation.Nop,
                    new CilOperand.None())]))
            .ToImmutableArray();
        var body = ControlFlowTestSupport.Body(
            CliValueKind.Void,
            0,
            [],
            ControlFlowTestSupport.I(0, CilOperation.Return));
        if (hasExceptions)
        {
            body = body with
            {
                ExceptionRegions =
                [new(
                    CilExceptionRegionKind.Catch,
                    0,
                    1,
                    1,
                    1,
                    ControlFlowTestSupport.TypeKey,
                    null)],
            };
        }
        var emptyEdges = Enumerable.Range(0, nodeCount)
            .ToImmutableDictionary(
                index => index,
                _ => ImmutableArray<int>.Empty);
        var graph = new ControlFlowGraph(
            body,
            blocks,
            normalizedSuccessors,
            predecessors,
            emptyEdges,
            emptyEdges,
            Enumerable.Range(0, nodeCount).ToImmutableHashSet());
        return new ControlFlowStructuringState(new ValidatedControlFlowGraph(
            graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty));
    }

    private sealed class FixedJoinFinder(int? result) :
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

    private sealed class MappedReachableBlockFinder(
        params ((int Start, int? Stop) Key, ImmutableHashSet<int> Blocks)[] rows) :
        IReachableBlockFinder
    {
        private readonly ImmutableDictionary<(int Start, int? Stop),
            ImmutableHashSet<int>> _rows = rows.ToImmutableDictionary(
                row => row.Key,
                row => row.Blocks);

        public ImmutableHashSet<int> Find(
            ControlFlowStructuringState state,
            int start,
            int? stop,
            ImmutableHashSet<int> allowed) =>
            _rows.GetValueOrDefault((start, stop)) ?? [];

        public ImmutableHashSet<int> Find(
            ControlFlowGraph graph,
            int entry,
            ImmutableHashSet<int> allowed) =>
            throw new InvalidOperationException();

        public ImmutableHashSet<int> Find(
            ControlFlowGraph graph,
            int entry,
            ImmutableHashSet<int> stops,
            ImmutableHashSet<int> allowed) =>
            throw new InvalidOperationException();
    }

    private sealed class MappedDistanceFinder(
        params (int Start, (int Block, int Distance)[] Distances)[] rows) :
        IControlFlowDistanceFinder
    {
        private readonly ImmutableDictionary<int, Dictionary<int, int>> _rows =
            rows.ToImmutableDictionary(
                row => row.Start,
                row => row.Distances.ToDictionary(
                    distance => distance.Block,
                    distance => distance.Distance));

        public Dictionary<int, int> Find(
            ControlFlowStructuringState state,
            int start,
            int? stop,
            ImmutableHashSet<int> allowed) =>
            _rows.GetValueOrDefault(start) ?? [];
    }

    private sealed class RecordingOverlapClassifier(bool result) :
        IBranchReachabilityOverlapClassifier
    {
        public ImmutableHashSet<int> LastAllowed { get; private set; } = [];

        public bool Classify(
            ControlFlowStructuringState state,
            int first,
            int second,
            int? stop,
            ImmutableHashSet<int> allowed)
        {
            LastAllowed = allowed;
            return result;
        }
    }
}
