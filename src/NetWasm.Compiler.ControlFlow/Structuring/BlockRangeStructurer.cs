// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ControlFlow.Draft;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

// Structures the blocks that start within a range: a protected block's or a handler's (BlocksStarting finds them).
// Where that needs a dispatcher, in a whole protected block or handler that holds no region of its own, the reducible
// structurer places the range's blocks instead, when their control flow is reducible: labels route its transfers
// without a program counter. A lowered runtime-async method's body is such a range: its resumption enters code the
// first call also runs, which only labels express.
internal sealed class BlockRangeStructurer(
    IStructuredControlFlowBuilder structuredControlFlow,
    IReducibleControlFlowStructurer? reducibleControlFlow = null) : IBlockRangeStructurer
{
    private readonly IStructuredControlFlowBuilder _structuredControlFlow = structuredControlFlow ?? throw new ArgumentNullException(nameof(structuredControlFlow));

    public StructuredSequenceDraft Structure(
        ControlFlowStructuringState state,
        int offset,
        int length)
    {
        var allowed = BlocksStarting.Between(state.Graph, offset, checked(offset + length));
        var entry = state.Graph.GetBlockAtOffset(offset).Index;
        var structured = _structuredControlFlow.Build(state,
            entry,
            stop: null,
            allowed, new HashSet<int>());
        return reducibleControlFlow is not null &&
            StructuredDispatcherDrafts.Contains(structured) &&
            HoldsNoOtherRegion(state.Graph.MethodBody.ExceptionRegions, offset, length) &&
            reducibleControlFlow.Structure(state.Validated, new StructuringRange(entry, allowed)) is { } labeled
                ? labeled
                : structured;
    }

    // Whether the range is a whole protected block or handler inside which no other region starts: its only edges out
    // are its leaves, and no transfer inside it crosses a region's boundary.
    private static bool HoldsNoOtherRegion(ImmutableArray<CilExceptionRegion> regions, int offset, int length)
    {
        var end = offset + length;
        var whole = false;
        foreach (var region in regions)
        {
            var asProtected = region.TryOffset == offset && region.TryLength == length;
            var asHandler = region.HandlerOffset == offset && region.HandlerLength == length;
            whole |= asProtected || asHandler;
            if ((!asProtected && Inside(region.TryOffset)) || (!asHandler && Inside(region.HandlerOffset)) ||
                (region.FilterOffset is int filter && Inside(filter)))
            {
                return false;
            }
        }
        return whole;

        bool Inside(int at) => at >= offset && at < end;
    }
}
