using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structured;

public sealed class StructuredExceptionGroupProjectorTests
{
    [Fact]
    public void ProjectTranslatesPresentAndAbsentOptionalGroupShapes()
    {
        var method = DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));
        var definitions = new StructuredBlockDefinitionFactory().Create(method.ValidatedGraph);
        var targetBlock = method.ValidatedGraph.Graph.Entry;
        var targetId = definitions.Keys.Single();
        var outsideId = new StructuredBlockId(targetId.Value + 100);
        definitions = definitions.Add(
            outsideId,
            new StructuredBlockDefinition(
                outsideId,
                targetBlock.StartOffset + 2,
                [],
                [],
                new StructuredFallthroughExit(null))
            {
                EndOffset = targetBlock.StartOffset + 2,
            });
        var emptySequence = new StructuredSequenceDraft([]);
        var nested = new StructuredExceptionGroupDraft(0, 1, [], [], []);
        var parent = new StructuredExceptionGroupDraft(0, 1, [], [], []);
        var group = new StructuredExceptionGroupDraft(
            targetBlock.StartOffset,
            1,
            [
                new StructuredExceptionCodeDraft(emptySequence),
                new StructuredNestedExceptionGroupDraft(nested),
            ],
            [
                new StructuredExceptionClauseDraft(
                    new CilExceptionRegion(
                        CilExceptionRegionKind.Catch,
                        0,
                        1,
                        targetBlock.StartOffset,
                        1,
                        null,
                        null),
                    emptySequence,
                    null),
                new StructuredExceptionClauseDraft(
                    new CilExceptionRegion(
                        CilExceptionRegionKind.Filter,
                        0,
                        1,
                        targetBlock.StartOffset,
                        1,
                        null,
                        targetBlock.StartOffset),
                    emptySequence,
                    emptySequence),
            ],
            [new StructuredExceptionContinuationDraft(targetBlock.StartOffset, emptySequence)])
        {
            ContinuationDispatcher = new StructuredDispatcherDraft(null, [], []),
            ContinuationJoinBlock = targetId.Value,
        };
        var id = new StructuredExceptionGroupId(2);
        var parentId = new StructuredExceptionGroupId(1);
        var nestedId = new StructuredExceptionGroupId(3);
        var groupIds = ImmutableDictionary.Create<StructuredExceptionGroupDraft, StructuredExceptionGroupId>(
                ReferenceEqualityComparer.Instance)
            .Add(group, id)
            .Add(parent, parentId)
            .Add(nested, nestedId);
        var controlFlow = new RecordingControlFlowProjector();
        var actor = Assert.IsAssignableFrom<IStructuredExceptionGroupProjector>(
            new StructuredExceptionGroupProjector(controlFlow));

        var result = actor.Project(group, id, parent, method, definitions, groupIds);

        Assert.Equal(id, result.Id);
        Assert.Equal(parentId, result.Parent);
        Assert.Equal(2, result.ProtectedParts.Length);
        Assert.IsType<StructuredExceptionCode>(result.ProtectedParts[0]);
        Assert.Equal(nestedId, Assert.IsType<StructuredNestedExceptionGroup>(result.ProtectedParts[1]).Group);
        Assert.Equal(2, result.Clauses.Length);
        Assert.Null(result.Clauses[0].FilterBody);
        Assert.Null(result.Clauses[0].FilterBlock);
        Assert.NotNull(result.Clauses[1].FilterBody);
        Assert.Equal(targetId, result.Clauses[1].FilterBlock);
        Assert.Equal(targetId, Assert.Single(result.NormalContinuations).Target);
        Assert.NotNull(result.ContinuationDispatcher);
        Assert.Equal(targetId, result.ContinuationJoinBlock);
        Assert.Equal(targetId, Assert.Single(result.ProtectedBlocks));
        Assert.Equal(4, controlFlow.SequenceCalls);
        Assert.Equal(2, controlFlow.PartCalls);
        Assert.Equal(1, controlFlow.DispatcherCalls);

        var absent = actor.Project(
            new StructuredExceptionGroupDraft(targetBlock.StartOffset + 10, 0, [], [], []),
            new StructuredExceptionGroupId(4),
            null,
            method,
            definitions,
            groupIds);

        Assert.Null(absent.Parent);
        Assert.Empty(absent.ProtectedParts);
        Assert.Empty(absent.Clauses);
        Assert.Empty(absent.NormalContinuations);
        Assert.Null(absent.ContinuationDispatcher);
        Assert.Null(absent.ContinuationJoinBlock);
        Assert.Empty(absent.ProtectedBlocks);
    }

    [Fact]
    public void ConstructorRequiresControlFlowProjection()
        => Assert.Throws<ArgumentNullException>(() => new StructuredExceptionGroupProjector(null!));

    private sealed class RecordingControlFlowProjector : IStructuredControlFlowProjector
    {
        public int SequenceCalls { get; private set; }

        public int PartCalls { get; private set; }

        public int DispatcherCalls { get; private set; }

        public StructuredSequence Project(
            StructuredSequenceDraft sequence,
            StructuredMethodDraft method,
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
            ImmutableDictionary<StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds,
            StructuredExceptionGroupDraft? activeGroup)
        {
            SequenceCalls++;
            return new StructuredSequence([]);
        }

        public StructuredExceptionPart Project(
            StructuredExceptionPartDraft part,
            StructuredMethodDraft method,
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
            ImmutableDictionary<StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds,
            StructuredExceptionGroupDraft activeGroup)
        {
            PartCalls++;
            return part switch
            {
                StructuredExceptionCodeDraft => new StructuredExceptionCode(new StructuredSequence([])),
                StructuredNestedExceptionGroupDraft nested => new StructuredNestedExceptionGroup(groupIds[nested.Group]),
                _ => throw new InvalidOperationException(),
            };
        }

        public StructuredDispatcher Project(
            StructuredDispatcherDraft dispatcher,
            StructuredMethodDraft method,
            ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
            ImmutableDictionary<StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds,
            StructuredExceptionGroupDraft? activeGroup)
        {
            DispatcherCalls++;
            return new StructuredDispatcher(null, [], []);
        }
    }
}
