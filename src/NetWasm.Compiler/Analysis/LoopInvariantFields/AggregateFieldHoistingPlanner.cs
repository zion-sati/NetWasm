// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.LoopInvariantFields;

internal sealed class AggregateFieldHoistingPlanner(
    IControlFlowGraphBuilderFactory graphBuilders,
    IControlFlowGraphAnalyzerFactory graphAnalyzers,
    IFieldProjectionResolver projections,
    LoopInvariantFieldHoistingPolicy policy) : IAggregateFieldHoistingPlanner
{
    private readonly IControlFlowGraphBuilderFactory _graphBuilders = graphBuilders ??
        throw new ArgumentNullException(nameof(graphBuilders));
    private readonly IControlFlowGraphAnalyzerFactory _graphAnalyzers = graphAnalyzers ??
        throw new ArgumentNullException(nameof(graphAnalyzers));
    private readonly IFieldProjectionResolver _projections = projections ??
        throw new ArgumentNullException(nameof(projections));
    private readonly LoopInvariantFieldHoistingPolicy _policy = policy switch
    {
        LoopInvariantFieldHoistingPolicy.Disabled or
        LoopInvariantFieldHoistingPolicy.Conservative or
        LoopInvariantFieldHoistingPolicy.NullChecks => policy,
        _ => throw new ArgumentOutOfRangeException(nameof(policy)),
    };

    public AggregateFieldHoistingPlan Plan(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (_policy == LoopInvariantFieldHoistingPolicy.Disabled ||
            !body.ExceptionRegions.IsEmpty ||
            body.Instructions.IsEmpty)
        {
            return AggregateFieldHoistingPlan.Empty;
        }

        var graph = _graphBuilders.Create().Build(body);
        var analysis = _graphAnalyzers.Create().Analyze(
            graph,
            graph.Entry.Index,
            graph.ReachableBlocks);
        if (analysis.NaturalLoops.IsEmpty)
        {
            return AggregateFieldHoistingPlan.Empty;
        }

        var blockByOffset = graph.Blocks
            .SelectMany(block => block.Instructions.Select(instruction =>
                new KeyValuePair<int, int>(instruction.Offset, block.Index)))
            .ToImmutableDictionary();
        var addressUses = ReadAddressUses(body, graph);
        var stores = ReadStores(body);
        var claimedOffsets = new HashSet<int>();
        var hoists = ImmutableArray.CreateBuilder<AggregateFieldHoist>();

        foreach (var loop in analysis.NaturalLoops
                     .OrderBy(loop => loop.Count)
                     .ThenBy(loop => loop.Min()))
        {
            if (!TryResolvePreheader(graph, loop, out var header, out var preheader))
            {
                continue;
            }

            var preheaderBlock = graph.GetBlock(preheader);
            if (preheaderBlock.Terminator.Operation != CilOperation.Branch ||
                preheaderBlock.Terminator.Operand is not CilOperand.BranchTarget target ||
                target.Offset != graph.GetBlock(header).StartOffset)
            {
                continue;
            }

            var candidates = addressUses.Valid
                .Where(pair =>
                    loop.Contains(blockByOffset[pair.Key]) &&
                    !claimedOffsets.Contains(pair.Key) &&
                    !claimedOffsets.Contains(pair.Value.Projection.Instruction.Offset))
                .Select(pair => new ProjectionOccurrence(
                    pair.Key,
                    pair.Value.Projection.Instruction.Offset,
                    pair.Value.SourceLocalIndex,
                    pair.Value.Projection))
                .GroupBy(occurrence => new ProjectionKey(
                    occurrence.SourceLocalIndex,
                    occurrence.Projection.Field.Definition.Key,
                    occurrence.Projection.Field.DeclaringType.CanonicalName))
                .OrderBy(group => group.Key.SourceLocalIndex)
                .ThenBy(group => group.Key.FieldKey.MetadataToken)
                .ThenBy(group => group.Key.DeclaringType, StringComparer.Ordinal)
                .ToArray();

            foreach (var candidateGroup in candidates)
            {
                var occurrences = candidateGroup.OrderBy(value => value.AddressOffset).ToArray();
                var localIndex = candidateGroup.Key.SourceLocalIndex;
                if (addressUses.InvalidLocals.Contains(localIndex) ||
                    !IsStableLocal(
                        localIndex,
                        stores,
                        blockByOffset,
                        analysis,
                        loop,
                        preheader,
                        preheaderBlock.Terminator.Offset))
                {
                    continue;
                }

                var first = occurrences[0];
                var cachedLocalIndex = checked(body.LocalSignatureTypes.Length + hoists.Count);
                hoists.Add(new AggregateFieldHoist(
                    preheaderBlock.Terminator.Offset,
                    localIndex,
                    cachedLocalIndex,
                    first.Projection.Field.FieldType,
                    body.Instructions.Single(instruction =>
                        instruction.Offset == first.AddressOffset),
                    first.Projection.Instruction,
                    [.. occurrences.Select(value => value.AddressOffset)],
                    [.. occurrences.Select(value => value.ProjectionOffset)]));
                foreach (var occurrence in occurrences)
                {
                    claimedOffsets.Add(occurrence.AddressOffset);
                    claimedOffsets.Add(occurrence.ProjectionOffset);
                }
            }
        }

        return hoists.Count == 0
            ? AggregateFieldHoistingPlan.Empty
            : new AggregateFieldHoistingPlan(hoists.ToImmutable());
    }

    private AddressUseSnapshot ReadAddressUses(
        CilMethodBody body,
        ControlFlowGraph graph)
    {
        var valid = ImmutableDictionary.CreateBuilder<int, AddressUse>();
        var invalidLocals = ImmutableHashSet.CreateBuilder<int>();
        foreach (var block in graph.Blocks)
        {
            for (var index = 0; index < block.Instructions.Length; index++)
            {
                var address = block.Instructions[index];
                if (address.Operation != CilOperation.LoadLocalAddress ||
                    address.Operand is not CilOperand.Index local)
                {
                    continue;
                }

                if ((uint)local.Value >= (uint)body.LocalSignatureTypes.Length ||
                    index + 1 >= block.Instructions.Length ||
                    address.NextOffset != block.Instructions[index + 1].Offset)
                {
                    invalidLocals.Add(local.Value);
                    continue;
                }

                var projection = _projections.Resolve(
                    body.LocalSignatureTypes[local.Value],
                    block.Instructions[index + 1]);
                if (projection is null)
                {
                    invalidLocals.Add(local.Value);
                    continue;
                }

                valid.Add(address.Offset, new AddressUse(local.Value, projection));
            }
        }

        return new(valid.ToImmutable(), invalidLocals.ToImmutable());
    }

    private static ImmutableDictionary<int, ImmutableArray<CilInstruction>> ReadStores(
        CilMethodBody body) => body.Instructions
        .Where(instruction =>
            instruction.Operation == CilOperation.StoreLocal &&
            instruction.Operand is CilOperand.Index)
        .GroupBy(instruction => ((CilOperand.Index)instruction.Operand).Value)
        .ToImmutableDictionary(
            group => group.Key,
            group => group.ToImmutableArray());

    private static bool IsStableLocal(
        int localIndex,
        ImmutableDictionary<int, ImmutableArray<CilInstruction>> stores,
        ImmutableDictionary<int, int> blockByOffset,
        ControlFlowGraphAnalysis analysis,
        ImmutableHashSet<int> loop,
        int preheader,
        int preheaderTerminatorOffset)
    {
        if (!stores.TryGetValue(localIndex, out var localStores) ||
            localStores.Length != 1)
        {
            return false;
        }

        var store = localStores[0];
        var storeBlock = blockByOffset[store.Offset];
        return !loop.Contains(storeBlock) &&
            analysis.Dominators[preheader].Contains(storeBlock) &&
            (storeBlock != preheader || store.Offset < preheaderTerminatorOffset);
    }

    private static bool TryResolvePreheader(
        ControlFlowGraph graph,
        ImmutableHashSet<int> loop,
        out int header,
        out int preheader)
    {
        var headers = loop
            .Where(block => graph.Predecessors[block].Any(predecessor =>
                !loop.Contains(predecessor)))
            .ToArray();
        if (headers.Length != 1)
        {
            header = default;
            preheader = default;
            return false;
        }

        header = headers[0];
        var preheaders = graph.Predecessors[header]
            .Where(predecessor => !loop.Contains(predecessor))
            .ToArray();
        if (preheaders.Length != 1 ||
            graph.Successors[preheaders[0]].Length != 1 ||
            graph.Successors[preheaders[0]][0] != header)
        {
            preheader = default;
            return false;
        }

        preheader = preheaders[0];
        return true;
    }

    private sealed record AddressUse(
        int SourceLocalIndex,
        FieldProjection Projection);

    private sealed record AddressUseSnapshot(
        ImmutableDictionary<int, AddressUse> Valid,
        ImmutableHashSet<int> InvalidLocals);

    private readonly record struct ProjectionOccurrence(
        int AddressOffset,
        int ProjectionOffset,
        int SourceLocalIndex,
        FieldProjection Projection);

    private readonly record struct ProjectionKey(
        int SourceLocalIndex,
        EntityKey FieldKey,
        string DeclaringType);
}
