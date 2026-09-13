using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;
using NetWasm.Compiler.Core;
using System.Collections.Immutable;

using NwDraft = global::NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Tests.Draft;

public sealed class StructuredControlFlowOwnershipActorTests
{
    [Fact]
    public void OccurrenceCollectorTraversesEveryStructuredContainer()
    {
        var collector = Assert.IsAssignableFrom<NwDraft.IStructuredControlFlowOccurrenceCollector>(
            new NwDraft.StructuredControlFlowOccurrenceCollector());
        var sequence = CreateComprehensiveSequence();

        var occurrences = collector.Collect(sequence);

        Assert.True(occurrences.Length > 10);
        Assert.Contains(occurrences, static occurrence => occurrence.Preferred);
        Assert.Contains(occurrences, static occurrence => !occurrence.Preferred);
    }

    [Fact]
    public void OccurrenceCollectorRequiresASequence()
    {
        var collector = Assert.IsAssignableFrom<NwDraft.IStructuredControlFlowOccurrenceCollector>(
            new NwDraft.StructuredControlFlowOccurrenceCollector());

        Assert.Throws<ArgumentNullException>(() => collector.Collect(null!));
    }

    [Fact]
    public void OccurrenceCollectorRejectsAnUnknownExceptionPart()
    {
        var collector = Assert.IsAssignableFrom<NwDraft.IStructuredControlFlowOccurrenceCollector>(
            new NwDraft.StructuredControlFlowOccurrenceCollector());
        var group = ExceptionGroupCollectorTests.Group() with
        {
            ProtectedParts = [new UnsupportedExceptionPartDraft()],
        };
        var sequence = new NwDraft.StructuredSequenceDraft(
            [new NwDraft.StructuredExceptionRegionDraft(group, null)]);

        Assert.Throws<InvalidOperationException>(() => collector.Collect(sequence));
    }

    [Fact]
    public void OwnerSelectorUsesTheFirstOccurrenceUntilAPreferredOccurrenceAppears()
    {
        var selector = Assert.IsAssignableFrom<NwDraft.IStructuredControlFlowOwnerSelector>(
            new NwDraft.StructuredControlFlowOwnerSelector());
        ImmutableArray<NwDraft.StructuredControlFlowBlockOccurrence> occurrences =
        [
            new(10, 0, false),
            new(10, 1, false),
            new(20, 2, false),
            new(10, 3, true),
            new(10, 4, false),
            new(20, 5, true),
        ];

        var selected = selector.Select(occurrences);

        Assert.Equal(2, selected.Count);
        Assert.Equal(3, selected[10]);
        Assert.Equal(5, selected[20]);
    }

    [Fact]
    public void OwnerSelectorReturnsAnEmptyMapForNoOccurrences()
    {
        var selector = Assert.IsAssignableFrom<NwDraft.IStructuredControlFlowOwnerSelector>(
            new NwDraft.StructuredControlFlowOwnerSelector());

        Assert.Empty(selector.Select([]));
    }

    [Fact]
    public void OwnershipProjectorRetainsExactlyTheSelectedBlockOccurrences()
    {
        var collector = Assert.IsAssignableFrom<NwDraft.IStructuredControlFlowOccurrenceCollector>(
            new NwDraft.StructuredControlFlowOccurrenceCollector());
        var selector = Assert.IsAssignableFrom<NwDraft.IStructuredControlFlowOwnerSelector>(
            new NwDraft.StructuredControlFlowOwnerSelector());
        var projector = Assert.IsAssignableFrom<NwDraft.IStructuredControlFlowOwnershipProjector>(
            new NwDraft.StructuredControlFlowOwnershipProjector());
        var method = ExceptionRegionOwnershipProjectorDraftTests.CreateMethod() with
        {
            Body = CreateComprehensiveSequence(),
        };
        var selected = selector.Select(collector.Collect(method.Body));

        var projected = projector.Project(method, selected);
        var projectedOccurrences = collector.Collect(projected.Body);

        Assert.Equal(selected.Count, projectedOccurrences.Length);
        Assert.Equal(
            selected.Keys.Order(),
            projectedOccurrences.Select(static occurrence => occurrence.Block).Order());
    }

    [Fact]
    public void ResolverRequiresEveryOwnershipCapability()
    {
        var collector = new NwDraft.StructuredControlFlowOccurrenceCollector();
        var selector = new NwDraft.StructuredControlFlowOwnerSelector();
        var projector = new NwDraft.StructuredControlFlowOwnershipProjector();

        Assert.Throws<ArgumentNullException>(
            () => new NwDraft.StructuredControlFlowOwnershipResolverDraft(null!, selector, projector));
        Assert.Throws<ArgumentNullException>(
            () => new NwDraft.StructuredControlFlowOwnershipResolverDraft(collector, null!, projector));
        Assert.Throws<ArgumentNullException>(
            () => new NwDraft.StructuredControlFlowOwnershipResolverDraft(collector, selector, null!));
    }


    [Fact]
    public void OwnershipProjectorRejectsUnsupportedStructuredNodes()
    {
        var actor = Assert.IsAssignableFrom<NwDraft.IStructuredControlFlowOwnershipProjector>(
            new NwDraft.StructuredControlFlowOwnershipProjector());
        var method = DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));

        var unsupportedRegion = new UnsupportedRegionDraft();
        var projected = actor.Project(
            method with
            {
                Body = new NwDraft.StructuredSequenceDraft([unsupportedRegion]),
            },
            ImmutableDictionary<int, int>.Empty);
        Assert.Same(unsupportedRegion, Assert.Single(projected.Body.Regions));

        var group = new NwDraft.StructuredExceptionGroupDraft(
            0,
            0,
            [new UnsupportedExceptionPartDraft()],
            [],
            []);
        Assert.Throws<InvalidOperationException>(() => actor.Project(
            method with
            {
                Body = new NwDraft.StructuredSequenceDraft(
                [
                    new NwDraft.StructuredExceptionRegionDraft(group, null),
                ]),
                ExceptionGroups = [group],
            },
            ImmutableDictionary<int, int>.Empty));
    }

    internal static NwDraft.StructuredSequenceDraft CreateComprehensiveSequence()
    {
        var nextBlock = 0;
        global::NetWasm.Compiler.ControlFlow.BasicBlock Block() =>
            new(nextBlock, nextBlock++, []);
        NwDraft.StructuredSequenceDraft Nested() =>
            new([new NwDraft.StructuredBlockDraft(Block(), false)]);

        var dispatcherBlock = Block();
        var dispatcherExit = Block();
        var continuationDispatcherBlock = Block();
        var continuationDispatcherExit = Block();
        var baseGroup = ExceptionGroupCollectorTests.Group();
        var nestedGroup = ExceptionGroupCollectorTests.Group();
        var group = baseGroup with
        {
            ProtectedParts =
            [
                new NwDraft.StructuredExceptionCodeDraft(Nested()),
                new NwDraft.StructuredNestedExceptionGroupDraft(nestedGroup),
                new NwDraft.StructuredNestedExceptionGroupDraft(nestedGroup),
            ],
            Clauses =
            [
                ExceptionGroupCollectorTests.Clause(Nested(), Nested()),
                ExceptionGroupCollectorTests.Clause(Nested(), null),
            ],
            NormalContinuations = [new NwDraft.StructuredExceptionContinuationDraft(0, Nested())],
            ContinuationDispatcher = new NwDraft.StructuredDispatcherDraft(
                continuationDispatcherBlock.Index,
                [new NwDraft.StructuredDispatcherBlockDraft(
                    continuationDispatcherBlock,
                    null,
                    null)],
                [new NwDraft.StructuredDispatcherExitDraft(
                    continuationDispatcherExit.Index,
                    Nested())]),
        };

        return new NwDraft.StructuredSequenceDraft(
        [
            new NwDraft.StructuredBlockDraft(Block(), true),
            new NwDraft.StructuredIfDraft(Block(), Nested(), Nested()),
            new NwDraft.StructuredLoopDraft(Block(), true, Nested(), Nested(), Nested()),
            new NwDraft.StructuredPostTestLoopDraft(Nested(), Block(), true, Nested(), Nested()),
            new NwDraft.StructuredDispatcherDraft(
                dispatcherBlock.Index,
                [new NwDraft.StructuredDispatcherBlockDraft(dispatcherBlock, null, null)],
                [new NwDraft.StructuredDispatcherExitDraft(dispatcherExit.Index, Nested())]),
            new NwDraft.StructuredExceptionRegionDraft(group, null),
        ]);
    }

    private sealed record UnsupportedExceptionPartDraft : NwDraft.StructuredExceptionPartDraft;
    private sealed record UnsupportedRegionDraft : NwDraft.StructuredRegionDraft;

}
