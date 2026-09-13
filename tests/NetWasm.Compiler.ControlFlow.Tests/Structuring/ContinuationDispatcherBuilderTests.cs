using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.ControlFlow.Tests.Structuring;

public sealed class ContinuationDispatcherBuilderTests
{
    [Fact]
    public void ConstructorRejectsNullCollaborators()
    {
        var controlFlow = new RecordingStructuredControlFlowBuilder();
        var reachable = new RecordingReachableBlockFinder();

        var controlFlowException = Assert.Throws<ArgumentNullException>(
            () => new ContinuationDispatcherBuilder(null!, reachable));
        var reachableException = Assert.Throws<ArgumentNullException>(
            () => new ContinuationDispatcherBuilder(controlFlow, null!));

        Assert.Equal("structuredControlFlow", controlFlowException.ParamName);
        Assert.Equal("reachableBlocks", reachableException.ParamName);
    }

    [Fact]
    public void BuildUsesTheMethodEndForTargetsBeyondTheContinuationBoundary()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        var controlFlow = new RecordingStructuredControlFlowBuilder(state, [11, 99]);
        var reachable = new RecordingReachableBlockFinder();
        var actor = new ContinuationDispatcherBuilder(
            controlFlow,
            reachable);

        var result = Build(actor, state, [99, 11, 99], 22, 103);

        Assert.Equal([11, 99], reachable.Entries);
        var call = Assert.Single(controlFlow.DispatcherCalls);
        Assert.True(call.Component.SetEquals([11, 99]));
        Assert.Contains(102, call.Allowed);
        Assert.Empty(call.Path);
        Assert.True(Assert.Single(result.Blocks, block => block.Block.Index == 11).IsOriginal);
        Assert.False(Assert.Single(result.Blocks, block => block.Block.Index == 99).IsOriginal);
        Assert.True(state.ContinuationOwnedBlocks.SetEquals([11, 99]));
    }

    [Fact]
    public void BuildUsesTheContinuationBoundaryWhenEveryTargetPrecedesIt()
    {
        var state = ControlFlowStructuringStateTestFactory.Create();
        var controlFlow = new RecordingStructuredControlFlowBuilder(state, [11]);
        var actor = new ContinuationDispatcherBuilder(
            controlFlow,
            new RecordingReachableBlockFinder());

        Build(actor, state, [11], 22, 103);

        var allowed = Assert.Single(controlFlow.DispatcherCalls).Allowed;
        Assert.Contains(11, allowed);
        Assert.Contains(21, allowed);
        Assert.DoesNotContain(22, allowed);
    }

    private sealed class RecordingStructuredControlFlowBuilder : IStructuredControlFlowBuilder
    {
        private readonly StructuredDispatcherDraft _result;

        public RecordingStructuredControlFlowBuilder(
            ControlFlowStructuringState? state = null,
            ImmutableArray<int> blocks = default)
        {
            _result = new StructuredDispatcherDraft(
                null,
                state is null
                    ? []
                    : [.. blocks.Select((block, index) => new StructuredDispatcherBlockDraft(
                        state.Graph.GetBlock(block),
                        null,
                        null)
                    {
                        IsOriginal = index == 0,
                    })],
                []);
        }

        public List<DispatcherCall> DispatcherCalls { get; } = [];

        public StructuredSequenceDraft Build(
            ControlFlowStructuringState state,
            int? start,
            int? stop,
            ImmutableHashSet<int> allowed,
            HashSet<int> path) => throw new InvalidOperationException();

        public StructuredDispatcherDraft Build(
            ControlFlowStructuringState state,
            int? entry,
            ImmutableHashSet<int> component,
            ImmutableHashSet<int> allowed,
            int? exitStop,
            HashSet<int> path)
        {
            DispatcherCalls.Add(new(component, allowed, path));
            return _result;
        }

        public sealed record DispatcherCall(
            ImmutableHashSet<int> Component,
            ImmutableHashSet<int> Allowed,
            HashSet<int> Path);
    }

    private sealed class RecordingReachableBlockFinder : IReachableBlockFinder
    {
        public List<int> Entries { get; } = [];

        public ImmutableHashSet<int> Find(
            ControlFlowStructuringState state,
            int start,
            int? stop,
            ImmutableHashSet<int> allowed) => throw new InvalidOperationException();

        public ImmutableHashSet<int> Find(
            ControlFlowGraph graph,
            int entry,
            ImmutableHashSet<int> allowed)
        {
            Entries.Add(entry);
            return [entry];
        }

        public ImmutableHashSet<int> Find(
            ControlFlowGraph graph,
            int entry,
            ImmutableHashSet<int> stops,
            ImmutableHashSet<int> allowed) => throw new InvalidOperationException();
    }

    private static StructuredDispatcherDraft Build<TBuilder>(
        TBuilder builder,
        ControlFlowStructuringState state,
        IEnumerable<int> targets,
        int continuationBoundary,
        int methodEnd)
        where TBuilder : IContinuationDispatcherBuilder =>
        builder.Build(state, targets, continuationBoundary, methodEnd);
}
