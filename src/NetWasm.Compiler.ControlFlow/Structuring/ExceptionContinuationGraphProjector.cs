using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ExceptionContinuationGraphProjector : IExceptionContinuationGraphProjector
{
    public ControlFlowGraph Project(ControlFlowGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var successors = graph.Successors.ToArray();
        foreach (var region in graph.MethodBody.ExceptionRegions)
        {
            if (region.Kind is not (CilExceptionRegionKind.Catch or CilExceptionRegionKind.Filter))
            {
                continue;
            }

            var entry = graph.GetBlockAtOffset(region.TryOffset).Index;
            var handlerEnd = checked(region.HandlerOffset + region.HandlerLength);
            var continuations = graph.Blocks
                .Where(block => graph.ReachableBlocks.Contains(block.Index) &&
                    block.StartOffset >= region.HandlerOffset && block.StartOffset < handlerEnd &&
                    block.Terminator.Operation == CilOperation.Leave)
                .Select(block => ((CilOperand.BranchTarget)block.Terminator.Operand).Offset)
                .Where(offset => offset < region.HandlerOffset || offset >= handlerEnd)
                .Select(offset => graph.GetBlockAtOffset(offset).Index);
            successors[entry] = [.. successors[entry].Concat(continuations).Distinct().Order()];
        }

        var predecessors = new ImmutableArray<int>.Builder[successors.Length];
        for (var block = 0; block < predecessors.Length; block++) predecessors[block] = ImmutableArray.CreateBuilder<int>();
        for (var source = 0; source < successors.Length; source++)
        {
            foreach (var target in successors[source])
            {
                predecessors[target].Add(source);
            }
        }

        // These summary edges describe leaving a complete exception region.
        // They are for cycle, join and overlap discovery, never stack validation or emission.
        return new ControlFlowGraph(
            graph.MethodBody,
            graph.Blocks,
            BlockEdgeDictionary.Over(successors),
            BlockEdgeDictionary.Over(Array.ConvertAll(predecessors, builder => builder.ToImmutable())),
            graph.ExceptionalSuccessors,
            graph.ExceptionalPredecessors,
            graph.ReachableBlocks);
    }
}
