// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

// Where a stack value came from: a local plus a constant offset, the local's array length plus an offset, a constant,
// or the sum of two locals, Local and Base, plus an offset, as a span read at `start + index` computes its index. Such
// a sum may also add or subtract a third local, Term, as an image row above is read at `start + index - width`.
internal readonly record struct ValueOrigin(int? Local, int Offset, bool Constant, bool Length = false, int? Base = null,
    int? Term = null, bool TermNegated = false)
{
    public static ValueOrigin Unknown => default;

    public static ValueOrigin FromLocal(int local) => new(local, 0, false);
}

// Tracks the origin of each stack slot through one block's instructions.
internal static class ValueOrigins
{
    // Copies maps a local to the origin of the value last stored in it, as an
    // inlined call's spilled argument copies index + 1, until either local is
    // stored; it is emptied wherever the stack's origins are lost.
    public static void Track(CilInstruction instruction, List<ValueOrigin> origins, Dictionary<int, ValueOrigin> copies)
    {
        switch (instruction.Operation)
        {
            case CilOperation.LoadLocal:
                var loaded = ((CilOperand.Index)instruction.Operand).Value;
                origins.Add(copies.TryGetValue(loaded, out var copied) ? copied : ValueOrigin.FromLocal(loaded));
                return;
            case CilOperation.StoreLocal:
                var value = origins[^1];
                Consume(origins, 1, false);
                var local = ((CilOperand.Index)instruction.Operand).Value;
                for (var i = 0; i < origins.Count; i++)
                    if (origins[i].Local == local || origins[i].Base == local || origins[i].Term == local)
                        origins[i] = ValueOrigin.Unknown;
                foreach (var (copy, origin) in copies.ToArray())
                    if (copy == local || origin.Local == local || origin.Base == local || origin.Term == local)
                        copies.Remove(copy);
                if (value is { Local: { } source, Constant: false, Length: false } && source != local && value.Base != local &&
                    value.Term != local)
                    copies[local] = value;
                return;
            case CilOperation.LoadInt32 when instruction.Operand is CilOperand.ConstantI4 constant:
                origins.Add(new(null, constant.Value, true));
                return;
            case CilOperation.Add or CilOperation.Subtract when origins.Count >= 2 &&
                origins[^2] is { Local: { } addend, Constant: false } left && origins[^1] is { Constant: true } right:
                origins.RemoveRange(origins.Count - 2, 2);
                var sum = instruction.Operation == CilOperation.Add ? (long)left.Offset + right.Offset : (long)left.Offset - right.Offset;
                origins.Add(sum is >= int.MinValue and <= int.MaxValue
                    ? left with { Offset = (int)sum }
                    : ValueOrigin.Unknown);
                return;
            // Two locals' sum, as `start + index`.
            case CilOperation.Add when origins.Count >= 2 &&
                origins[^2] is { Local: { } first, Constant: false, Length: false, Base: null } augend &&
                origins[^1] is { Local: { } second, Constant: false, Length: false, Base: null } addend:
                origins.RemoveRange(origins.Count - 2, 2);
                var offsets = (long)augend.Offset + addend.Offset;
                origins.Add(offsets is >= int.MinValue and <= int.MaxValue
                    ? new(first, (int)offsets, false, Base: second)
                    : ValueOrigin.Unknown);
                return;
            // A sum of two locals plus or less a third, as `start + index - width`.
            case CilOperation.Add or CilOperation.Subtract when origins.Count >= 2 &&
                origins[^2] is { Local: not null, Base: not null, Term: null, Constant: false, Length: false } pair &&
                origins[^1] is { Local: { } term, Offset: 0, Constant: false, Length: false, Base: null, Term: null }:
                origins.RemoveRange(origins.Count - 2, 2);
                origins.Add(pair with { Term = term, TermNegated = instruction.Operation == CilOperation.Subtract });
                return;
            // An array length is at most int.MaxValue, so narrowing it keeps the value.
            case CilOperation.LoadArrayLength when origins.Count >= 1 &&
                origins[^1] is { Local: { } measured, Offset: 0, Constant: false, Length: false, Base: null }:
                origins[^1] = new(measured, 0, false, Length: true);
                return;
            case CilOperation.ConvertInt32 when origins.Count >= 1 && origins[^1] is { Length: true }:
                return;
            case CilOperation.Duplicate:
                origins.Add(origins[^1]);
                return;
            case CilOperation.Nop:
            case CilOperation.Readonly:
            case CilOperation.Volatile:
            case CilOperation.Unaligned:
                return;
            case CilOperation.Negate:
            case CilOperation.OnesComplement:
                Consume(origins, 1, true);
                return;
            case CilOperation.LoadArrayElement:
            case CilOperation.LoadArrayElementReference:
            case CilOperation.LoadArrayElementAddress:
            case >= CilOperation.Add and <= CilOperation.RemainderUnsigned:
            case >= CilOperation.CompareEqual and <= CilOperation.CompareLessThanUnsigned:
                Consume(origins, 2, true);
                return;
            case CilOperation.StoreArrayElement:
            case CilOperation.StoreArrayElementReference:
                Consume(origins, 3, false);
                return;
            case CilOperation.LoadArrayLength:
            case CilOperation.LoadField:
            case CilOperation.LoadFieldAddress:
            case CilOperation.LoadObject:
            case >= CilOperation.ConvertInt32 and <= CilOperation.ConvertNumeric:
                Consume(origins, 1, true);
                return;
            case CilOperation.Pop:
            case CilOperation.StoreArgument:
            case CilOperation.StoreStaticField:
                Consume(origins, 1, false);
                return;
            case CilOperation.StoreField:
            case CilOperation.StoreObject:
                Consume(origins, 2, false);
                return;
            case >= CilOperation.LoadInt32 and <= CilOperation.LoadFieldToken:
            case CilOperation.LoadArgument:
            case CilOperation.LoadLocalAddress:
            case CilOperation.LoadArgumentAddress:
            case CilOperation.LoadStaticField:
            case CilOperation.LoadStaticFieldAddress:
            case CilOperation.SizeOf:
                origins.Add(ValueOrigin.Unknown);
                return;
            default:
                // No cross-call or cross-block expression provenance is inferred.
                origins.Clear();
                copies.Clear();
                return;
        }
    }

    private static void Consume(List<ValueOrigin> origins, int count, bool produces)
    {
        origins.RemoveRange(origins.Count - count, count);
        if (produces) origins.Add(ValueOrigin.Unknown);
    }
}
