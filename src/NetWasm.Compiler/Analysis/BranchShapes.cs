// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// What the branch-to-select rewriters share: which instructions only compute a number and may run speculatively,
// the comparison that leaves a conditional branch's condition as an int32, where control enters a method, and the
// splice that replaces spans of instructions and renumbers the rest.
internal static class BranchShapes
{
    // The instructions [First, Last] become Replacement. A replacement that reads its condition from a new int32
    // local refers to it as ConditionLocal, the first index past the method's own locals; the splice gives each its
    // own.
    internal sealed record Span(int First, int Last, ImmutableArray<CilInstruction> Replacement, bool UsesConditionLocal = false);

    // The comparison that leaves a conditional branch's condition as an int32, whether that is true when the branch is
    // taken, and how many values the branch consumes.
    internal sealed record BranchCondition(
        ImmutableArray<(CilOperation Operation, CilOperand Operand)> Instructions, bool TakenWhenTrue, int Operands);

    internal static bool IsForwardConditionalBranch(CilInstruction instruction) =>
        instruction.Operation is CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or CilOperation.BranchIfEqual or
            CilOperation.BranchIfNotEqual or CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned or
            CilOperation.BranchIfGreaterThanOrEqualSigned or CilOperation.BranchIfGreaterThanOrEqualUnsigned or
            CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned or
            CilOperation.BranchIfLessThanOrEqualSigned or CilOperation.BranchIfLessThanOrEqualUnsigned &&
        instruction.Operand is CilOperand.BranchTarget target && target.Offset > instruction.Offset;

    internal static int CountForwardConditionalBranches(CilMethodBody body) =>
        body.Instructions.Count(IsForwardConditionalBranch);

    internal static bool IsSelectable(CliValueKind kind) =>
        kind is CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or CliValueKind.NativeInt;

    // The condition of a forward conditional branch (IsForwardConditionalBranch), whose entry stack holds its operands.
    internal static BranchCondition? Condition(CilOperation branch, ImmutableArray<CliValueKind> entry)
    {
        if (branch is CilOperation.BranchIfTrue or CilOperation.BranchIfFalse)
        {
            var whenTrue = branch == CilOperation.BranchIfTrue;
            return entry[^1] switch
            {
                CliValueKind.I4 => new([], whenTrue, 1),
                // A wider value is compared with zero, which is true when brfalse would branch.
                CliValueKind.I8 => new(
                    [(CilOperation.LoadInt64, new CilOperand.ConstantI8(0)), (CilOperation.CompareEqual, new CilOperand.None())],
                    !whenTrue, 1),
                CliValueKind.NativeInt => new(
                    [(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), (CilOperation.ConvertNativeInt, new CilOperand.None()),
                        (CilOperation.CompareEqual, new CilOperand.None())],
                    !whenTrue, 1),
                _ => null,
            };
        }
        var (left, right) = (entry[^2], entry[^1]);
        // Validated IL compares an int32 or an int64 only with its own kind or (an int32) a native int, and a native
        // int also with a managed address.
        var integers = left is CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt &&
            right is CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt;
        var floats = left is CliValueKind.F4 or CliValueKind.F8 && left == right;
        if (!integers && !floats)
        {
            return null;
        }
        // For floating-point operands the unsigned forms are the unordered ones (ECMA-335 III.3), so a negated
        // ordered comparison is the unordered one and the reverse: bge branches unless a < b or either is NaN.
        var (compare, takenWhenTrue) = branch switch
        {
            CilOperation.BranchIfEqual => (CilOperation.CompareEqual, true),
            CilOperation.BranchIfNotEqual => (CilOperation.CompareEqual, false),
            CilOperation.BranchIfGreaterThanSigned => (CilOperation.CompareGreaterThanSigned, true),
            CilOperation.BranchIfGreaterThanUnsigned => (CilOperation.CompareGreaterThanUnsigned, true),
            CilOperation.BranchIfLessThanSigned => (CilOperation.CompareLessThanSigned, true),
            CilOperation.BranchIfLessThanUnsigned => (CilOperation.CompareLessThanUnsigned, true),
            CilOperation.BranchIfGreaterThanOrEqualSigned =>
                (floats ? CilOperation.CompareLessThanUnsigned : CilOperation.CompareLessThanSigned, false),
            CilOperation.BranchIfGreaterThanOrEqualUnsigned =>
                (floats ? CilOperation.CompareLessThanSigned : CilOperation.CompareLessThanUnsigned, false),
            CilOperation.BranchIfLessThanOrEqualSigned =>
                (floats ? CilOperation.CompareGreaterThanUnsigned : CilOperation.CompareGreaterThanSigned, false),
            // ble.un, the last of the ten.
            _ => (floats ? CilOperation.CompareGreaterThanSigned : CilOperation.CompareGreaterThanUnsigned, false),
        };
        return new([(compare, new CilOperand.None())], takenWhenTrue, 2);
    }

    // Whether [start, end) only computes numbers from locals, arguments and constants and leaves exactly `pushed`
    // values above what it found, consuming none of those. The kind of the last value it computed, or null.
    internal static CliValueKind? PureValue(
        CilMethodBody body,
        ImmutableDictionary<int, ImmutableArray<CliValueKind>> entryStacks,
        int start,
        int end,
        int pushed)
    {
        var depth = 0;
        var kind = CliValueKind.Unknown;
        for (var position = start; position < end; position++)
        {
            var instruction = body.Instructions[position];
            if (!entryStacks.TryGetValue(instruction.Offset, out var entry) ||
                Effect(body, instruction, entry) is not { } effect ||
                depth < effect.Pops)
            {
                return null;
            }
            depth += 1 - effect.Pops;
            kind = effect.Result;
        }
        return depth == pushed && (pushed == 0 || IsSelectable(kind)) ? kind : null;
    }

    internal static bool IsPureValue(
        CilMethodBody body,
        ImmutableDictionary<int, ImmutableArray<CliValueKind>> entryStacks,
        int start,
        int end,
        int pushed) => PureValue(body, entryStacks, start, end, pushed) is not null;

    // What an instruction that only computes a number consumes, and the kind of the one value it produces; null for
    // anything that reads memory, calls, may trap, or produces a reference. Wasm masks a shift count, so a shift
    // never traps; a conversion to an integer must start from one, since a floating-point value may not fit.
    internal static (int Pops, CliValueKind Result)? Effect(
        CilMethodBody body,
        CilInstruction instruction,
        ImmutableArray<CliValueKind> entry)
    {
        // The entry stack holds what the instruction pops.
        CliValueKind Top(int depth) => entry[^(depth + 1)];
        bool Integer(int depth) => Top(depth) is CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt;
        (int, CliValueKind)? Numeric(int pops, CliValueKind result) =>
            Enumerable.Range(0, pops).All(depth => IsSelectable(Top(depth))) && IsSelectable(result) ? (pops, result) : null;
        (int, CliValueKind)? Binary() =>
            Top(0) == Top(1) ? Numeric(2, Top(0)) :
            (Top(0), Top(1)) is (CliValueKind.I4, CliValueKind.NativeInt) or (CliValueKind.NativeInt, CliValueKind.I4)
                ? Numeric(2, CliValueKind.NativeInt)
                : null;
        (int, CliValueKind)? Convert(CliValueKind result) =>
            Integer(0) || result is CliValueKind.F4 or CliValueKind.F8 ? Numeric(1, result) : null;
        return instruction.Operation switch
        {
            CilOperation.LoadLocal when instruction.Operand is CilOperand.Index { Value: var index } &&
                index < body.Locals.Length => Numeric(0, body.Locals[index]),
            CilOperation.LoadArgument when instruction.Operand is CilOperand.Index { Value: var index } &&
                ArgumentKind(body, index) is { } argument => Numeric(0, argument),
            CilOperation.LoadInt32 => (0, CliValueKind.I4),
            CilOperation.LoadInt64 => (0, CliValueKind.I8),
            CilOperation.LoadFloat32 => (0, CliValueKind.F4),
            CilOperation.LoadFloat64 => (0, CliValueKind.F8),
            CilOperation.Add or CilOperation.Subtract or CilOperation.Multiply => Binary(),
            CilOperation.BitwiseAnd or CilOperation.BitwiseOr or CilOperation.BitwiseXor when Integer(0) && Integer(1) => Binary(),
            CilOperation.ShiftLeft or CilOperation.ShiftRightSigned or CilOperation.ShiftRightUnsigned when
                Integer(1) && Top(0) is CliValueKind.I4 or CliValueKind.NativeInt => Numeric(2, Top(1)),
            CilOperation.Negate => Numeric(1, Top(0)),
            CilOperation.OnesComplement when Integer(0) => Numeric(1, Top(0)),
            CilOperation.CompareEqual or CilOperation.CompareGreaterThanSigned or CilOperation.CompareGreaterThanUnsigned or
                CilOperation.CompareLessThanSigned or CilOperation.CompareLessThanUnsigned when
                Binary() is not null => Numeric(2, CliValueKind.I4),
            CilOperation.Select when Top(0) == CliValueKind.I4 && Top(1) == Top(2) => Numeric(3, Top(1)),
            CilOperation.ConvertInt32 or CilOperation.ConvertInt32Unsigned => Convert(CliValueKind.I4),
            CilOperation.ConvertInt64 or CilOperation.ConvertInt64Unsigned => Convert(CliValueKind.I8),
            CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt => Convert(CliValueKind.NativeInt),
            CilOperation.ConvertFloat32 => Convert(CliValueKind.F4),
            CilOperation.ConvertFloat64 or CilOperation.ConvertFloatUnsigned => Convert(CliValueKind.F8),
            CilOperation.ConvertNumeric when instruction.Operand is CilOperand.NumericConversion { Checked: false } conversion =>
                Convert(conversion.Native ? CliValueKind.NativeInt : conversion.BitWidth == 64 ? CliValueKind.I8 : CliValueKind.I4),
            _ => null,
        };
    }

    internal static CliValueKind? ArgumentKind(CilMethodBody body, int index)
    {
        var signature = body.MethodInstance?.Signature ?? body.Method.Signature;
        var parameter = body.Method.IsStatic ? index : index - 1;
        return parameter >= 0 && parameter < signature.ParameterTypes.Length ? signature.ParameterTypes[parameter] : null;
    }

    internal static Dictionary<int, int> Indices(CilMethodBody body)
    {
        var indices = new Dictionary<int, int>();
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            indices.Add(body.Instructions[index].Offset, index);
        }
        return indices;
    }

    // Whether control can enter [start, end) other than by falling into its first instruction.
    internal static bool Entered(ImmutableArray<CilInstruction> instructions, Dictionary<int, int> entries, int start, int end)
    {
        for (var index = start; index < end; index++)
        {
            if (entries.GetValueOrDefault(instructions[index].Offset) != 0)
            {
                return true;
            }
        }
        return false;
    }

    // The branches into each offset; an exception region boundary counts twice, so no span may contain one.
    internal static Dictionary<int, int> EntryCounts(CilMethodBody body)
    {
        var entries = new Dictionary<int, int>();
        void Add(int offset, int count = 1) => entries[offset] = entries.GetValueOrDefault(offset) + count;
        foreach (var instruction in body.Instructions)
        {
            if (instruction.Operand is CilOperand.BranchTarget target)
            {
                Add(target.Offset);
            }
        }
        foreach (var region in body.ExceptionRegions)
        {
            foreach (var offset in new[] { region.TryOffset, region.TryOffset + region.TryLength,
                region.HandlerOffset, region.HandlerOffset + region.HandlerLength })
            {
                Add(offset, 2);
            }
            if (region.FilterOffset is { } filter)
            {
                Add(filter, 2);
            }
        }
        return entries;
    }

    internal static CilInstruction Synthetic(CilInstruction source, CilOperation operation, CilOperand operand) =>
        new(source.Offset, source.NextOffset, operation, operand) { OriginalOffset = source.SourceOffset };

    // Each span's instructions become its replacement; every instruction is renumbered by its position, and branches,
    // exception regions and source offsets keep pointing at what they pointed at. A branch to an offset inside a span
    // lands on the replacement's copy of that instruction.
    internal static CilMethodBody Splice(CilMethodBody body, List<Span> spans)
    {
        var instructions = body.Instructions;
        var starts = new Dictionary<int, int>();
        var rewritten = new List<CilInstruction>();
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        var conditionLocal = body.Locals.Length;
        var next = 0;
        foreach (var span in spans)
        {
            for (; next < span.First; next++)
            {
                starts.Add(instructions[next].Offset, rewritten.Count);
                rewritten.Add(instructions[next]);
            }
            starts.Add(instructions[span.First].Offset, rewritten.Count);
            var local = locals.Count;
            if (span.UsesConditionLocal)
            {
                locals.Add(CliValueKind.I4);
                localTypes.Add(CliTypeIdentity.Primitive("i4", CliValueKind.I4));
            }
            foreach (var instruction in span.Replacement)
            {
                // A replacement may branch to an instruction it copies, which keeps its offset.
                starts.TryAdd(instruction.Offset, rewritten.Count);
                rewritten.Add(span.UsesConditionLocal &&
                    instruction is { Operation: CilOperation.StoreLocal or CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var index } } &&
                    index == conditionLocal
                        ? instruction with { Operand = new CilOperand.Index(local) }
                        : instruction);
            }
            next = span.Last + 1;
        }
        for (; next < instructions.Length; next++)
        {
            starts.Add(instructions[next].Offset, rewritten.Count);
            rewritten.Add(instructions[next]);
        }
        starts.Add(instructions[^1].NextOffset, rewritten.Count);
        int Start(int offset) => starts[offset];
        return body with
        {
            Instructions = [.. rewritten.Select((instruction, index) => instruction with
            {
                Offset = index,
                NextOffset = index + 1,
                OriginalOffset = instruction.SourceOffset,
                Operand = instruction.Operand is CilOperand.BranchTarget target
                    ? new CilOperand.BranchTarget(Start(target.Offset))
                    : instruction.Operand,
            })],
            Locals = locals.ToImmutable(),
            LocalSignatureTypes = localTypes.ToImmutable(),
            // Two values and a condition, or two conditions, can sit above what the span found on the stack.
            MaxStack = checked(body.MaxStack + 2),
            ExceptionRegions = [.. body.ExceptionRegions.Select(region => region with
            {
                TryOffset = Start(region.TryOffset),
                TryLength = Start(region.TryOffset + region.TryLength) - Start(region.TryOffset),
                HandlerOffset = Start(region.HandlerOffset),
                HandlerLength = Start(region.HandlerOffset + region.HandlerLength) - Start(region.HandlerOffset),
                FilterOffset = region.FilterOffset is { } filter ? Start(filter) : null,
            })],
        };
    }
}
