using NetWasm.Compiler.ControlFlow.Draft;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class ContinuationDispatcherBuilder(
    IStructuredControlFlowBuilder structuredControlFlow,
    IReachableBlockFinder reachableBlocks) : IContinuationDispatcherBuilder
{
    private readonly IStructuredControlFlowBuilder _structuredControlFlow = structuredControlFlow ?? throw new ArgumentNullException(nameof(structuredControlFlow));
    private readonly IReachableBlockFinder _reachableBlocks = reachableBlocks ?? throw new ArgumentNullException(nameof(reachableBlocks));

    public StructuredDispatcherDraft Build(ControlFlowStructuringState state,
        IEnumerable<int> targets,
        int continuationBoundary,
        int methodEnd)
    {
        var targetArray = targets.Distinct().Order().ToArray();
        var end = targetArray.Any(target => target > continuationBoundary)
            ? methodEnd
            : continuationBoundary;
        var allowed = state.Graph.Blocks
            .Where(block => block.StartOffset >= targetArray[0] &&
                block.StartOffset < end)
            .Select(block => block.Index)
            .ToImmutableHashSet();
        var component = targetArray
            .Select(target => state.Graph.GetBlockAtOffset(target).Index)
            .SelectMany(target => _reachableBlocks.Find(state.Graph, target, allowed))
            .ToImmutableHashSet();
        var dispatcher = _structuredControlFlow.Build(
            state,
            null,
            component,
            allowed,
            null,
            []);
        return dispatcher with
        {
            Blocks = [.. dispatcher.Blocks.Select(block => block with
            {
                IsOriginal = MarkContinuationOwnership(block),
            })],
        };

        bool MarkContinuationOwnership(StructuredDispatcherBlockDraft block)
        {
            state.ContinuationOwnedBlocks.Add(block.Block.Index);
            return block.IsOriginal;
        }
    }
}
