using NetWasm.Compiler.ControlFlow.Draft;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class BlockRangeStructurer(IStructuredControlFlowBuilder structuredControlFlow) : IBlockRangeStructurer
{
    private readonly IStructuredControlFlowBuilder _structuredControlFlow = structuredControlFlow ?? throw new ArgumentNullException(nameof(structuredControlFlow));
    public StructuredSequenceDraft Structure(
        ControlFlowStructuringState state,
        int offset,
        int length)
    {
        var end = checked(offset + length);
        var allowed = state.Graph.Blocks
            .Where(block => block.StartOffset >= offset && block.StartOffset < end)
            .Select(block => block.Index)
            .ToImmutableHashSet();
        return _structuredControlFlow.Build(state,
            state.Graph.GetBlockAtOffset(offset).Index,
            stop: null,
            allowed, new HashSet<int>());
    }
}
