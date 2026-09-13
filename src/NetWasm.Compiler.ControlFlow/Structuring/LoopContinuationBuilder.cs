using NetWasm.Compiler.ControlFlow.Draft;
using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class LoopContinuationBuilder(IBlockRangeStructurer ranges) : ILoopContinuationBuilder
{
    private readonly IBlockRangeStructurer _ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));

    public StructuredSequenceDraft Build(ControlFlowStructuringState state,
        int offset,
        int length,
        LoopRegion loop)
    {
        var start = state.Graph.GetBlockAtOffset(offset).Index;
        if (start == loop.Continue)
        {
            return new StructuredSequenceDraft([new StructuredLoopContinueDraft()]);
        }
        state.LoopExitTargets.Push(loop.Exit);
        state.LoopContinueTargets.Push(loop.Header);
        try
        {
            return _ranges.Structure(state, offset, length);
        }
        finally
        {
            state.LoopContinueTargets.Pop();
            state.LoopExitTargets.Pop();
        }
    }
}
