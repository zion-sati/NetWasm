using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Draft = NetWasm.Compiler.ControlFlow.Draft;
using NetWasm.Compiler.ControlFlow;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal sealed class StructuredExceptionGroupProjector(
    IStructuredControlFlowProjector controlFlowProjector) : IStructuredExceptionGroupProjector
{
    private readonly IStructuredControlFlowProjector _controlFlow =
        controlFlowProjector ?? throw new ArgumentNullException(nameof(controlFlowProjector));

    public StructuredExceptionGroup Project(
        Draft.StructuredExceptionGroupDraft group,
        StructuredExceptionGroupId id,
        Draft.StructuredExceptionGroupDraft? parent,
        Draft.StructuredMethodDraft method,
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> groupIds)
    {
        var continuations = group.NormalContinuations.Select((continuation, index) => new StructuredExceptionContinuation(
            new(index),
            continuation.TargetOffset,
            new(method.ValidatedGraph.Graph.GetBlockAtOffset(continuation.TargetOffset).Index),
            _controlFlow.Project(continuation.Body, method, definitions, groupIds, parent))).ToImmutableArray();
        return new(
            id,
            parent is null ? null : groupIds[parent],
            group.TryOffset,
            group.TryLength,
            group.ProtectedParts.Select(part => _controlFlow.Project(part, method, definitions, groupIds, group)).ToImmutableArray(),
            group.Clauses.Select(clause => new StructuredExceptionClause(
                clause.Region.Kind,
                clause.Region.HandlerOffset,
                clause.Region.HandlerLength,
                clause.Region.CatchType,
                clause.Region.FilterOffset,
                _controlFlow.Project(clause.HandlerBody, method, definitions, groupIds, group),
                clause.FilterBody is null
                    ? null
                    : _controlFlow.Project(clause.FilterBody, method, definitions, groupIds, group))
            {
                HandlerBlock = new(method.ValidatedGraph.Graph
                    .GetBlockAtOffset(clause.Region.HandlerOffset).Index),
                FilterBlock = clause.Region.FilterOffset is int filterOffset
                    ? new(method.ValidatedGraph.Graph.GetBlockAtOffset(filterOffset).Index)
                    : null,
            }).ToImmutableArray(),
            continuations,
            group.ContinuationDispatcher is null
                ? null
                : _controlFlow.Project(group.ContinuationDispatcher, method, definitions, groupIds, parent),
            group.ContinuationJoinBlock is int join ? new(join) : null)
        {
            ProtectedBlocks = [.. definitions.Values
                .Where(block => block.StartOffset >= group.TryOffset &&
                    block.StartOffset < group.TryOffset + group.TryLength)
                .OrderBy(block => block.Id.Value)
                .Select(block => block.Id)],
        };
    }
}
