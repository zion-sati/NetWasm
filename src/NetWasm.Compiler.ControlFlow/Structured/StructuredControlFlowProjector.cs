using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Draft = NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal sealed class StructuredControlFlowProjector : IStructuredControlFlowProjector
{
    public StructuredSequence Project(
        Draft.StructuredSequenceDraft sequence,
        Draft.StructuredMethodDraft method,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds,
        Draft.StructuredExceptionGroupDraft? activeGroup)
        => ProjectSequence(sequence, method, definitions, groupIds, activeGroup);

    public StructuredExceptionPart Project(
        Draft.StructuredExceptionPartDraft part,
        Draft.StructuredMethodDraft method,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds,
        Draft.StructuredExceptionGroupDraft activeGroup)
        => ProjectPart(part, method, definitions, groupIds, activeGroup);

    private static StructuredExceptionPart ProjectPart(
        Draft.StructuredExceptionPartDraft part,
        Draft.StructuredMethodDraft method,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds,
        Draft.StructuredExceptionGroupDraft activeGroup) => part switch
        {
            Draft.StructuredExceptionCodeDraft code => new StructuredExceptionCode(
                ProjectSequence(code.Body, method, definitions, groupIds, activeGroup)),
            Draft.StructuredNestedExceptionGroupDraft nested => new StructuredNestedExceptionGroup(groupIds[nested.Group]),
            _ => throw new InvalidOperationException("Unknown legacy exception part."),
        };

    private static StructuredSequence ProjectSequence(
        Draft.StructuredSequenceDraft sequence,
        Draft.StructuredMethodDraft method,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds,
        Draft.StructuredExceptionGroupDraft? activeGroup) => new(
        sequence.Regions.Select(region => ProjectRegion(region, method, definitions, groupIds, activeGroup)).ToImmutableArray());

    private static StructuredRegion ProjectRegion(
        Draft.StructuredRegionDraft region,
        Draft.StructuredMethodDraft method,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds,
        Draft.StructuredExceptionGroupDraft? activeGroup) => region switch
        {
            Draft.StructuredBlockDraft block => new StructuredCode(Occurrence(
                block.Block,
            block.IsOriginal,
            method,
            definitions,
                activeGroup)),
            Draft.StructuredLoopBreakDraft => new StructuredLoopBreak(),
            Draft.StructuredLoopContinueDraft => new StructuredLoopContinue(),
            Draft.StructuredDispatcherContinueDraft continuation => new StructuredDispatcherContinue(new(continuation.TargetBlock)),
            Draft.StructuredExceptionRegionDraft exception => new StructuredExceptionRegion(
                groupIds[exception.Group],
                exception.FallthroughContinuationIndex is int continuation ? new(continuation) : null)
            {
                DispatcherContinuations = exception.DispatcherContinuations
                    .Select(index => new StructuredContinuationId(index))
                    .ToImmutableHashSet(),
            },
            Draft.StructuredDispatcherDraft dispatcher => ProjectDispatcher(dispatcher, method, definitions, groupIds, activeGroup),
            Draft.StructuredIfDraft conditional => new StructuredIf(
                Occurrence(conditional.ConditionBlock, conditional.IsOriginal, method, definitions, activeGroup),
                ProjectSequence(conditional.WhenTrue, method, definitions, groupIds, activeGroup),
                ProjectSequence(conditional.WhenFalse, method, definitions, groupIds, activeGroup)),
            Draft.StructuredLoopDraft loop => new StructuredLoop(
                Occurrence(loop.ConditionBlock, loop.IsOriginal, method, definitions, activeGroup),
                loop.ContinueWhenConditionTrue,
                ProjectSequence(loop.Body, method, definitions, groupIds, activeGroup),
                ProjectSequence(loop.ContinueBody, method, definitions, groupIds, activeGroup),
                ProjectSequence(loop.ExitBody, method, definitions, groupIds, activeGroup)),
            Draft.StructuredPostTestLoopDraft loop => new StructuredPostTestLoop(
                ProjectSequence(loop.Body, method, definitions, groupIds, activeGroup),
                Occurrence(loop.ConditionBlock, loop.IsOriginal, method, definitions, activeGroup),
                loop.ContinueWhenConditionTrue,
                ProjectSequence(loop.ContinueBody, method, definitions, groupIds, activeGroup),
                ProjectSequence(loop.ExitBody, method, definitions, groupIds, activeGroup)),
            _ => throw new InvalidOperationException("Unknown legacy structured region."),
        };

    public StructuredDispatcher Project(
        Draft.StructuredDispatcherDraft dispatcher,
        Draft.StructuredMethodDraft method,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds,
        Draft.StructuredExceptionGroupDraft? activeGroup)
        => ProjectDispatcher(dispatcher, method, definitions, groupIds, activeGroup);

    private static StructuredDispatcher ProjectDispatcher(
        Draft.StructuredDispatcherDraft dispatcher,
        Draft.StructuredMethodDraft method,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds,
        Draft.StructuredExceptionGroupDraft? activeGroup) => new(
            dispatcher.EntryBlock is int entry ? new(entry) : null,
            dispatcher.Blocks.Select(block => new StructuredDispatcherBlock(
                Occurrence(block.Block, block.IsOriginal, method, definitions, activeGroup),
                block.WhenTrue is int whenTrue ? new(whenTrue) : null,
                block.WhenFalse is int whenFalse ? new(whenFalse) : null)).ToImmutableArray(),
            dispatcher.Exits.Select(exit => new StructuredDispatcherExit(
                new(exit.TargetBlock),
                ProjectSequence(exit.Body, method, definitions, groupIds, activeGroup))).ToImmutableArray());

    private static StructuredBlockOccurrence Occurrence(
        BasicBlock block,
        bool isOriginal,
        Draft.StructuredMethodDraft method,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        Draft.StructuredExceptionGroupDraft? activeGroup)
    {
        var id = new StructuredBlockId(block.Index);
        var definition = definitions[id];
        var routingLeave = !isOriginal && activeGroup is null && definition.Exit is StructuredLeaveExit;
        var role = (isOriginal, routingLeave) switch
        {
            (true, _) => StructuredBlockRole.Owner,
            (false, true) => StructuredBlockRole.RoutingReplica,
            _ => StructuredBlockRole.ExecutingReplica,
        };
        StructuredContinuationId? continuation = null;
        if (!routingLeave && definition.Exit is StructuredLeaveExit leave && activeGroup is { } legacyGroup)
        {
            var targetOffset = method.ValidatedGraph.Graph.GetBlock(leave.Target.Value).StartOffset;
            var index = -1;
            for (var candidateIndex = 0; candidateIndex < legacyGroup.NormalContinuations.Length; candidateIndex++)
            {
                if (legacyGroup.NormalContinuations[candidateIndex].TargetOffset == targetOffset)
                {
                    index = candidateIndex;
                    break;
                }
            }
            if (index >= 0) continuation = new(index);
        }
        return new(id, role, continuation);
    }

}
