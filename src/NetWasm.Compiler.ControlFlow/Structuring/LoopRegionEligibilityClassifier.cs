// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class LoopRegionEligibilityClassifier(
    IControlFlowSubgraphCycleClassifier cycles) :
    ILoopRegionEligibilityClassifier
{
    private readonly IControlFlowSubgraphCycleClassifier _cycles = cycles ??
        throw new ArgumentNullException(nameof(cycles));

    public bool Classify(LoopRegionEligibilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Graph);
        ArgumentNullException.ThrowIfNull(request.NaturalLoop);
        ArgumentNullException.ThrowIfNull(request.Region);
        ArgumentNullException.ThrowIfNull(request.Domain);

        var graph = request.Graph;
        var naturalLoop = request.NaturalLoop;
        var region = request.Region;
        if (!region.CoreComponent.SetEquals(naturalLoop))
        {
            return false;
        }
        if (!naturalLoop.IsSubsetOf(request.Domain.Blocks))
        {
            return false;
        }
        if (!naturalLoop.Contains(region.Header) ||
            region.Condition is int condition && !naturalLoop.Contains(condition) ||
            !naturalLoop.Contains(region.Continue))
        {
            return false;
        }
        if (naturalLoop.Contains(region.ConditionExit) ||
            naturalLoop.Contains(region.Exit))
        {
            return false;
        }
        if (!naturalLoop.IsSubsetOf(region.BodyComponent))
        {
            return false;
        }
        if (region.BodyComponent.Overlaps(region.ConditionExitComponent))
        {
            return false;
        }
        if (!region.Component.IsSubsetOf(graph.ReachableBlocks))
        {
            return false;
        }

        var extension = region.Component.Except(naturalLoop);
        if (_cycles.Classify(graph, extension))
        {
            return false;
        }

        foreach (var block in extension)
        {
            // Incoming edges do not make a closed exit extension unsafe. The
            // structured IR owns block occurrences, then selects one canonical
            // body definition, so mutually exclusive paths can reference the
            // same original block without executing it twice. Outgoing edges,
            // cycles and re-entry remain structural constraints below.
            if (graph.Successors[block].Any(successor =>
                    naturalLoop.Contains(successor) ||
                    successor != region.Exit &&
                    !region.Component.Contains(successor)))
            {
                return false;
            }
        }

        var outsideDomain = extension.Except(request.Domain.Blocks);
        if (outsideDomain.IsEmpty)
        {
            return true;
        }
        if (graph.MethodBody.ExceptionRegions.IsEmpty ||
            !outsideDomain.IsSubsetOf(request.Domains
                .SelectMany(domain => domain.Blocks)
                .ToImmutableHashSet()))
        {
            return false;
        }

        return outsideDomain.All(block => graph.Predecessors[block]
            .Where(region.Component.Contains)
            .Where(request.Domain.Blocks.Contains)
            .All(predecessor =>
                graph.GetBlock(predecessor).Terminator.Operation ==
                CilOperation.Leave));
    }

}
