using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IEmptyFinallyRemover
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A finally or fault handler left with nothing to run, as an inlined empty
// Dispose leaves the one around a foreach over a struct enumerator, protects
// nothing. Without its region a throw inside the try reaches no handler in the
// method, so it is no point at which the method's references must be published,
// and the planners that skip methods with exception regions consider the method.
// A leave out of the region becomes a branch unless it still leaves another
// one; validation requires an empty stack at a leave, so the branch moves no
// values. The handler, which nothing enters any more, becomes nops. .NET's JIT
// removes empty finallys the same way.
internal sealed class EmptyFinallyRemover(bool enabled) : IEmptyFinallyRemover
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled) return body;
        var empty = body.ExceptionRegions.Where(region => IsEmpty(body, region)).ToArray();
        if (empty.Length == 0) return body;

        var remaining = body.ExceptionRegions.Where(region => !empty.Contains(region)).ToArray();
        var instructions = body.Instructions.ToArray();
        for (var index = 0; index < instructions.Length; index++)
        {
            var instruction = instructions[index];
            if (empty.Any(region => Contains(region.HandlerOffset, region.HandlerLength, instruction.Offset)))
            {
                instructions[index] = instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
                continue;
            }
            if (instruction is not { Operation: CilOperation.Leave, Operand: CilOperand.BranchTarget { Offset: var target } } ||
                !empty.Any(region => Leaves(region, instruction.Offset, target)) ||
                remaining.Any(region => Leaves(region, instruction.Offset, target)))
            {
                continue;
            }
            instructions[index] = instruction with { Operation = CilOperation.Branch };
        }
        return body with { Instructions = [.. instructions], ExceptionRegions = [.. remaining] };
    }

    // A handler that runs no code and is followed by more of the method, so
    // its nops do not fall off the end.
    private static bool IsEmpty(CilMethodBody body, CilExceptionRegion region) =>
        region.Kind is CilExceptionRegionKind.Finally or CilExceptionRegionKind.Fault &&
        body.Instructions.Any(instruction => instruction.Offset >= region.HandlerOffset + region.HandlerLength) &&
        body.Instructions
            .Where(instruction => Contains(region.HandlerOffset, region.HandlerLength, instruction.Offset))
            .All(instruction => instruction.Operation is CilOperation.Nop or CilOperation.EndFinally);

    private static bool Leaves(CilExceptionRegion region, int source, int target) =>
        Contains(region.TryOffset, region.TryLength, source) && !Contains(region.TryOffset, region.TryLength, target) ||
        Contains(region.HandlerOffset, region.HandlerLength, source) &&
        !Contains(region.HandlerOffset, region.HandlerLength, target);

    private static bool Contains(int start, int length, int offset) => offset >= start && offset < start + length;
}
