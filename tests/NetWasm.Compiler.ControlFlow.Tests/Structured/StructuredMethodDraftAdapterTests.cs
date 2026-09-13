using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NwDraft = global::NetWasm.Compiler.ControlFlow.Draft;
using New = global::NetWasm.Compiler.ControlFlow.Structured;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

using NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.ControlFlow.Tests.Structured;

public sealed class StructuredMethodDraftAdapterTests
{
    [Fact]
    public void AdaptCreatesGraphFreeMethodFactsAndOwnedRegions()
    {
        var legacy = DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));

        var method = ((New.IStructuredMethodDraftAdapter)new New.StructuredMethodDraftAdapter(
            new New.StructuredBlockDefinitionFactory(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupCollector(),
            new global::NetWasm.Compiler.ControlFlow.Structured.ExceptionGroupIdAssigner(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupParentMapBuilder(
                new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder()),
                    new New.StructuredControlFlowProjector(),
            new New.StructuredExceptionGroupProjector(new New.StructuredControlFlowProjector()))).Adapt(legacy);

        Assert.Equal(legacy.ValidatedGraph.Graph.MethodBody.Method, method.Header.Method);
        Assert.Equal(new New.StructuredBlockId(0), method.EntryBlock);
        Assert.Single(method.Blocks);
        var code = Assert.IsType<New.StructuredCode>(Assert.Single(method.Body.Regions));
        Assert.Equal(New.StructuredBlockRole.Owner, code.Occurrence.Role);
        Assert.Empty(method.ExceptionGroups);
        Assert.NotEmpty(method.InstructionEntryStacks);
    }

    [Fact]
    public void AdaptRequiresAMethodAndBlockFactory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ((New.IStructuredMethodDraftAdapter)new New.StructuredMethodDraftAdapter(
                new New.StructuredBlockDefinitionFactory(),
                new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupCollector(),
                new global::NetWasm.Compiler.ControlFlow.Structured.ExceptionGroupIdAssigner(),
                new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupParentMapBuilder(
                    new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder()),
                        new New.StructuredControlFlowProjector(),
                new New.StructuredExceptionGroupProjector(new New.StructuredControlFlowProjector()))).Adapt(null!));
        Assert.Throws<ArgumentNullException>(() => new New.StructuredMethodDraftAdapter(null!,
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupCollector(),
            new global::NetWasm.Compiler.ControlFlow.Structured.ExceptionGroupIdAssigner(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupParentMapBuilder(
                new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder()),
                    new New.StructuredControlFlowProjector(),
            new New.StructuredExceptionGroupProjector(new New.StructuredControlFlowProjector())));
    }

    [Fact]
    public void ConstructorRequiresExceptionGroupCapabilities()
    {
        var blocks = new StructuredBlockDefinitionFactory();
        var groups = new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupCollector();
        var groupIds = new ExceptionGroupIdAssigner();
        var parents = new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupParentMapBuilder(
            new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder());

        Assert.Throws<ArgumentNullException>(
            () => new StructuredMethodDraftAdapter(blocks, null!, groupIds, parents,
                new StructuredControlFlowProjector(),
                new StructuredExceptionGroupProjector(new StructuredControlFlowProjector())));
        Assert.Throws<ArgumentNullException>(
            () => new StructuredMethodDraftAdapter(blocks, groups, null!, parents,
                new StructuredControlFlowProjector(),
                new StructuredExceptionGroupProjector(new StructuredControlFlowProjector())));
        Assert.Throws<ArgumentNullException>(
            () => new StructuredMethodDraftAdapter(blocks, groups, groupIds, null!,
                new StructuredControlFlowProjector(),
                new StructuredExceptionGroupProjector(new StructuredControlFlowProjector())));

        Assert.Throws<ArgumentNullException>(() => new New.StructuredMethodDraftAdapter(
            new New.StructuredBlockDefinitionFactory(),
            new NwDraft.ExceptionGroupCollector(),
            new New.ExceptionGroupIdAssigner(),
            new NwDraft.ExceptionGroupParentMapBuilder(
                new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder()),
            null!,
            new New.StructuredExceptionGroupProjector(new New.StructuredControlFlowProjector())));
        Assert.Throws<ArgumentNullException>(() => new New.StructuredMethodDraftAdapter(
            new New.StructuredBlockDefinitionFactory(),
            new NwDraft.ExceptionGroupCollector(),
            new New.ExceptionGroupIdAssigner(),
            new NwDraft.ExceptionGroupParentMapBuilder(
                new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder()),
            new New.StructuredControlFlowProjector(),
            null!));
    }

    [Fact]
    public void AdaptRejectsUnknownLegacyRegionAndExceptionPart()
    {
        var legacy = DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));
        var projector = new New.StructuredMethodDraftAdapter(new New.StructuredBlockDefinitionFactory(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupCollector(),
            new global::NetWasm.Compiler.ControlFlow.Structured.ExceptionGroupIdAssigner(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupParentMapBuilder(
                new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder()),
                    new New.StructuredControlFlowProjector(),
            new New.StructuredExceptionGroupProjector(new New.StructuredControlFlowProjector()));

        Assert.Throws<InvalidOperationException>(() => ((New.IStructuredMethodDraftAdapter)projector).Adapt(legacy with
        {
            Body = new NwDraft.StructuredSequenceDraft([new UnknownLegacyRegion()]),
        }));

        var group = new NwDraft.StructuredExceptionGroupDraft(
            0,
            1,
            [new UnknownLegacyExceptionPart()],
            [],
            []);
        Assert.Throws<InvalidOperationException>(() => ((New.IStructuredMethodDraftAdapter)projector).Adapt(legacy with
        {
            Body = new NwDraft.StructuredSequenceDraft([new NwDraft.StructuredExceptionRegionDraft(group, null)]),
            ExceptionGroups = [group],
        }));
    }

    [Fact]
    public void AdaptRejectsExceptionGroupWithConflictingParents()
    {
        var legacy = DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));
        var child = new NwDraft.StructuredExceptionGroupDraft(0, 1, [], [], []);
        var parent = new NwDraft.StructuredExceptionGroupDraft(
            0,
            1,
            [new NwDraft.StructuredNestedExceptionGroupDraft(child)],
            [],
            []);
        var body = new NwDraft.StructuredSequenceDraft(
        [
            new NwDraft.StructuredExceptionRegionDraft(child, null),
            new NwDraft.StructuredExceptionRegionDraft(parent, null),
        ]);

        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredMethodDraftAdapter)new New.StructuredMethodDraftAdapter(
                new New.StructuredBlockDefinitionFactory(),
                new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupCollector(),
                new global::NetWasm.Compiler.ControlFlow.Structured.ExceptionGroupIdAssigner(),
                new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupParentMapBuilder(
                    new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder()),
                        new New.StructuredControlFlowProjector(),
                new New.StructuredExceptionGroupProjector(new New.StructuredControlFlowProjector()))).Adapt(legacy with
                {
                    Body = body,
                    ExceptionGroups = [child, parent],
                }));
        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredMethodDraftAdapter)new New.StructuredMethodDraftAdapter(
                new New.StructuredBlockDefinitionFactory(),
                new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupCollector(),
                new global::NetWasm.Compiler.ControlFlow.Structured.ExceptionGroupIdAssigner(),
                new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupParentMapBuilder(
                    new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder()),
                        new New.StructuredControlFlowProjector(),
                new New.StructuredExceptionGroupProjector(new New.StructuredControlFlowProjector()))).Adapt(legacy with
                {
                    Body = new NwDraft.StructuredSequenceDraft(
                [
                    new NwDraft.StructuredExceptionRegionDraft(parent, null),
                    new NwDraft.StructuredExceptionRegionDraft(child, null),
                ]),
                    ExceptionGroups = [parent, child],
                }));
    }

    [Fact]
    public void AdaptClassifiesNonOriginalExecutionAndRepeatedGroupParent()
    {
        var legacy = DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));
        var original = Assert.IsType<NwDraft.StructuredBlockDraft>(Assert.Single(legacy.Body.Regions));
        var nonOriginal = legacy with
        {
            Body = new NwDraft.StructuredSequenceDraft([original with { IsOriginal = false }]),
        };
        var projector = new New.StructuredMethodDraftAdapter(new New.StructuredBlockDefinitionFactory(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupCollector(),
            new global::NetWasm.Compiler.ControlFlow.Structured.ExceptionGroupIdAssigner(),
            new RootExceptionGroupParentMapBuilder(),
                new New.StructuredControlFlowProjector(),
            new New.StructuredExceptionGroupProjector(new New.StructuredControlFlowProjector()));

        var projected = ((New.IStructuredMethodDraftAdapter)projector).Adapt(nonOriginal);

        var code = Assert.IsType<New.StructuredCode>(Assert.Single(projected.Body.Regions));
        Assert.Equal(New.StructuredBlockRole.ExecutingReplica, code.Occurrence.Role);

        var group = new NwDraft.StructuredExceptionGroupDraft(0, 1, [], [], []);
        var repeated = legacy with
        {
            Body = new NwDraft.StructuredSequenceDraft(
            [
                new NwDraft.StructuredExceptionRegionDraft(group, null),
                new NwDraft.StructuredExceptionRegionDraft(group, null),
            ]),
            ExceptionGroups = [group],
        };
        Assert.Single(((New.IStructuredMethodDraftAdapter)projector).Adapt(repeated).ExceptionGroups);

        var child = new NwDraft.StructuredExceptionGroupDraft(0, 1, [], [], []);
        var parent = new NwDraft.StructuredExceptionGroupDraft(
            0,
            1,
            [
                new NwDraft.StructuredNestedExceptionGroupDraft(child),
                new NwDraft.StructuredNestedExceptionGroupDraft(child),
            ],
            [],
            []);
        var sameNonNullParent = legacy with
        {
            Body = new NwDraft.StructuredSequenceDraft([new NwDraft.StructuredExceptionRegionDraft(parent, null)]),
            ExceptionGroups = [parent, child],
        };
        Assert.Equal(
            2,
            ((New.IStructuredMethodDraftAdapter)projector).Adapt(sameNonNullParent).ExceptionGroups.Count);
    }

    [Fact]
    public void AdaptLeavesOutsideExceptionScopeWithoutAContinuation()
    {
        var body = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Throw),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Leave, new CilOperand.BranchTarget(4)),
            I(4, CilOperation.Return)) with
        {
            ExceptionRegions =
            [new CilExceptionRegion(
                CilExceptionRegionKind.Catch,
                0,
                2,
                2,
                2,
                TypeKey,
                null)],
        };
        var legacy = DraftMethod(body);
        var leaveBlock = legacy.ValidatedGraph.Graph.Blocks.Single(
            block => block.Terminator.Operation == CilOperation.Leave);
        legacy = legacy with
        {
            Body = new NwDraft.StructuredSequenceDraft([new NwDraft.StructuredBlockDraft(leaveBlock)]),
        };

        var method = ((New.IStructuredMethodDraftAdapter)new New.StructuredMethodDraftAdapter(
            new New.StructuredBlockDefinitionFactory(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupCollector(),
            new global::NetWasm.Compiler.ControlFlow.Structured.ExceptionGroupIdAssigner(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupParentMapBuilder(
                new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder()),
                    new New.StructuredControlFlowProjector(),
            new New.StructuredExceptionGroupProjector(new New.StructuredControlFlowProjector()))).Adapt(legacy);

        var leave = method.Body.Regions
            .OfType<New.StructuredCode>()
            .Select(code => code.Occurrence)
            .Single(occurrence => method.Blocks[occurrence.Block].Exit is New.StructuredLeaveExit);
        Assert.Null(leave.LeaveContinuation);

        legacy = legacy with
        {
            Body = new NwDraft.StructuredSequenceDraft([new NwDraft.StructuredBlockDraft(leaveBlock, IsOriginal: false)]),
        };
        method = ((New.IStructuredMethodDraftAdapter)new New.StructuredMethodDraftAdapter(
            new New.StructuredBlockDefinitionFactory(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupCollector(),
            new global::NetWasm.Compiler.ControlFlow.Structured.ExceptionGroupIdAssigner(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupParentMapBuilder(
                new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder()),
                    new New.StructuredControlFlowProjector(),
            new New.StructuredExceptionGroupProjector(new New.StructuredControlFlowProjector()))).Adapt(legacy);
        leave = Assert.IsType<New.StructuredCode>(Assert.Single(method.Body.Regions)).Occurrence;
        Assert.Equal(New.StructuredBlockRole.RoutingReplica, leave.Role);
        Assert.Null(leave.LeaveContinuation);
    }

    private sealed record UnknownLegacyRegion : NwDraft.StructuredRegionDraft;

    private sealed record UnknownLegacyExceptionPart : NwDraft.StructuredExceptionPartDraft;


    [Fact]
    public void AdaptTreatsAMissingParentMapEntryAsTopLevel()
    {
        var group = new NwDraft.StructuredExceptionGroupDraft(0, 0, [], [], []);
        var method = DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return))) with
        {
            ExceptionGroups = [group],
        };
        var parents = new EmptyExceptionGroupParentMapBuilder();
        var controlFlow = new New.StructuredControlFlowProjector();
        var actor = Assert.IsAssignableFrom<New.IStructuredMethodDraftAdapter>(
            new New.StructuredMethodDraftAdapter(
                new New.StructuredBlockDefinitionFactory(),
                new NwDraft.ExceptionGroupCollector(),
                new New.ExceptionGroupIdAssigner(),
                parents,
                controlFlow,
                new New.StructuredExceptionGroupProjector(controlFlow)));

        var result = actor.Adapt(method);

        var groupId = Assert.Single(result.TopLevelExceptionGroups);
        Assert.Null(result.ExceptionGroups[groupId].Parent);
        Assert.Equal(1, parents.BuildCalls);
    }

    private sealed class EmptyExceptionGroupParentMapBuilder : NwDraft.IExceptionGroupParentMapBuilder
    {
        public int BuildCalls { get; private set; }

        public IReadOnlyDictionary<NwDraft.StructuredExceptionGroupDraft, NwDraft.StructuredExceptionGroupDraft?> Build(
            ImmutableArray<NwDraft.StructuredExceptionGroupDraft> exceptionGroups)
        {
            BuildCalls++;
            return ImmutableDictionary.Create<NwDraft.StructuredExceptionGroupDraft, NwDraft.StructuredExceptionGroupDraft?>(
                ReferenceEqualityComparer.Instance);
        }
    }

}
