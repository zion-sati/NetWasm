using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ControlFlowDomainFinder(IReachableBlockFinder reachableBlocks) : IControlFlowDomainFinder
{
    private readonly IReachableBlockFinder _reachableBlocks = reachableBlocks ?? throw new ArgumentNullException(nameof(reachableBlocks));
    public ImmutableArray<ControlFlowDomain> Find(
        ControlFlowGraph graph)
    {
        var domains = ImmutableArray.CreateBuilder<ControlFlowDomain>();
        AddDomain(graph.Entry.Index, graph.ReachableBlocks);
        foreach (var region in graph.MethodBody.ExceptionRegions)
        {
            AddRange(region.HandlerOffset, region.HandlerLength);
            if (region.FilterOffset is int filterOffset)
            {
                AddRange(filterOffset, region.HandlerOffset - filterOffset);
            }
        }
        return domains.ToImmutable();

        void AddRange(int offset, int length)
        {
            var end = checked(offset + length);
            var blocks = graph.Blocks
                .Where(block => block.StartOffset >= offset && block.StartOffset < end)
                .Select(block => block.Index)
                .ToImmutableHashSet();
            if (!blocks.IsEmpty)
            {
                AddDomain(graph.GetBlockAtOffset(offset).Index, blocks);
            }
        }

        void AddDomain(int entry, ImmutableHashSet<int> allowed)
        {
            var reachable = _reachableBlocks.Find(graph, entry, allowed);
            // Valid exception clauses have disjoint handler/filter ranges, so
            // each range contributes a distinct control-flow domain.
            domains.Add(new ControlFlowDomain(entry, reachable));
        }
    }

}
