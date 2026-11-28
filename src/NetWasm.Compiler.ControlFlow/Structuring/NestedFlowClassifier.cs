// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class NestedFlowClassifier : INestedFlowClassifier
{
    public bool Classify(ControlFlowStructuringState state,
        ExceptionGroupSource source,
        ExceptionGroupSource[] children)
    {
        if (children.Length == 0)
        {
            return false;
        }
        var partStarts = children
            .Select(child => child.TryOffset)
            .Prepend(source.TryOffset)
            .ToArray();
        var sorted = true;
        for (var index = 1; index < partStarts.Length; index++) sorted &= partStarts[index - 1] <= partStarts[index];
        var sourceBlocks = BlocksStarting.Between(state.Graph, source.TryOffset, source.TryEnd);
        var visited = new HashSet<int>();
        foreach (var blockIndex in sourceBlocks)
        {
            var sourcePart = FindPart(state.Graph.GetBlock(blockIndex).StartOffset);
            visited.Clear();
            foreach (var successor in state.Graph.Successors[blockIndex])
            {
                // A successor in the protected block starts within it, as sourceBlocks' blocks do: a range test, not a
                // set of the block's blocks made for each block.
                var successorOffset = state.Graph.GetBlock(successor).StartOffset;
                if (successorOffset < source.TryOffset || successorOffset >= source.TryEnd || !visited.Add(successor))
                {
                    continue;
                }
                var targetPart = FindPart(successorOffset);
                if (sourcePart == targetPart)
                {
                    continue;
                }
                // A valid protected transfer can only advance to the next
                // nested try part at its entry block. Keep scanning after that
                // transition because a later continuation can still return to
                // an earlier part and require dynamic nesting.
                if (EqualityComparer<(int Part, int Offset)>.Default.Equals(
                    (sourcePart + 1, successorOffset),
                    (targetPart, partStarts[targetPart])))
                {
                    continue;
                }
                return true;
            }
        }
        return false;

        // The last part that starts at or before the offset, by binary search over the parts' starts in offset order.
        int FindPart(int offset)
        {
            if (sorted)
            {
                var low = 1;
                var high = partStarts.Length;
                while (low < high)
                {
                    var middle = low + ((high - low) >> 1);
                    if (partStarts[middle] <= offset) low = middle + 1;
                    else high = middle;
                }
                return low - 1;
            }
            var part = 0;
            for (var index = 1; index < partStarts.Length; index++)
            {
                if (offset < partStarts[index])
                {
                    break;
                }
                part = index;
            }
            return part;
        }
    }
}
