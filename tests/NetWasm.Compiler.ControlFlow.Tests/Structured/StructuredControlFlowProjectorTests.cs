using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Tests.Draft;
using NwDraft = NetWasm.Compiler.ControlFlow.Draft;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

using NetWasm.Compiler.Core;
using New = global::NetWasm.Compiler.ControlFlow.Structured;
namespace NetWasm.Compiler.ControlFlow.Tests.Structured;

public sealed class StructuredControlFlowProjectorTests
{
    [Fact]
    public void ProjectTranslatesMethodBodyThroughCapability()
    {
        var method = ControlFlowTestSupport.DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));
        var groups = new NwDraft.ExceptionGroupCollector().Collect(method);
        var groupIds = new ExceptionGroupIdAssigner().Assign(groups);
        var definitions = new StructuredBlockDefinitionFactory().Create(method.ValidatedGraph);
        var projector = Assert.IsAssignableFrom<IStructuredControlFlowProjector>(
            new StructuredControlFlowProjector());

        var result = projector.Project(method.Body, method, definitions, groupIds, null);

        Assert.Equal(method.Body.Regions.Length, result.Regions.Length);
        Assert.NotEmpty(result.Regions);
        Assert.All(result.Regions, region => Assert.NotNull(region));
    }

    [Fact]
    public void ProjectPreservesOccurrenceSpecificDispatcherContinuations()
    {
        var method = DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));
        var group = new NwDraft.StructuredExceptionGroupDraft(0, 0, [], [], []);
        var groupId = new New.StructuredExceptionGroupId(7);
        var sequence = new NwDraft.StructuredSequenceDraft(
        [
            new NwDraft.StructuredExceptionRegionDraft(group, null)
            {
                DispatcherContinuations = [1, 3],
            },
        ]);
        var actor = Assert.IsAssignableFrom<IStructuredControlFlowProjector>(
            new StructuredControlFlowProjector());

        var result = actor.Project(
            sequence,
            method,
            ImmutableDictionary<New.StructuredBlockId, New.StructuredBlockDefinition>.Empty,
            ImmutableDictionary<NwDraft.StructuredExceptionGroupDraft, New.StructuredExceptionGroupId>
                .Empty.Add(group, groupId),
            null);

        var exception = Assert.IsType<New.StructuredExceptionRegion>(Assert.Single(result.Regions));
        Assert.Equal(groupId, exception.Group);
        Assert.Equal(
            [new New.StructuredContinuationId(1), new New.StructuredContinuationId(3)],
            exception.DispatcherContinuations.OrderBy(continuation => continuation.Value));
    }

    [Fact]
    public void ProjectTranslatesEveryStructuredRegionAndExceptionPart()
    {
        var sequence = StructuredControlFlowOwnershipActorTests.CreateComprehensiveSequence();
        var method = DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return))) with
        {
            Body = sequence,
        };
        var groups = new NwDraft.ExceptionGroupCollector().Collect(method);
        var groupIds = new ExceptionGroupIdAssigner().Assign(groups);
        var definitions = CreateDefinitions(sequence);
        var actor = Assert.IsAssignableFrom<IStructuredControlFlowProjector>(new StructuredControlFlowProjector());

        var result = actor.Project(sequence, method, definitions, groupIds, null);
        var regions = DescendantsAndSelf(result).ToArray();
        var parts = groups
            .SelectMany(static group => group.ProtectedParts.Select(part => (Group: group, Part: part)))
            .Select(item => actor.Project(item.Part, method, definitions, groupIds, item.Group))
            .ToArray();
        var groupRegions = groups
            .SelectMany(group => GroupSequences(group).Select(sequenceItem => (Group: group, Sequence: sequenceItem)))
            .SelectMany(item => DescendantsAndSelf(actor.Project(
                item.Sequence,
                method,
                definitions,
                groupIds,
                item.Group)))
            .ToArray();
        var markerRegions = actor.Project(
            new NwDraft.StructuredSequenceDraft(
            [
                new NwDraft.StructuredLoopBreakDraft(),
                new NwDraft.StructuredLoopContinueDraft(),
                new NwDraft.StructuredDispatcherContinueDraft(0),
            ]),
            method,
            definitions,
            groupIds,
            null).Regions;
        var allRegions = regions
            .Concat(parts.OfType<New.StructuredExceptionCode>().SelectMany(static part => DescendantsAndSelf(part.Body)))
            .Concat(groupRegions)
            .Concat(markerRegions)
            .ToArray();

        Assert.Contains(allRegions, static region => region is New.StructuredCode);
        Assert.Contains(allRegions, static region => region is New.StructuredLoopBreak);
        Assert.Contains(allRegions, static region => region is New.StructuredLoopContinue);
        Assert.Contains(allRegions, static region => region is New.StructuredDispatcherContinue);
        Assert.Contains(allRegions, static region => region is New.StructuredExceptionRegion);
        Assert.Contains(allRegions, static region => region is New.StructuredDispatcher);
        Assert.Contains(allRegions, static region => region is New.StructuredIf);
        Assert.Contains(allRegions, static region => region is New.StructuredLoop);
        Assert.Contains(allRegions, static region => region is New.StructuredPostTestLoop);

        Assert.Contains(parts, static part => part is New.StructuredExceptionCode);
        Assert.Contains(parts, static part => part is New.StructuredNestedExceptionGroup);

        var dispatcher = groups
            .Select(static group => group.ContinuationDispatcher)
            .First(static candidate => candidate is not null)!;
        var projectedDispatcher = actor.Project(dispatcher, method, definitions, groupIds, groups[0]);
        Assert.Equal(dispatcher.Blocks.Length, projectedDispatcher.Blocks.Length);
        Assert.Equal(dispatcher.Exits.Length, projectedDispatcher.Exits.Length);
    }

    [Fact]
    public void ProjectRejectsUnsupportedDraftKinds()
    {
        var method = DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));
        var definitions = new New.StructuredBlockDefinitionFactory().Create(method.ValidatedGraph);
        var actor = Assert.IsAssignableFrom<IStructuredControlFlowProjector>(new StructuredControlFlowProjector());

        Assert.Throws<InvalidOperationException>(() => actor.Project(
            new NwDraft.StructuredSequenceDraft([new UnsupportedRegionDraft()]),
            method,
            definitions,
            ImmutableDictionary<NwDraft.StructuredExceptionGroupDraft, New.StructuredExceptionGroupId>.Empty,
            null));

        Assert.Throws<InvalidOperationException>(() => actor.Project(
            new UnsupportedExceptionPartDraft(),
            method,
            definitions,
            ImmutableDictionary<NwDraft.StructuredExceptionGroupDraft, New.StructuredExceptionGroupId>.Empty,
            new NwDraft.StructuredExceptionGroupDraft(0, 0, [], [], [])));
    }

    [Fact]
    public void ProjectPreservesAbsentDispatcherRoutes()
    {
        var method = DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));
        var block = method.ValidatedGraph.Graph.Entry;
        var dispatcher = new NwDraft.StructuredDispatcherDraft(
            null,
            [new NwDraft.StructuredDispatcherBlockDraft(block, null, null)],
            []);
        var definitions = new New.StructuredBlockDefinitionFactory().Create(method.ValidatedGraph);
        var actor = Assert.IsAssignableFrom<IStructuredControlFlowProjector>(new StructuredControlFlowProjector());

        var result = actor.Project(
            dispatcher,
            method,
            definitions,
            ImmutableDictionary<NwDraft.StructuredExceptionGroupDraft, New.StructuredExceptionGroupId>.Empty,
            null);

        Assert.Null(result.EntryBlock);
        var projectedBlock = Assert.Single(result.Blocks);
        Assert.Null(projectedBlock.WhenTrue);
        Assert.Null(projectedBlock.WhenFalse);
        Assert.Empty(result.Exits);

        var routedResult = actor.Project(
            new NwDraft.StructuredDispatcherDraft(
                block.StartOffset,
                [new NwDraft.StructuredDispatcherBlockDraft(block, block.StartOffset, block.StartOffset)],
                []),
            method,
            definitions,
            ImmutableDictionary<NwDraft.StructuredExceptionGroupDraft, New.StructuredExceptionGroupId>.Empty,
            null);
        var expectedTarget = new New.StructuredBlockId(block.StartOffset);
        Assert.Equal(expectedTarget, routedResult.EntryBlock);
        var routedBlock = Assert.Single(routedResult.Blocks);
        Assert.Equal(expectedTarget, routedBlock.WhenTrue);
        Assert.Equal(expectedTarget, routedBlock.WhenFalse);
    }

    [Fact]
    public void ProjectAssignsTheMatchingLeaveContinuation()
    {
        var method = DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));
        var block = method.ValidatedGraph.Graph.Entry;
        var definitions = new New.StructuredBlockDefinitionFactory().Create(method.ValidatedGraph);
        var blockId = definitions.Keys.Single();
        var leaveDefinitions = definitions.SetItem(
            blockId,
            definitions[blockId] with
            {
                Exit = new New.StructuredLeaveExit(0, blockId),
            });
        var group = new NwDraft.StructuredExceptionGroupDraft(
            0,
            0,
            [],
            [],
            [
                new NwDraft.StructuredExceptionContinuationDraft(
                    block.StartOffset + 1,
                    new NwDraft.StructuredSequenceDraft([])),
                new NwDraft.StructuredExceptionContinuationDraft(
                    block.StartOffset,
                    new NwDraft.StructuredSequenceDraft([])),
            ]);
        var groupIds = ImmutableDictionary<NwDraft.StructuredExceptionGroupDraft, New.StructuredExceptionGroupId>.Empty
            .Add(group, new New.StructuredExceptionGroupId(0));
        var actor = Assert.IsAssignableFrom<IStructuredControlFlowProjector>(new StructuredControlFlowProjector());

        var result = actor.Project(
            new NwDraft.StructuredSequenceDraft([new NwDraft.StructuredBlockDraft(block)]),
            method,
            leaveDefinitions,
            groupIds,
            group);

        var code = Assert.IsType<New.StructuredCode>(Assert.Single(result.Regions));
        Assert.Equal(new New.StructuredContinuationId(1), code.Occurrence.LeaveContinuation);

        var routing = actor.Project(
            new NwDraft.StructuredSequenceDraft([new NwDraft.StructuredBlockDraft(block, false)]),
            method,
            leaveDefinitions,
            groupIds,
            null);
        var routingCode = Assert.IsType<New.StructuredCode>(Assert.Single(routing.Regions));
        Assert.Equal(New.StructuredBlockRole.RoutingReplica, routingCode.Occurrence.Role);
        Assert.Null(routingCode.Occurrence.LeaveContinuation);
    }

    private static ImmutableDictionary<New.StructuredBlockId, New.StructuredBlockDefinition> CreateDefinitions(
        NwDraft.StructuredSequenceDraft sequence)
        => new NwDraft.StructuredControlFlowOccurrenceCollector()
            .Collect(sequence)
            .Select(static occurrence => new New.StructuredBlockId(occurrence.Block))
            .Distinct()
            .ToImmutableDictionary(
                static id => id,
                static id => new New.StructuredBlockDefinition(
                    id,
                    id.Value,
                    [],
                    [],
                    new New.StructuredFallthroughExit(null))
                {
                    EndOffset = id.Value,
                });

    private static IEnumerable<New.StructuredRegion> DescendantsAndSelf(New.StructuredSequence sequence)
    {
        foreach (var region in sequence.Regions)
        {
            yield return region;

            IEnumerable<New.StructuredSequence> children = region switch
            {
                New.StructuredIf conditional => [conditional.WhenTrue, conditional.WhenFalse],
                New.StructuredLoop loop => [loop.Body, loop.ContinueBody, loop.ExitBody],
                New.StructuredPostTestLoop loop => [loop.Body, loop.ContinueBody, loop.ExitBody],
                New.StructuredDispatcher dispatcher => dispatcher.Exits.Select(static exit => exit.Body),
                _ => [],
            };

            foreach (var child in children.SelectMany(DescendantsAndSelf))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<NwDraft.StructuredSequenceDraft> GroupSequences(
        NwDraft.StructuredExceptionGroupDraft group)
    {
        foreach (var code in group.ProtectedParts.OfType<NwDraft.StructuredExceptionCodeDraft>())
        {
            yield return code.Body;
        }

        foreach (var clause in group.Clauses)
        {
            if (clause.FilterBody is not null)
            {
                yield return clause.FilterBody;
            }

            yield return clause.HandlerBody;
        }

        foreach (var continuation in group.NormalContinuations)
        {
            yield return continuation.Body;
        }

        if (group.ContinuationDispatcher is not null)
        {
            yield return new NwDraft.StructuredSequenceDraft([group.ContinuationDispatcher]);
        }
    }

    private sealed record UnsupportedRegionDraft : NwDraft.StructuredRegionDraft;

    private sealed record UnsupportedExceptionPartDraft : NwDraft.StructuredExceptionPartDraft;

}
