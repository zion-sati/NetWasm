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

        var successors = graph.Successors.ToBuilder();
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

        var predecessors = graph.Blocks.ToDictionary(
            block => block.Index,
            _ => ImmutableArray.CreateBuilder<int>());
        foreach (var (source, targets) in successors)
        {
            foreach (var target in targets)
            {
                predecessors[target].Add(source);
            }
        }

        // These summary edges describe leaving a complete exception region.
        // They are only for cycle discovery, never stack validation or emission.
        return new ControlFlowGraph(
            graph.MethodBody,
            graph.Blocks,
            successors.ToImmutable(),
            predecessors.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.ToImmutable()),
            graph.ExceptionalSuccessors,
            graph.ExceptionalPredecessors,
            graph.ReachableBlocks);
    }
}
