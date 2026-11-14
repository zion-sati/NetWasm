using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class NormalLeaveTargetFinder : INormalLeaveTargetFinder
{
    public int[] Find(ControlFlowStructuringState state,
        IEnumerable<CilExceptionRegion> regions,
        IReadOnlyCollection<ExceptionGroupSource> excluded,
        IReadOnlyCollection<ExceptionGroupSource> handlerExcluded)
    {
        var array = regions.ToArray();
        var tryOffset = array[0].TryOffset;
        var tryEnd = checked(tryOffset + array[0].TryLength);
        (int Start, int End)[] ranges = [.. array
            .Select(region => (
                region.HandlerOffset,
                checked(region.HandlerOffset + region.HandlerLength)))
            .Prepend((tryOffset, tryEnd))];
        var targets = state.Graph.Blocks
            .Where(block => state.Graph.ReachableBlocks.Contains(block.Index))
            .Where(block => ranges.Any(range =>
                    block.StartOffset >= range.Start && block.StartOffset < range.End))
            .SelectMany(block => block.Instructions.Select(instruction => (
                Block: block,
                Instruction: instruction)))
            .Where(item => item.Instruction.Operation == CilOperation.Leave)
            .Where(item =>
            {
                var target = ((CilOperand.BranchTarget)item.Instruction.Operand).Offset;
                // A leave from a group inside a handler stays that group's own unless it
                // leaves the handler too, as a continue through a finally in a loop's catch
                // handler does: then this region continues there as well, so the nested
                // group's continuation routes through this one.
                if (Encloses(handlerExcluded, item.Block.StartOffset))
                {
                    return !ranges.Any(range => target >= range.Start && target < range.End);
                }
                var insideChild = excluded.Any(child =>
                    EqualityComparer<(bool StartsAfter, bool EndsBefore)>.Default.Equals(
                        (item.Block.StartOffset >= child.TryOffset,
                            item.Block.StartOffset < child.Extent),
                        (true, true)));
                // A child's leave stays inside this region only when its target
                // does; a backward leave to a loop header before the region
                // leaves it too.
                return !EqualityComparer<(bool InsideChild, bool InsideTry)>.Default.Equals(
                    (insideChild, target >= tryOffset && target < tryEnd),
                    (true, true));
            })
            .Select(item =>
                ((CilOperand.BranchTarget)item.Instruction.Operand).Offset)
            .Distinct()
            .ToArray();
        return [.. targets.Order()];
    }

    // Whether one of the groups, from its try to the end of its last handler, holds the offset.
    private static bool Encloses(IReadOnlyCollection<ExceptionGroupSource> groups, int offset) =>
        groups.Any(group => EqualityComparer<(bool StartsAfter, bool EndsBefore)>.Default.Equals(
            (offset >= group.TryOffset, offset < group.Extent), (true, true)));
}
