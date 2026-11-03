using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Loops;

internal interface IConstantTripLoopUnroller
{
    CilMethodBody Rewrite(CilMethodBody body);
}

// A for loop over a constant range, `for (i = c; i < k; i++) body`, as a loop
// over a vector's lanes is, compares and branches once per iteration and reads
// i where a constant would let its body fold: GetElement(v, i) checks the lane
// and selects among them. Where the loop runs at most MaximumTrips times and
// its copies stay small, its body runs once for each value of i in a row
// instead, reading i as that constant. Each copy first stores i, so a break,
// a return or a throw leaves i as the loop would, a continue moves on to the
// next copy, and the last copy leaves i at k.
internal sealed class ConstantTripLoopUnroller(bool enabled) : IConstantTripLoopUnroller
{
    private const int MaximumTrips = 8;

    // The copies together, nops left out.
    private const int MaximumInstructions = 96;

    private sealed record Loop(int Head, int Increment, int Back, int Counter, int Start, int End);

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled || body.Instructions.Length < 8)
        {
            return body;
        }
        var instructions = body.Instructions;
        var indices = Enumerable.Range(0, instructions.Length).ToDictionary(index => instructions[index].Offset);
        List<CilInstruction>[]? replacements = null;
        var rewritten = new bool[instructions.Length];
        for (var head = 2; head < instructions.Length; head++)
        {
            if (Match(body, indices, head) is not { } loop ||
                Enumerable.Range(loop.Head, loop.Back - loop.Head + 1).Any(index => rewritten[index]))
            {
                continue;
            }
            replacements ??= [.. instructions.Select(static instruction => new List<CilInstruction> { instruction })];
            replacements[loop.Head] = Unroll(instructions, indices, loop);
            for (var index = loop.Head; index <= loop.Back; index++)
            {
                rewritten[index] = true;
                if (index != loop.Head) replacements[index] = [];
            }
        }
        return replacements is null ? body : RenumberedInstructions.Splice(body, replacements);
    }

    // `ldc c; stloc i; br condition`, the body, `ldloc i; ldc 1; add; stloc i`,
    // then `condition: ldloc i; ldc k; blt body` (or ble), as Roslyn compiles a
    // for loop. Nothing else stores or addresses i; the body branches only
    // forward within itself, to the increment, or out of the loop; nothing
    // outside branches into the loop; and no protected region starts or ends
    // inside it.
    private static Loop? Match(CilMethodBody body, Dictionary<int, int> indices, int head)
    {
        var instructions = body.Instructions;
        if (instructions[head] is not { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget { Offset: var conditionOffset } } ||
            !indices.TryGetValue(conditionOffset, out var condition) || condition <= head + 4 ||
            instructions[head - 2] is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var start } } ||
            instructions[head - 1] is not { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index { Value: var counter } } ||
            !IsLocal(instructions[condition], CilOperation.LoadLocal, counter))
        {
            return null;
        }
        var bound = condition + 1;
        while (bound < instructions.Length && instructions[bound].Operation == CilOperation.Nop) bound++;
        if (bound + 1 >= instructions.Length ||
            instructions[bound] is not { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: var limit } } ||
            instructions[bound + 1] is not { Operand: CilOperand.BranchTarget { Offset: var bodyOffset } } back ||
            bodyOffset != instructions[head + 1].Offset ||
            back.Operation is not (CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanOrEqualSigned))
        {
            return null;
        }
        var end = back.Operation == CilOperation.BranchIfLessThanSigned ? (long)limit : (long)limit + 1;
        var increment = condition - 4;
        var trips = end - start;
        if (!(IsLocal(instructions[increment], CilOperation.LoadLocal, counter) &&
              instructions[increment + 1] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: 1 } } &&
              instructions[increment + 2].Operation == CilOperation.Add &&
              IsLocal(instructions[increment + 3], CilOperation.StoreLocal, counter)) ||
            trips is < 1 or > MaximumTrips || end > int.MaxValue ||
            trips * Enumerable.Range(head + 1, increment - head - 1)
                .Count(index => instructions[index].Operation != CilOperation.Nop) > MaximumInstructions)
        {
            return null;
        }
        var loop = new Loop(head, increment, bound + 1, counter, start, (int)end);
        return CounterIsOnlyCounted(instructions, loop) && BranchesStayInShape(instructions, indices, loop) &&
            body.ExceptionRegions.All(region => Outside(region, instructions, indices, loop))
            ? loop
            : null;
    }

    private static bool IsLocal(CilInstruction instruction, CilOperation operation, int local) =>
        instruction.Operation == operation && instruction.Operand is CilOperand.Index { Value: var index } && index == local;

    // Only the loop's initializer and increment store i, the body reads it at
    // most, and nothing anywhere takes its address.
    private static bool CounterIsOnlyCounted(ImmutableArray<CilInstruction> instructions, Loop loop) =>
        !instructions.Any(instruction => IsLocal(instruction, CilOperation.LoadLocalAddress, loop.Counter)) &&
        !Enumerable.Range(loop.Head + 1, loop.Increment - loop.Head - 1)
            .Any(index => IsLocal(instructions[index], CilOperation.StoreLocal, loop.Counter));

    private static bool BranchesStayInShape(ImmutableArray<CilInstruction> instructions, Dictionary<int, int> indices, Loop loop)
    {
        for (var index = 0; index < instructions.Length; index++)
        {
            if (instructions[index].Operand is not CilOperand.BranchTarget { Offset: var offset } ||
                index == loop.Head || index == loop.Back)
            {
                continue;
            }
            var target = indices[offset];
            var inside = target > loop.Head && target <= loop.Back;
            if (index > loop.Head && index < loop.Increment
                    ? inside && !(target > index && target <= loop.Increment)
                    : inside)
            {
                return false;
            }
        }
        return true;
    }

    // A protected region holds the whole loop or none of it. One that ends
    // inside the loop starts inside it too: a branch enters a region only at
    // its start, and the back edge would enter it elsewhere.
    private static bool Outside(CilExceptionRegion region, ImmutableArray<CilInstruction> instructions, Dictionary<int, int> indices, Loop loop)
    {
        var first = instructions[loop.Head].Offset;
        var last = instructions[loop.Back].Offset;
        bool Inside(int offset) => offset > first && offset <= last;
        return !Inside(region.TryOffset) && !Inside(region.HandlerOffset) &&
            !(region.FilterOffset is { } filter && Inside(filter));
    }

    // Each copy: `ldc v; stloc i`, then the body with i read as v, a continue
    // landing on the next copy; after the last, `ldc k; stloc i`.
    private static List<CilInstruction> Unroll(ImmutableArray<CilInstruction> instructions, Dictionary<int, int> indices, Loop loop)
    {
        var bodyStart = loop.Head + 1;
        var copySize = 2 + loop.Increment - bodyStart;
        var unrolled = new List<CilInstruction>();
        void Add(CilOperation operation, CilOperand operand) => unrolled.Add(new CilInstruction(0, 1, operation, operand));
        for (var value = loop.Start; value <= loop.End; value++)
        {
            Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(value));
            Add(CilOperation.StoreLocal, new CilOperand.Index(loop.Counter));
            if (value == loop.End)
            {
                break;
            }
            var copy = (value - loop.Start) * copySize;
            for (var index = bodyStart; index < loop.Increment; index++)
            {
                var instruction = instructions[index];
                if (IsLocal(instruction, CilOperation.LoadLocal, loop.Counter))
                {
                    Add(CilOperation.LoadInt32, new CilOperand.ConstantI4(value));
                }
                else if (instruction.Operand is CilOperand.BranchTarget { Offset: var offset } &&
                    indices[offset] is var target && target > loop.Head && target <= loop.Increment)
                {
                    Add(instruction.Operation, RenumberedInstructions.Local(target == loop.Increment
                        ? copy + copySize
                        : copy + 2 + target - bodyStart));
                }
                else
                {
                    Add(instruction.Operation, instruction.Operand);
                }
            }
        }
        return unrolled;
    }
}
