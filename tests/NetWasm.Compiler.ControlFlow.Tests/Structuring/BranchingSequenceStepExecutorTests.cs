using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.Core;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structuring;
using Xunit;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class BranchingSequenceStepExecutorTests
{
    [Fact]
    public void ConstructorRejectsNullCollaborators()
    {
        var joins = new FixedJoinFinder(3, 3);
        var reachable = new FixedReachableBlockFinder();
        var distances = new FixedDistanceFinder();
        var overlaps = new FixedBranchOverlapClassifier(false);
        var factories = new (string Parameter, Func<BranchingSequenceStepExecutor> Create)[]
        {
            ("joins", () => new(null!, reachable, distances, overlaps, new DispatcherExitSelector())),
            ("reachableBlocks", () => new(joins, null!, distances, overlaps, new DispatcherExitSelector())),
            ("distances", () => new(joins, reachable, null!, overlaps, new DispatcherExitSelector())),
            ("branchOverlaps", () => new(joins, reachable, distances, null!, new DispatcherExitSelector())),
            ("dispatcherExits", () => new(joins, reachable, distances, overlaps, null!)),
        };

        foreach (var (parameter, create) in factories)
        {
            var exception = Assert.Throws<ArgumentNullException>(create);
            Assert.Equal(parameter, exception.ParamName);
        }
    }

    [Theory]
    [InlineData(CilOperation.BranchIfTrue)]
    [InlineData(CilOperation.BranchIfFalse)]
    public void ExecuteDelegatesBothSidesOfEveryConditionalBranch(CilOperation operation)
    {
        var state = CreateConditionalState(operation);
        var entry = state.Graph.Entry;
        state.BoundaryOwnedBlocks.Add(entry.Index);
        var successors = state.Graph.Successors[entry.Index];
        var joins = new FixedJoinFinder(99, 99);
        var builder = new StructuredControlFlowBuilderProbe();
        var actor = CreateActor(joins, overlaps: false);
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var path = new HashSet<int> { -1 };

        var first = Execute(actor, builder, state, entry.StartOffset, null,
            state.Graph.ReachableBlocks, path, regions);
        var firstRegion = Assert.IsType<StructuredIfDraft>(Assert.Single(regions));
        regions.Clear();
        var second = Execute(actor, builder, state, entry.StartOffset, null,
            state.Graph.ReachableBlocks, path, regions);
        var secondRegion = Assert.IsType<StructuredIfDraft>(Assert.Single(regions));

        var branchSelectsFirst = operation != CilOperation.BranchIfFalse;
        Assert.Equal(branchSelectsFirst ? successors[0] : successors[1], builder.SequenceCalls[0].Start);
        Assert.Equal(branchSelectsFirst ? successors[1] : successors[0], builder.SequenceCalls[1].Start);
        Assert.All(builder.SequenceCalls, call => Assert.Equal(99, call.Stop));
        Assert.All(builder.SequenceCalls, call => Assert.NotSame(path, call.Path));
        Assert.True(firstRegion.IsOriginal);
        Assert.False(secondRegion.IsOriginal);
        Assert.Contains(entry.Index, state.BoundaryClaimedBlockBodies);
        Assert.Equal(99, first.NextBlockOffset);
        Assert.Equal(99, second.NextBlockOffset);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ExecuteUsesAReachableExplicitStopAsTheJoin(
        bool firstReachesStop,
        bool secondReachesStop)
    {
        var state = CreateConditionalState(CilOperation.BranchIfTrue);
        var entry = state.Graph.Entry;
        var successors = state.Graph.Successors[entry.Index];
        const int stop = 99;
        var distances = new FixedDistanceFinder(
            firstReachesStop ? successors[0] : secondReachesStop ? successors[1] : -1,
            stop);
        var joins = new FixedJoinFinder(88, 88);
        var actor = CreateActor(joins, distances: distances, overlaps: false);

        var result = Execute(actor, new StructuredControlFlowBuilderProbe(), state,
            entry.StartOffset, stop, state.Graph.ReachableBlocks, [],
            ImmutableArray.CreateBuilder<StructuredRegionDraft>());

        Assert.Equal(stop, result.NextBlockOffset);
        Assert.Equal(0, joins.PairCallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ExecuteUsesAnActiveLoopExitAsTheJoin(int successorIndex)
    {
        var state = CreateConditionalState(CilOperation.BranchIfTrue);
        var entry = state.Graph.Entry;
        var loopExit = state.Graph.Successors[entry.Index][successorIndex];
        state.LoopExitTargets.Push(loopExit);
        var joins = new FixedJoinFinder(88, 88);
        var actor = CreateActor(joins, overlaps: false);

        var result = Execute(actor, new StructuredControlFlowBuilderProbe(), state,
            entry.StartOffset, null, state.Graph.ReachableBlocks, [],
            ImmutableArray.CreateBuilder<StructuredRegionDraft>());

        Assert.Null(result.NextBlockOffset);
        Assert.Equal(0, joins.PairCallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExecuteMovesTheJoinPastAReachableDispatcher(bool firstPath)
    {
        var state = CreateConditionalStateWithDispatcherExits(CilOperation.BranchIfTrue);
        var entry = state.Graph.Entry;
        var successors = state.Graph.Successors[entry.Index];
        var dispatcherEntry = successors[0];
        state.DispatchersByNode[dispatcherEntry] = [dispatcherEntry];
        var reachable = new FixedReachableBlockFinder(new Dictionary<int, ImmutableHashSet<int>>
        {
            [successors[0]] = firstPath ? [dispatcherEntry, 4] : [],
            [successors[1]] = firstPath ? [4] : [dispatcherEntry, 4],
        });
        var joins = new FixedJoinFinder(dispatcherEntry, 77);
        var actor = CreateActor(joins, reachable: reachable, overlaps: false);

        var result = Execute(actor, new StructuredControlFlowBuilderProbe(), state,
            entry.StartOffset, null, state.Graph.ReachableBlocks, [],
            ImmutableArray.CreateBuilder<StructuredRegionDraft>());

        Assert.Equal(77, result.NextBlockOffset);
        Assert.Equal(1, joins.ArrayCallCount);
        Assert.Equal(firstPath ? 1 : 2, reachable.CallCount);
    }

    [Fact]
    public void ExecuteDelegatesAnOverlappingBranchAsOneDispatcher()
    {
        var state = CreateConditionalState(CilOperation.BranchIfTrue);
        var entry = state.Graph.Entry;
        var component = ImmutableHashSet.Create(entry.Index);
        var reachable = new FixedReachableBlockFinder(new Dictionary<int, ImmutableHashSet<int>>
        {
            [entry.Index] = component,
        });
        var joins = new FixedJoinFinder(99, 77);
        var builder = new StructuredControlFlowBuilderProbe();
        var path = new HashSet<int> { -1 };
        var actor = CreateActor(joins, reachable, overlaps: true);
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();

        var result = Execute(actor, builder, state, entry.StartOffset, null,
            state.Graph.ReachableBlocks, path, regions);

        Assert.Equal(77, result.NextBlockOffset);
        Assert.Same(builder.DispatcherResult, Assert.Single(regions));
        var call = Assert.Single(builder.DispatcherCalls);
        Assert.True(call.Component.SetEquals(component));
        Assert.Equal(77, call.ExitStop);
        Assert.NotSame(path, call.Path);
    }

    [Fact]
    public void ExecuteDeclinesANonBranchingBlock()
    {
        var distances = new ControlFlowDistanceFinder();
        var joins = new CommonReachableBlockFinder(
            new ControlFlowPostDominatorFinder(new ControlFlowPostDominanceAnalyzer()),
            distances);
        var reachableBlocks = new ReachableBlockFinder(distances);
        var command = new BranchingSequenceStepExecutor(
            joins,
            reachableBlocks,
            distances,
            new BranchReachabilityOverlapClassifier(reachableBlocks),
                new DispatcherExitSelector());
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var recursiveBuilder = new StructuredControlFlowBuilderProbe();
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var cursor = state.Graph.Blocks[^1].StartOffset;

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

    private static BranchingSequenceStepExecutor CreateActor(
        FixedJoinFinder joins,
        FixedReachableBlockFinder? reachable = null,
        FixedDistanceFinder? distances = null,
        bool overlaps = false) => new BranchingSequenceStepExecutor(
            joins,
            reachable ?? new FixedReachableBlockFinder(),
            distances ?? new FixedDistanceFinder(),
            new FixedBranchOverlapClassifier(overlaps),
            new DispatcherExitSelector());

    private static StructuredSequenceStepResult Execute<TActor>(
        TActor actor,
        IStructuredControlFlowBuilder builder,
        ControlFlowStructuringState state,
        int current,
        int? stop,
        ImmutableHashSet<int> allowed,
        HashSet<int> path,
        ImmutableArray<StructuredRegionDraft>.Builder regions)
        where TActor : IStructuredSequenceStepExecutor =>
        ((IStructuredSequenceStepExecutor)actor).Execute(builder, state, current, stop, allowed, path, regions);

    private static ControlFlowStructuringState CreateConditionalState(CilOperation operation)
    {
        var body = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, operation, new CilOperand.BranchTarget(3)),
            I(2, CilOperation.Return, new CilOperand.None()),
            I(3, CilOperation.Return, new CilOperand.None()));
        return new ControlFlowStructuringState(Validate(body));
    }

    private static ControlFlowStructuringState CreateConditionalStateWithDispatcherExits(CilOperation operation)
    {
        var body = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, operation, new CilOperand.BranchTarget(3)),
            I(2, CilOperation.Branch, new CilOperand.BranchTarget(4)),
            I(3, CilOperation.Branch, new CilOperand.BranchTarget(4)),
            I(4, CilOperation.Return, new CilOperand.None()));
        return new ControlFlowStructuringState(Validate(body));
    }

    private sealed class FixedJoinFinder(int? pairResult, int? arrayResult) :
        ICommonReachableBlockFinder
    {
        public int PairCallCount { get; private set; }

        public int ArrayCallCount { get; private set; }

        public int? Find(ControlFlowStructuringState state, int first, int second,
            int? stop, ImmutableHashSet<int> allowed)
        {
            PairCallCount++;
            return pairResult;
        }

        public int? Find(ControlFlowStructuringState state, int[] starts,
            int? stop, ImmutableHashSet<int> allowed)
        {
            ArrayCallCount++;
            return arrayResult;
        }
    }

    private sealed class FixedReachableBlockFinder(
        Dictionary<int, ImmutableHashSet<int>>? results = null) : IReachableBlockFinder
    {
        public int CallCount { get; private set; }

        public ImmutableHashSet<int> Find(ControlFlowStructuringState state, int start,
            int? stop, ImmutableHashSet<int> allowed)
        {
            CallCount++;
            return results?.GetValueOrDefault(start) ?? [start];
        }

        public ImmutableHashSet<int> Find(ControlFlowGraph graph, int entry,
            ImmutableHashSet<int> allowed) => throw new InvalidOperationException();

        public ImmutableHashSet<int> Find(ControlFlowGraph graph, int entry,
            ImmutableHashSet<int> stops, ImmutableHashSet<int> allowed) =>
            throw new InvalidOperationException();
    }

    private sealed class FixedDistanceFinder(int reachableStart = -1, int reachableStop = -1) :
        IControlFlowDistanceFinder
    {
        public Dictionary<int, int> Find(ControlFlowStructuringState state, int start,
            int? stop, ImmutableHashSet<int> allowed) =>
            start == reachableStart && stop == reachableStop
                ? new Dictionary<int, int> { [reachableStop] = 0 }
                : [];
    }

    private sealed class FixedBranchOverlapClassifier(bool result) :
        IBranchReachabilityOverlapClassifier
    {
        public bool Classify(ControlFlowStructuringState state, int first, int second,
            int? stop, ImmutableHashSet<int> allowed) => result;
    }
}
