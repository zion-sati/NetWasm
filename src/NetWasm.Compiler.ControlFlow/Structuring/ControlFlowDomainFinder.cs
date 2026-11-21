// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ControlFlowDomainFinder(
    IReachableBlockFinder reachableBlocks,
    IExceptionRegionClaimOrderer claimOrder) : IControlFlowDomainFinder
{
    private readonly IReachableBlockFinder _reachableBlocks = reachableBlocks ?? throw new ArgumentNullException(nameof(reachableBlocks));
    private readonly IExceptionRegionClaimOrderer _claimOrder = claimOrder ?? throw new ArgumentNullException(nameof(claimOrder));
    public ImmutableArray<ControlFlowDomain> Find(
        ControlFlowGraph graph)
    {
        var domains = ImmutableArray.CreateBuilder<ControlFlowDomain>();
        var unclaimed = graph.ReachableBlocks;
        AddDomain(graph.Entry.Index);
        // A handler claims its code before the handlers nested in it can claim the parts they leave to.
        foreach (var region in _claimOrder.Order(graph.MethodBody.ExceptionRegions))
        {
            AddDomain(graph.GetBlockAtOffset(region.HandlerOffset).Index);
            if (region.FilterOffset is int filterOffset)
            {
                AddDomain(graph.GetBlockAtOffset(filterOffset).Index);
            }
        }
        return domains.ToImmutable();

        void AddDomain(int entry)
        {
            // A handler can be the only normal predecessor of a continuation
            // outside its lexical range. Follow those leave edges as well, so
            // every reachable cycle belongs to a loop-analysis domain. Exclude
            // previously claimed paths to keep shared continuations single-owned;
            // normal reachability always claims a whole SCC, never part of one.
            var reachable = _reachableBlocks.Find(graph, entry, unclaimed);
            unclaimed = unclaimed.Except(reachable);
            domains.Add(new ControlFlowDomain(entry, reachable));
        }
    }

}
