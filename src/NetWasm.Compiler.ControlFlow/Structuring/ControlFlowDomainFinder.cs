using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ControlFlowDomainFinder(IReachableBlockFinder reachableBlocks) : IControlFlowDomainFinder
{
    private readonly IReachableBlockFinder _reachableBlocks = reachableBlocks ?? throw new ArgumentNullException(nameof(reachableBlocks));
    public ImmutableArray<ControlFlowDomain> Find(
        ControlFlowGraph graph)
    {
        var domains = ImmutableArray.CreateBuilder<ControlFlowDomain>();
        var unclaimed = graph.ReachableBlocks;
        AddDomain(graph.Entry.Index);
        foreach (var region in graph.MethodBody.ExceptionRegions)
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
