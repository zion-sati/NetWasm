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
        var sourceBlocks = state.Graph.Blocks
            .SkipWhile(block => block.StartOffset < source.TryOffset)
            .TakeWhile(block => block.StartOffset < source.TryEnd)
            .Select(block => block.Index)
            .ToImmutableHashSet();
        foreach (var blockIndex in sourceBlocks)
        {
            var sourcePart = FindPart(state.Graph.GetBlock(blockIndex).StartOffset);
            foreach (var successor in state.Graph.Successors[blockIndex].Intersect(sourceBlocks))
            {
                var successorOffset = state.Graph.GetBlock(successor).StartOffset;
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

        int FindPart(int offset)
        {
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
