using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structuring;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class StructuredControlFlowBuilderTests
{
    [Fact]
    public void ConstructorRejectsNullCollaborators()
    {
        var shells = new FixedDispatcherShellBuilder();
        var steps = new RecordingSequenceStepExecutor();

        var shellException = Assert.Throws<ArgumentNullException>(
            () => new StructuredControlFlowBuilder(null!, steps));
        var stepException = Assert.Throws<ArgumentNullException>(
            () => new StructuredControlFlowBuilder(shells, null!));

        Assert.Equal("exceptionAwareDispatcherShells", shellException.ParamName);
        Assert.Equal("sequenceSteps", stepException.ParamName);
    }

    [Fact]
    public void BuildReturnsEmptyForANullStartAStopOrAnExcludedBlock()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var entry = state.Graph.Entry;
        var steps = new RecordingSequenceStepExecutor();
        var actor = new StructuredControlFlowBuilder(
            new FixedDispatcherShellBuilder(),
            steps);

        Assert.Empty(BuildSequence(actor, state, null, null, state.Graph.ReachableBlocks, []).Regions);
        Assert.Empty(BuildSequence(actor, state, entry.Index, entry.Index, state.Graph.ReachableBlocks, []).Regions);
        Assert.Empty(BuildSequence(actor, state, entry.Index, null, [], []).Regions);
        Assert.Equal(0, steps.CallCount);
    }

    [Fact]
    public void BuildPublishesActiveDispatcherLoopExitAndLoopContinueMarkers()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var entry = state.Graph.Entry;
        var actor = new StructuredControlFlowBuilder(
            new FixedDispatcherShellBuilder(),
            new RecordingSequenceStepExecutor());

        state.ActiveDispatchers.Push([entry.Index]);
        var dispatcher = BuildSequence(actor, state, entry.Index, null, state.Graph.ReachableBlocks, []);
        state.ActiveDispatchers.Pop();
        state.LoopExitTargets.Push(entry.Index);
        var loopExit = BuildSequence(actor, state, entry.Index, null, state.Graph.ReachableBlocks, []);
        state.LoopExitTargets.Pop();
        state.LoopContinueTargets.Push(entry.Index);
        var loopContinue = BuildSequence(actor, state, entry.Index, null, state.Graph.ReachableBlocks, [-1]);
        state.LoopContinueTargets.Pop();

        Assert.Equal(entry.Index, Assert.IsType<StructuredDispatcherContinueDraft>(
            Assert.Single(dispatcher.Regions)).TargetBlock);
        Assert.IsType<StructuredLoopBreakDraft>(Assert.Single(loopExit.Regions));
        Assert.IsType<StructuredLoopContinueDraft>(Assert.Single(loopContinue.Regions));
    }

    [Fact]
    public void BuildHonorsAContinueStepWithoutPublishingAnOrdinaryBlock()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var entry = state.Graph.Entry;
        var steps = new RecordingSequenceStepExecutor(
            new StructuredSequenceStepResult(
                StructuredSequenceStepDisposition.Continue,
                null));
        var actor = new StructuredControlFlowBuilder(
            new FixedDispatcherShellBuilder(),
            steps);
        var path = new HashSet<int>();

        var result = BuildSequence(actor, state, entry.Index, null, state.Graph.ReachableBlocks, path);

        Assert.Empty(result.Regions);
        Assert.Equal(1, steps.CallCount);
        Assert.Contains(entry.Index, path);
    }

    [Fact]
    public void BuildClaimsOrdinaryAndBoundaryBodiesOnce()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var entry = state.Graph.Entry;
        state.BoundaryOwnedBlocks.Add(entry.Index);
        var actor = new StructuredControlFlowBuilder(
            new FixedDispatcherShellBuilder(),
            new RecordingSequenceStepExecutor());

        var first = BuildSequence(actor, state, entry.Index, null, state.Graph.ReachableBlocks, []);
        var second = BuildSequence(actor, state, entry.Index, null, state.Graph.ReachableBlocks, []);

        Assert.True(Assert.IsType<StructuredBlockDraft>(Assert.Single(first.Regions)).IsOriginal);
        Assert.False(Assert.IsType<StructuredBlockDraft>(Assert.Single(second.Regions)).IsOriginal);
        Assert.Contains(entry.Index, state.BoundaryClaimedBlockBodies);
    }

    [Fact]
    public void BuildDoesNotClaimAContinuationOwnedBody()
    {
        var state = ControlFlowStructuringStateTestFactory.CreateSingleReturnBlock();
        var entry = state.Graph.Entry;
        state.ContinuationOwnedBlocks.Add(entry.Index);
        var actor = new StructuredControlFlowBuilder(
            new FixedDispatcherShellBuilder(),
            new RecordingSequenceStepExecutor());

        var result = BuildSequence(actor, state, entry.Index, null, state.Graph.ReachableBlocks, []);

        Assert.False(Assert.IsType<StructuredBlockDraft>(Assert.Single(result.Regions)).IsOriginal);
    }

    [Fact]
    public void BuildFillsDispatcherExitsAndRestoresTheActiveDispatcherStack()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        var entry = state.Graph.Entry.Index;
        var exit = state.Graph.Successors[entry][0];
        var allowed = ImmutableHashSet.Create(entry, exit);
        var shell = new StructuredDispatcherDraft(
            entry,
            [],
            [new StructuredDispatcherExitDraft(exit, StructuredSequenceDraft.Empty)]);
        var shells = new FixedDispatcherShellBuilder(
            new ExceptionAwareDispatcherShell(shell, [entry]));
        var steps = new RecordingSequenceStepExecutor();
        var actor = new StructuredControlFlowBuilder(shells, steps);
        var path = new HashSet<int> { -1 };

        var result = BuildDispatcher(
            actor,
            state,
            entry,
            [entry],
            allowed,
            null,
            path);

        var body = Assert.Single(result.Exits).Body;
        Assert.NotEmpty(body.Regions);
        Assert.Empty(state.ActiveDispatchers);
        var exitPath = Assert.Single(steps.Paths);
        Assert.Contains(-1, exitPath);
        Assert.Contains(exit, exitPath);
        Assert.NotSame(path, exitPath);
    }

    [Fact]
    public void BuildRestoresTheActiveDispatcherStackWhenAnExitFails()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        var entry = state.Graph.Entry.Index;
        var exit = state.Graph.Successors[entry][0];
        var allowed = ImmutableHashSet.Create(entry, exit);
        var shell = new StructuredDispatcherDraft(
            entry,
            [],
            [new StructuredDispatcherExitDraft(exit, StructuredSequenceDraft.Empty)]);
        var actor = new StructuredControlFlowBuilder(
            new FixedDispatcherShellBuilder(
                new ExceptionAwareDispatcherShell(shell, [entry])),
            new RecordingSequenceStepExecutor(exception: new InvalidOperationException()));

        Assert.Throws<InvalidOperationException>(() => BuildDispatcher(
            actor, state, entry, [entry], allowed, null, []));
        Assert.Empty(state.ActiveDispatchers);
    }

    private sealed class FixedDispatcherShellBuilder(
        ExceptionAwareDispatcherShell? result = null) : IExceptionAwareDispatcherShellBuilder
    {
        private readonly ExceptionAwareDispatcherShell _result = result ?? new(
            new StructuredDispatcherDraft(null, [], []),
            []);

        public ExceptionAwareDispatcherShell Build(
            ControlFlowStructuringState state,
            int? entry,
            ImmutableHashSet<int> component,
            ImmutableHashSet<int> allowed,
            int? exitStop) => _result;
    }

    private sealed class RecordingSequenceStepExecutor(
        StructuredSequenceStepResult? result = null,
        Exception? exception = null) : IStructuredSequenceStepExecutor
    {
        private readonly StructuredSequenceStepResult _result = result ?? new(
            StructuredSequenceStepDisposition.NotHandled,
            null);

        public int CallCount { get; private set; }

        public List<HashSet<int>> Paths { get; } = [];

        public StructuredSequenceStepResult Execute(
            IStructuredControlFlowBuilder structuredControlFlowBuilder,
            ControlFlowStructuringState state,
            int currentBlockOffset,
            int? stop,
            ImmutableHashSet<int> allowed,
            HashSet<int> path,
            ImmutableArray<StructuredRegionDraft>.Builder regions)
        {
            CallCount++;
            Paths.Add(path);
            if (exception is not null)
            {
                throw exception;
            }

            return _result;
        }
    }

    private static StructuredSequenceDraft BuildSequence<TBuilder>(
        TBuilder builder,
        ControlFlowStructuringState state,
        int? start,
        int? stop,
        ImmutableHashSet<int> allowed,
        HashSet<int> path)
        where TBuilder : IStructuredControlFlowBuilder =>
        builder.Build(state, start, stop, allowed, path);

    private static StructuredDispatcherDraft BuildDispatcher<TBuilder>(
        TBuilder builder,
        ControlFlowStructuringState state,
        int? entry,
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed,
        int? exitStop,
        HashSet<int> path)
        where TBuilder : IStructuredControlFlowBuilder =>
        builder.Build(state, entry, component, allowed, exitStop, path);
}
