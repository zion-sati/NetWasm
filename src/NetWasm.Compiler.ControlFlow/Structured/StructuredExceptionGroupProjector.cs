// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
                CatchTypeIdentity = clause.Region.CatchTypeIdentity,
                PassThroughLocal = clause.Region.PassThroughLocal,
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
            ProtectedBlocks = ProtectedBlocks(definitions, group.TryOffset, group.TryOffset + group.TryLength),
        };
    }

    // The method's block definitions by start, built once per method's definitions (every group of the method shares
    // them), so a group's protected blocks are a binary search, not a scan of the method per group.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>, StructuredBlockDefinition[]> s_byStart = new();

    private static ImmutableArray<StructuredBlockId> ProtectedBlocks(
        ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> definitions,
        int start,
        int end)
    {
        var byStart = s_byStart.GetValue(definitions,
            all => [.. all.Values.OrderBy(block => block.StartOffset).ThenBy(block => block.Id.Value)]);
        var low = 0;
        var high = byStart.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (byStart[middle].StartOffset < start) low = middle + 1;
            else high = middle;
        }
        var blocks = new List<StructuredBlockId>();
        for (var index = low; index < byStart.Length && byStart[index].StartOffset < end; index++)
        {
            blocks.Add(byStart[index].Id);
        }
        return [.. blocks.OrderBy(id => id.Value)];
    }
}
