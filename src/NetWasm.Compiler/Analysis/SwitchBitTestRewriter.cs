using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// CIL switch decoding lowers each case to dup, ldc.i4 k, bne.un next, pop,
// br target, with a final pop before the default. When every case goes to
// one or two targets, as for `value is 1 or 3 or 5 or 7`, a range check and a
// bit test of a constant mask take the same branch in constant time, the way
// .NET 11's JIT folds a small set of constants.
internal sealed class SwitchBitTestRewriter : IMethodRewriteRule
{
    // The mask is one int32.
    internal const int MaximumCases = 32;
    private const int CaseLength = 5;

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var instructions = body.Instructions.ToArray();
        var entries = EntryOffsets(body);
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        var changed = false;
        for (var start = 0; start < instructions.Length; start++)
        {
            var cases = CaseCount(instructions, start);
            if (cases < 3 || cases > MaximumCases || Enters(entries, instructions, start, cases))
            {
                continue;
            }
            var targets = Enumerable.Range(0, cases)
                .Select(index => ((CilOperand.BranchTarget)instructions[start + index * CaseLength + 4].Operand).Offset)
                .ToArray();
            var distinct = targets.Distinct().ToArray();
            if (distinct.Length > 2)
            {
                continue;
            }
            var end = start + cases * CaseLength;
            ImmutableArray<(CilOperation Operation, CilOperand Operand)> replacement = distinct.Length == 1
                ? [
                    (CilOperation.LoadInt32, new CilOperand.ConstantI4(cases)),
                    (CilOperation.BranchIfLessThanUnsigned, new CilOperand.BranchTarget(distinct[0])),
                    (CilOperation.Branch, new CilOperand.BranchTarget(instructions[end].NextOffset)),
                ]
                : BitTest(cases, targets, distinct, instructions[end].Offset, locals.Count);
            if (distinct.Length == 2)
            {
                locals.Add(CliValueKind.I4);
                localTypes.Add(CliTypeIdentity.Primitive("i4", CliValueKind.I4));
            }
            // The final pop stays for the out-of-range path of a bit test; a range check alone consumes the value.
            var span = distinct.Length == 1 ? cases * CaseLength + 1 : cases * CaseLength;
            for (var index = 0; index < span; index++)
            {
                var original = instructions[start + index];
                instructions[start + index] = index < replacement.Length
                    ? original with { Operation = replacement[index].Operation, Operand = replacement[index].Operand }
                    : original with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
            }
            changed = true;
            start = end;
        }
        return changed
            ? body with
            {
                Instructions = [.. instructions],
                Locals = locals.ToImmutable(),
                LocalSignatureTypes = localTypes.ToImmutable(),
            }
            : body;
    }

    private static ImmutableArray<(CilOperation Operation, CilOperand Operand)> BitTest(
        int cases, int[] targets, int[] distinct, int finalPop, int local)
    {
        var mask = 0u;
        for (var index = 0; index < cases; index++)
        {
            if (targets[index] == distinct[0]) mask |= 1u << index;
        }
        return
        [
            (CilOperation.Duplicate, new CilOperand.None()),
            (CilOperation.LoadInt32, new CilOperand.ConstantI4(cases)),
            // Out of range: the final pop removes the value and falls into the default.
            (CilOperation.BranchIfGreaterThanOrEqualUnsigned, new CilOperand.BranchTarget(finalPop)),
            // In range, so narrowing a native integer loses nothing.
            (CilOperation.ConvertInt32, new CilOperand.None()),
            (CilOperation.StoreLocal, new CilOperand.Index(local)),
            (CilOperation.LoadInt32, new CilOperand.ConstantI4(unchecked((int)mask))),
            (CilOperation.LoadLocal, new CilOperand.Index(local)),
            (CilOperation.ShiftRightUnsigned, new CilOperand.None()),
            (CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            (CilOperation.BitwiseAnd, new CilOperand.None()),
            (CilOperation.BranchIfTrue, new CilOperand.BranchTarget(distinct[0])),
            (CilOperation.Branch, new CilOperand.BranchTarget(distinct[1])),
        ];
    }

    // The number of lowered cases starting at the instruction, if a final pop follows them.
    private static int CaseCount(CilInstruction[] instructions, int start)
    {
        var cases = 0;
        while (IsCase(instructions, start + cases * CaseLength, cases))
        {
            cases++;
        }
        // A matched case guarantees an instruction after it.
        return cases > 0 && instructions[start + cases * CaseLength].Operation == CilOperation.Pop ? cases : 0;
    }

    private static bool IsCase(CilInstruction[] instructions, int at, int value) =>
        at + CaseLength < instructions.Length &&
        instructions[at].Operation == CilOperation.Duplicate &&
        instructions[at + 1] is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 constant } &&
        constant.Value == value &&
        instructions[at + 2] is { Operation: CilOperation.BranchIfNotEqual, Operand: CilOperand.BranchTarget next } &&
        next.Offset == instructions[at + CaseLength].Offset &&
        instructions[at + 3].Operation == CilOperation.Pop &&
        instructions[at + 4] is { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget };

    // Whether control can enter the chain anywhere but its first instruction.
    private static bool Enters(Dictionary<int, int> entries, CilInstruction[] instructions, int start, int cases)
    {
        for (var index = start + 1; index <= start + cases * CaseLength; index++)
        {
            var offset = instructions[index].Offset;
            // Each case's bne.un targets the next case: that is the chain's own edge.
            var internalEdge = (index - start) % CaseLength == 0 ? 1 : 0;
            if (entries.GetValueOrDefault(offset) > internalEdge)
            {
                return true;
            }
        }
        return false;
    }

    private static Dictionary<int, int> EntryOffsets(CilMethodBody body)
    {
        var entries = new Dictionary<int, int>();
        void Add(int offset) => entries[offset] = entries.GetValueOrDefault(offset) + 1;
        foreach (var instruction in body.Instructions)
        {
            if (instruction.Operand is CilOperand.BranchTarget target)
            {
                Add(target.Offset);
            }
        }
        foreach (var region in body.ExceptionRegions)
        {
            // A region boundary counts as an entry twice, so no chain may contain one.
            foreach (var offset in new[] { region.TryOffset, region.TryOffset + region.TryLength,
                region.HandlerOffset, region.HandlerOffset + region.HandlerLength })
            {
                Add(offset);
                Add(offset);
            }
            if (region.FilterOffset is { } filter)
            {
                Add(filter);
                Add(filter);
            }
        }
        return entries;
    }
}
