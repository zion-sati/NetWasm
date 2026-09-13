using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using System.Collections.Immutable;
using Xunit;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ExceptionRegionSequenceStepExecutorBranchTests
{
    [Fact]
    public void ConstructorRejectsNullCollaborators()
    {
        var overlaps = new FixedReachableSetOverlapClassifier(false);
        var boundaries = new RecordingDispatcherBoundaryClipper();
        var joins = new FixedCommonReachableBlockFinder(null);
        var reachableBlocks = new RecordingReachableBlockFinder();
        var factories = new (string Parameter, Func<ExceptionRegionSequenceStepExecutor> Create)[]
        {
            ("reachableSetOverlaps", () => new(null!, boundaries, joins, reachableBlocks)),
            ("dispatcherBoundaries", () => new(overlaps, null!, joins, reachableBlocks)),
            ("joins", () => new(overlaps, boundaries, null!, reachableBlocks)),
            ("reachableBlocks", () => new(overlaps, boundaries, joins, null!)),
        };

        foreach (var (parameter, create) in factories)
        {
            var exception = Assert.Throws<ArgumentNullException>(create);
            Assert.Equal(parameter, exception.ParamName);
        }
    }

    [Fact]
    public void ExecuteProjectsAnExceptionGroupWithoutAContinuation()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var entry = state.Graph.Entry;
        var group = new StructuredExceptionGroupDraft(0, 1, [], [], []);
        state.ExceptionGroupsByEntry[entry.StartOffset] = group;
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var actor = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(CreateExecutor());

        var result = actor.Execute(
            new StructuredControlFlowBuilderProbe(),
            state,
            entry.StartOffset,
            null,
            ImmutableHashSet.Create(entry.Index),
            [],
            regions);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Null(result.NextBlockOffset);
        var exceptionRegion = Assert.IsType<StructuredExceptionRegionDraft>(Assert.Single(regions));
        Assert.Same(group, exceptionRegion.Group);
        Assert.Null(exceptionRegion.FallthroughContinuationIndex);
    }

    [Fact]
    public void ExecuteProjectsAnAllowedFallthroughContinuation()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var entry = state.Graph.Entry;
        var continuationBody = new StructuredSequenceDraft([]);
        var group = new StructuredExceptionGroupDraft(
            0,
            1,
            [],
            [],
            [new StructuredExceptionContinuationDraft(entry.StartOffset, continuationBody)]);
        state.ExceptionGroupsByEntry[entry.StartOffset] = group;
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var actor = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(CreateExecutor());

        var result = actor.Execute(
            new StructuredControlFlowBuilderProbe(),
            state,
            entry.StartOffset,
            null,
            ImmutableHashSet.Create(entry.Index),
            [],
            regions);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Equal(entry.StartOffset, result.NextBlockOffset);
        var exceptionRegion = Assert.IsType<StructuredExceptionRegionDraft>(Assert.Single(regions));
        Assert.Equal(entry.Index, exceptionRegion.FallthroughContinuationIndex);
    }

    [Fact]
    public void ExecuteDoesNotProjectADisallowedFallthroughContinuation()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var entry = state.Graph.Entry;
        var group = new StructuredExceptionGroupDraft(
            0,
            1,
            [],
            [],
            [new StructuredExceptionContinuationDraft(entry.StartOffset, new StructuredSequenceDraft([]))]);
        state.ExceptionGroupsByEntry[entry.StartOffset] = group;
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var actor = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(CreateExecutor());

        var result = actor.Execute(
            new StructuredControlFlowBuilderProbe(),
            state,
            entry.StartOffset,
            null,
            ImmutableHashSet<int>.Empty,
            [],
            regions);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Null(result.NextBlockOffset);
        Assert.Null(Assert.IsType<StructuredExceptionRegionDraft>(Assert.Single(regions)).FallthroughContinuationIndex);
    }

    [Fact]
    public void ExecuteDoesNotProjectAFallthroughAlreadyOwnedByTheCurrentPath()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var entry = state.Graph.Entry;
        var group = new StructuredExceptionGroupDraft(
            0,
            1,
            [],
            [],
            [new StructuredExceptionContinuationDraft(entry.StartOffset, new StructuredSequenceDraft([]))]);
        state.ExceptionGroupsByEntry[entry.StartOffset] = group;
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var actor = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(CreateExecutor());

        var result = actor.Execute(
            new StructuredControlFlowBuilderProbe(),
            state,
            entry.StartOffset,
            null,
            ImmutableHashSet.Create(entry.Index),
            [entry.Index],
            regions);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Null(result.NextBlockOffset);
        Assert.Null(Assert.IsType<StructuredExceptionRegionDraft>(Assert.Single(regions)).FallthroughContinuationIndex);
    }

    [Fact]
    public void ExecuteDoesNotProjectAFallthroughOwnedByADispatcher()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var entry = state.Graph.Entry;
        var group = new StructuredExceptionGroupDraft(
            0,
            1,
            [],
            [],
            [new StructuredExceptionContinuationDraft(entry.StartOffset, new StructuredSequenceDraft([]))]);
        state.ExceptionGroupsByEntry[entry.StartOffset] = group;
        state.DispatchersByNode[entry.Index] = ImmutableHashSet.Create(entry.Index);
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var actor = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(CreateExecutor());

        var result = actor.Execute(
            new StructuredControlFlowBuilderProbe(),
            state,
            entry.StartOffset,
            null,
            ImmutableHashSet.Create(entry.Index),
            [],
            regions);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Null(result.NextBlockOffset);
        Assert.Null(Assert.IsType<StructuredExceptionRegionDraft>(Assert.Single(regions)).FallthroughContinuationIndex);
    }

    [Fact]
    public void ExecuteRoutesAContinuationThroughTheActiveDispatcher()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var entry = state.Graph.Entry;
        var group = new StructuredExceptionGroupDraft(
            0,
            1,
            [],
            [],
            [new StructuredExceptionContinuationDraft(entry.StartOffset, new StructuredSequenceDraft([]))]);
        state.ExceptionGroupsByEntry[entry.StartOffset] = group;
        state.ActiveDispatchers.Push(ImmutableHashSet.Create(entry.Index));
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var actor = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(CreateExecutor());

        var result = actor.Execute(
            new StructuredControlFlowBuilderProbe(),
            state,
            entry.StartOffset,
            null,
            ImmutableHashSet.Create(entry.Index),
            [],
            regions);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Null(result.NextBlockOffset);
        var exception = Assert.IsType<StructuredExceptionRegionDraft>(Assert.Single(regions));
        Assert.Null(exception.FallthroughContinuationIndex);
        Assert.Equal([0], exception.DispatcherContinuations);
    }

    [Fact]
    public void ExecuteProjectsTheContinuationDispatcherAfterTheExceptionRegion()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var entry = state.Graph.Entry;
        var dispatcher = new StructuredDispatcherDraft(null, [], []);
        var group = new StructuredExceptionGroupDraft(0, 1, [], [], [])
        {
            ContinuationDispatcher = dispatcher,
            ContinuationJoinBlock = entry.Index,
        };
        state.ExceptionGroupsByEntry[entry.StartOffset] = group;
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var actor = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(CreateExecutor());

        var result = actor.Execute(
            new StructuredControlFlowBuilderProbe(),
            state,
            entry.StartOffset,
            null,
            ImmutableHashSet.Create(entry.Index),
            [],
            regions);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Equal(entry.StartOffset, result.NextBlockOffset);
        Assert.Collection(
            regions,
            region => Assert.Same(group, Assert.IsType<StructuredExceptionRegionDraft>(region).Group),
            region => Assert.Same(dispatcher, region));
    }

    [Fact]
    public void ExecuteDelegatesANonOverlappingDispatcherWithItsCurrentPath()
    {
        var state = CreateUnconditionalBranchState();
        var entry = state.Graph.Entry;
        var component = ImmutableHashSet.Create(entry.Index);
        state.DispatchersByNode[entry.StartOffset] = component;
        var overlaps = new FixedReachableSetOverlapClassifier(false);
        var boundaries = new RecordingDispatcherBoundaryClipper();
        var joins = new FixedCommonReachableBlockFinder(null);
        var reachableBlocks = new RecordingReachableBlockFinder();
        var actor = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(
            new ExceptionRegionSequenceStepExecutor(
                overlaps,
                boundaries,
                joins,
                reachableBlocks));
        var builder = new StructuredControlFlowBuilderProbe();
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var path = new HashSet<int> { -1 };

        var result = actor.Execute(
            builder,
            state,
            entry.StartOffset,
            null,
            state.Graph.ReachableBlocks,
            path,
            regions);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Null(result.NextBlockOffset);
        Assert.Same(builder.DispatcherResult, Assert.Single(regions));
        var call = Assert.Single(builder.DispatcherCalls);
        Assert.Equal(entry.StartOffset, call.Entry);
        Assert.True(call.Component.SetEquals(component));
        Assert.Same(state.Graph.ReachableBlocks, call.Allowed);
        Assert.Null(call.ExitStop);
        Assert.Equal(path, call.Path);
        Assert.NotSame(path, call.Path);
        Assert.Equal(1, boundaries.CallCount);
        Assert.Equal(1, joins.CallCount);
        Assert.Equal(1, overlaps.CallCount);
        Assert.Equal(1, reachableBlocks.CallCount);
    }

    [Fact]
    public void ExecuteDelegatesAClosedDispatcherWithoutDiscoveringExitBodies()
    {
        var state = CreateSelfLoopState();
        var entry = state.Graph.Entry;
        state.DispatchersByNode[entry.StartOffset] = ImmutableHashSet.Create(entry.Index);
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var builder = new StructuredControlFlowBuilderProbe();
        var reachableBlocks = new RecordingReachableBlockFinder();
        var actor = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(
            new ExceptionRegionSequenceStepExecutor(
                new FixedReachableSetOverlapClassifier(false),
                new RecordingDispatcherBoundaryClipper(),
                new FixedCommonReachableBlockFinder(null),
                reachableBlocks));

        var result = actor.Execute(
            builder,
            state,
            entry.StartOffset,
            null,
            state.Graph.ReachableBlocks,
            [],
            regions);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Null(result.NextBlockOffset);
        Assert.Same(builder.DispatcherResult, Assert.Single(regions));
        var call = Assert.Single(builder.DispatcherCalls);
        Assert.Equal(entry.StartOffset, call.Entry);
        Assert.True(call.Component.SetEquals([entry.Index]));
        Assert.Empty(call.Path);
        Assert.Equal(1, builder.CallCount);
        Assert.Equal(0, reachableBlocks.CallCount);
    }

    [Fact]
    public void ExecuteLeavesConditionalBranchPolarityToDispatcherConstruction()
    {
        var branchIfTrue = Execute(CilOperation.BranchIfTrue);
        var branchIfFalse = Execute(CilOperation.BranchIfFalse);

        Assert.True(branchIfTrue.Component.SetEquals(branchIfFalse.Component));
        Assert.Equal(branchIfTrue.Entry, branchIfFalse.Entry);

        static StructuredControlFlowBuilderProbe.DispatcherBuildCall Execute(
            CilOperation operation)
        {
            var state = CreateConditionalBranchState(operation);
            var entry = state.Graph.Entry;
            state.DispatchersByNode[entry.StartOffset] = ImmutableHashSet.Create(entry.Index);
            var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
            var builder = new StructuredControlFlowBuilderProbe();
            var actor = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(
                new ExceptionRegionSequenceStepExecutor(
                    new FixedReachableSetOverlapClassifier(false),
                    new RecordingDispatcherBoundaryClipper(),
                    new FixedCommonReachableBlockFinder(null),
                    new RecordingReachableBlockFinder()));

            var result = actor.Execute(
                builder,
                state,
                entry.StartOffset,
                null,
                state.Graph.ReachableBlocks,
                [],
                regions);

            Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
            Assert.Null(result.NextBlockOffset);
            Assert.Same(builder.DispatcherResult, Assert.Single(regions));
            return Assert.Single(builder.DispatcherCalls);
        }
    }

    [Fact]
    public void ExecuteProjectsDispatcherExitsAndReturnsTheDiscoveredJoin()
    {
        var state = CreateConditionalBranchState(CilOperation.BranchIfTrue);
        var entry = state.Graph.Entry;
        var successors = state.Graph.Successors[entry.Index];
        var join = state.Graph.GetBlock(successors[0]).StartOffset;
        state.DispatchersByNode[entry.StartOffset] = ImmutableHashSet.Create(entry.Index);
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var builder = new StructuredControlFlowBuilderProbe();
        var joins = new FixedCommonReachableBlockFinder(join);
        var reachableBlocks = new RecordingReachableBlockFinder();
        var actor = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(
            new ExceptionRegionSequenceStepExecutor(
                new FixedReachableSetOverlapClassifier(false),
                new RecordingDispatcherBoundaryClipper(),
                joins,
                reachableBlocks));

        var result = actor.Execute(
            builder,
            state,
            entry.StartOffset,
            null,
            state.Graph.ReachableBlocks,
            [],
            regions);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Equal(join, result.NextBlockOffset);
        Assert.Same(builder.DispatcherResult, Assert.Single(regions));
        var call = Assert.Single(builder.DispatcherCalls);
        Assert.Equal(entry.StartOffset, call.Entry);
        Assert.Equal(join, call.ExitStop);
        Assert.True(call.Component.SetEquals([entry.Index]));
        Assert.Equal(1, builder.CallCount);
        Assert.Equal(1, joins.CallCount);
        Assert.Equal(2, reachableBlocks.CallCount);
    }

    [Fact]
    public void ExecuteBoundsExitBodiesByADiscoveredJoinOutsideTheAllowedRegion()
    {
        var state = CreateConditionalBranchState(CilOperation.BranchIfTrue);
        var entry = state.Graph.Entry;
        var join = state.Graph.GetBlock(state.Graph.Successors[entry.Index][0]).StartOffset;
        state.DispatchersByNode[entry.StartOffset] = ImmutableHashSet.Create(entry.Index);
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var joins = new FixedCommonReachableBlockFinder(join);
        var builder = new StructuredControlFlowBuilderProbe();
        var path = new HashSet<int> { -1 };
        var allowed = ImmutableHashSet.Create(entry.Index);
        var actor = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(
            new ExceptionRegionSequenceStepExecutor(
                new FixedReachableSetOverlapClassifier(false),
                new RecordingDispatcherBoundaryClipper(),
                joins,
                new RecordingReachableBlockFinder()));

        var result = actor.Execute(
            builder,
            state,
            entry.StartOffset,
            null,
            allowed,
            path,
            regions);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Equal(join, result.NextBlockOffset);
        Assert.Same(builder.DispatcherResult, Assert.Single(regions));
        Assert.Equal(1, joins.CallCount);
        var call = Assert.Single(builder.DispatcherCalls);
        Assert.Same(allowed, call.Allowed);
        Assert.Equal(join, call.ExitStop);
        Assert.Equal(path, call.Path);
        Assert.NotSame(path, call.Path);
    }

    [Fact]
    public void ExecuteDelegatesTheReachableFallbackWhenExitReachabilityOverlaps()
    {
        var state = CreateConditionalBranchState(CilOperation.BranchIfTrue);
        var entry = state.Graph.Entry;
        var component = ImmutableHashSet.Create(entry.Index);
        state.DispatchersByNode[entry.StartOffset] = component;
        var regions = ImmutableArray.CreateBuilder<StructuredRegionDraft>();
        var builder = new StructuredControlFlowBuilderProbe();
        var overlaps = new FixedReachableSetOverlapClassifier(true);
        var reachableBlocks = new RecordingReachableBlockFinder();
        var actor = Assert.IsAssignableFrom<IStructuredSequenceStepExecutor>(
            new ExceptionRegionSequenceStepExecutor(
                overlaps,
                new RecordingDispatcherBoundaryClipper(),
                new FixedCommonReachableBlockFinder(null),
                reachableBlocks));

        var result = actor.Execute(
            builder,
            state,
            entry.StartOffset,
            null,
            state.Graph.ReachableBlocks,
            [],
            regions);

        Assert.Equal(StructuredSequenceStepDisposition.Continue, result.Disposition);
        Assert.Null(result.NextBlockOffset);
        Assert.Same(builder.DispatcherResult, Assert.Single(regions));
        Assert.True(Assert.Single(builder.DispatcherCalls).Component.SetEquals(component));
        Assert.Equal(1, builder.CallCount);
        Assert.Equal(1, overlaps.CallCount);
        Assert.Equal(3, reachableBlocks.CallCount);
    }

    private static ExceptionRegionSequenceStepExecutor CreateExecutor()
    {
        var distances = new ControlFlowDistanceFinder();
        return new ExceptionRegionSequenceStepExecutor(
            new ReachableSetOverlapClassifier(),
            new DispatcherBoundaryClipper(),
            new CommonReachableBlockFinder(
                new ControlFlowPostDominatorFinder(new ControlFlowPostDominanceAnalyzer()),
                distances),
            new ReachableBlockFinder(distances));
    }

    private static ControlFlowStructuringState CreateUnconditionalBranchState()
    {
        var body = Body(
            CliValueKind.Void,
            0,
            [],
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(1)),
            I(1, CilOperation.Return, new CilOperand.None()));
        return new ControlFlowStructuringState(Validate(body));
    }

    private static ControlFlowStructuringState CreateConditionalBranchState(CilOperation operation)
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

    private static ControlFlowStructuringState CreateSelfLoopState()
    {
        var body = Body(
            CliValueKind.Void,
            0,
            [],
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(0)));
        return new ControlFlowStructuringState(Validate(body));
    }

    private sealed class FixedReachableSetOverlapClassifier(bool result) : IReachableSetOverlapClassifier
    {
        public int CallCount { get; private set; }

        public bool Overlaps(ImmutableArray<ImmutableHashSet<int>> reachableSets)
        {
            CallCount++;
            return result;
        }
    }

    private sealed class RecordingDispatcherBoundaryClipper : IDispatcherBoundaryClipper
    {
        public int CallCount { get; private set; }

        public ImmutableHashSet<int> Clip(
            ImmutableHashSet<int> component,
            ImmutableHashSet<int> allowed,
            int? boundary)
        {
            CallCount++;
            return component;
        }
    }

    private sealed class FixedCommonReachableBlockFinder(int? result) : ICommonReachableBlockFinder
    {
        public int CallCount { get; private set; }

        public int? Find(
            ControlFlowStructuringState state,
            int first,
            int second,
            int? stop,
            ImmutableHashSet<int> allowed)
        {
            CallCount++;
            return result;
        }

        public int? Find(
            ControlFlowStructuringState state,
            int[] starts,
            int? stop,
            ImmutableHashSet<int> allowed)
        {
            CallCount++;
            return result;
        }
    }

    private sealed class RecordingReachableBlockFinder : IReachableBlockFinder
    {
        public int CallCount { get; private set; }

        public ImmutableHashSet<int> Find(
            ControlFlowStructuringState state,
            int start,
            int? stop,
            ImmutableHashSet<int> allowed)
        {
            CallCount++;
            return ImmutableHashSet.Create(start);
        }

        public ImmutableHashSet<int> Find(
            ControlFlowGraph graph,
            int entry,
            ImmutableHashSet<int> allowed)
        {
            CallCount++;
            return ImmutableHashSet.Create(entry);
        }

        public ImmutableHashSet<int> Find(
            ControlFlowGraph graph,
            int entry,
            ImmutableHashSet<int> stops,
            ImmutableHashSet<int> allowed)
        {
            CallCount++;
            return ImmutableHashSet.Create(entry);
        }
    }

}
