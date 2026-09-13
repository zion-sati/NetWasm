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
            .Where(block => ranges.Any(range =>
                    block.StartOffset >= range.Start && block.StartOffset < range.End))
            .SelectMany(block => block.Instructions.Select(instruction => (
                Block: block,
                Instruction: instruction)))
            .Where(item => item.Instruction.Operation == CilOperation.Leave)
            .Where(item =>
            {
                if (handlerExcluded.Any(child =>
                        item.Block.StartOffset >= child.TryOffset &&
                        item.Block.StartOffset < child.Extent))
                {
                    return false;
                }
                var insideChild = excluded.Any(child =>
                    EqualityComparer<(bool StartsAfter, bool EndsBefore)>.Default.Equals(
                        (item.Block.StartOffset >= child.TryOffset,
                            item.Block.StartOffset < child.Extent),
                        (true, true)));
                var target = ((CilOperand.BranchTarget)item.Instruction.Operand).Offset;
                return !EqualityComparer<(bool InsideChild, bool BeforeTryEnd)>.Default.Equals(
                    (insideChild, target < tryEnd),
                    (true, true));
            })
            .Select(item =>
                ((CilOperand.BranchTarget)item.Instruction.Operand).Offset)
            .Distinct()
            .ToArray();
        return [.. targets.Order()];
    }
}
