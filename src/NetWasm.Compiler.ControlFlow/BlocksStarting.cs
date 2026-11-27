// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

// The blocks that start within a range of offsets, a protected block's or a handler's. A graph's blocks are in offset
// order (CilControlFlowGraphBuilder makes one per leader, in order, and numbers them so), so the blocks in a range are
// a run of indices a binary search finds: a scan of every block for each range made a method with N protected blocks
// cost N squared.
internal static class BlocksStarting
{
    // The indices of the blocks that start at or after start and before end.
    internal static ImmutableHashSet<int> Between(ControlFlowGraph graph, int start, int end)
    {
        var blocks = graph.Blocks;
        var low = 0;
        var high = blocks.Length;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (blocks[middle].StartOffset < start) low = middle + 1;
            else high = middle;
        }
        var found = ImmutableHashSet.CreateBuilder<int>();
        for (var index = low; index < blocks.Length && blocks[index].StartOffset < end; index++) found.Add(blocks[index].Index);
        return found.ToImmutable();
    }
}
