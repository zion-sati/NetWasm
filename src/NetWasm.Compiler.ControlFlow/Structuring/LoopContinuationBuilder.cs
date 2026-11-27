// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Draft;
using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// A protected block's continuation inside a loop body, structured with the loop's break and continue targets. A
// continue goes to the loop's continue block (its latch, or the header when it has none), which every path back to the
// header passes through, so a continuation that reaches that block continues there rather than running it and
// continuing into it again.
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
        state.LoopContinueTargets.Push(loop.Continue);
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
