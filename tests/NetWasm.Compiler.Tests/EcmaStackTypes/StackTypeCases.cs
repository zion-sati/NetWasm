// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Emit;

namespace NetWasm.Compiler.Tests.EcmaStackTypes;

using static StackKind;

// The operand pairs ECMA-335 Partition III 1.5's tables allow, as cases: binary numeric operations (Table III.2),
// unary numeric (III.3), comparisons (III.4, as ceq/cgt/clt and as two-operand branches), integer operations (III.5),
// shifts (III.6), overflow arithmetic (III.7), conversions (III.8) and switch (III.3.66). F is either float, and the
// two mix.
internal static class StackTypeCases
{
    public static readonly string[] Families = ["binary", "unary", "integer", "shift", "overflow", "compare", "branch", "convert", "switch"];

    // int32 and native int mix to native int; int64 only with itself; F with F.
    private static readonly (StackKind Left, StackKind Right, StackKind Result)[] Numeric =
    [
        (Int32, Int32, Int32), (Int32, NativeInt, NativeInt), (NativeInt, Int32, NativeInt), (NativeInt, NativeInt, NativeInt),
        (Int64, Int64, Int64),
    ];

    private static readonly (StackKind Left, StackKind Right, StackKind Result)[] Floating =
    [
        (Float32, Float32, Float32), (Float64, Float64, Float64), (Float32, Float64, Float64), (Float64, Float32, Float64),
    ];

    // Comparisons also take an address with an address or a native int.
    private static readonly (StackKind Left, StackKind Right)[] Comparable =
    [
        .. Numeric.Select(static pair => (pair.Left, pair.Right)),
        .. Floating.Select(static pair => (pair.Left, pair.Right)),
        (Address, Address), (Address, NativeInt), (NativeInt, Address),
    ];

    public static IReadOnlyList<StackTypeCase> Of(string family) => family switch
    {
        "binary" => Binary().ToList(),
        "unary" => Unary().ToList(),
        "integer" => Integer().ToList(),
        "shift" => Shift().ToList(),
        "overflow" => Overflow().ToList(),
        "compare" => Compare().ToList(),
        "branch" => Branch().ToList(),
        "convert" => Convert().ToList(),
        "switch" => Switch().ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    private static IEnumerable<StackTypeCase> Binary()
    {
        foreach (var operation in new[] { OpCodes.Add, OpCodes.Sub, OpCodes.Mul, OpCodes.Div, OpCodes.Rem })
        {
            foreach (var (left, right, result) in Numeric.Concat(Floating))
            {
                yield return new("binary", operation, [left, right], result);
            }
        }
        foreach (var (left, right) in new[] { (Int32, Address), (NativeInt, Address), (Address, Int32), (Address, NativeInt) })
        {
            yield return new("binary", OpCodes.Add, [left, right], Address);
        }
        yield return new("binary", OpCodes.Sub, [Address, Int32], Address);
        yield return new("binary", OpCodes.Sub, [Address, NativeInt], Address);
        yield return new("binary", OpCodes.Sub, [Address, Address], NativeInt);
    }

    private static IEnumerable<StackTypeCase> Unary()
    {
        foreach (var kind in new[] { Int32, Int64, NativeInt, Float32, Float64 })
        {
            yield return new("unary", OpCodes.Neg, [kind], kind);
        }
        foreach (var kind in new[] { Int32, Int64, NativeInt })
        {
            yield return new("unary", OpCodes.Not, [kind], kind);
        }
    }

    private static IEnumerable<StackTypeCase> Integer()
    {
        foreach (var operation in new[] { OpCodes.And, OpCodes.Or, OpCodes.Xor, OpCodes.Div_Un, OpCodes.Rem_Un })
        {
            foreach (var (left, right, result) in Numeric)
            {
                var unsigned = operation == OpCodes.Div_Un || operation == OpCodes.Rem_Un;
                yield return new("integer", operation, [left, right], result,
                    unsigned && result == NativeInt ? InputDomain.PointerSizeDependent : InputDomain.All);
            }
        }
    }

    private static IEnumerable<StackTypeCase> Shift()
    {
        foreach (var operation in new[] { OpCodes.Shl, OpCodes.Shr, OpCodes.Shr_Un })
        {
            foreach (var value in new[] { Int32, Int64, NativeInt })
            {
                foreach (var amount in new[] { Int32, NativeInt })
                {
                    yield return new("shift", operation, [value, amount], value,
                        operation == OpCodes.Shr_Un && value == NativeInt ? InputDomain.PointerSizeDependent : InputDomain.All);
                }
            }
        }
    }

    private static IEnumerable<StackTypeCase> Overflow()
    {
        foreach (var operation in new[] { OpCodes.Add_Ovf, OpCodes.Add_Ovf_Un, OpCodes.Sub_Ovf, OpCodes.Sub_Ovf_Un, OpCodes.Mul_Ovf, OpCodes.Mul_Ovf_Un })
        {
            var unsigned = operation.Name!.EndsWith(".un", StringComparison.Ordinal);
            foreach (var (left, right, result) in Numeric)
            {
                yield return new("overflow", operation, [left, right], result,
                    unsigned && result == NativeInt ? InputDomain.PointerSizeDependent : InputDomain.All);
            }
        }
        foreach (var (left, right) in new[] { (Int32, Address), (NativeInt, Address), (Address, Int32), (Address, NativeInt) })
        {
            yield return new("overflow", OpCodes.Add_Ovf_Un, [left, right], Address);
        }
        yield return new("overflow", OpCodes.Sub_Ovf_Un, [Address, Int32], Address);
        yield return new("overflow", OpCodes.Sub_Ovf_Un, [Address, NativeInt], Address);
        yield return new("overflow", OpCodes.Sub_Ovf_Un, [Address, Address], NativeInt);
    }

    private static IEnumerable<StackTypeCase> Compare()
    {
        foreach (var operation in new[] { OpCodes.Ceq, OpCodes.Cgt, OpCodes.Cgt_Un, OpCodes.Clt, OpCodes.Clt_Un })
        {
            foreach (var (left, right) in Comparable)
            {
                yield return new("compare", operation, [left, right], Int32);
            }
        }
        // Object references compare for equality, and cgt.un tests one against null.
        yield return new("compare", OpCodes.Ceq, [StackKind.Object, StackKind.Object], Int32);
        yield return new("compare", OpCodes.Cgt_Un, [StackKind.Object, StackKind.Object], Int32);
    }

    private static IEnumerable<StackTypeCase> Branch()
    {
        foreach (var operation in new[]
        {
            OpCodes.Beq, OpCodes.Bne_Un, OpCodes.Bge, OpCodes.Bge_Un, OpCodes.Bgt, OpCodes.Bgt_Un,
            OpCodes.Ble, OpCodes.Ble_Un, OpCodes.Blt, OpCodes.Blt_Un,
        })
        {
            foreach (var (left, right) in Comparable)
            {
                yield return new("branch", operation, [left, right], Int32);
            }
        }
        yield return new("branch", OpCodes.Beq, [StackKind.Object, StackKind.Object], Int32);
        yield return new("branch", OpCodes.Bne_Un, [StackKind.Object, StackKind.Object], Int32);
    }

    // switch takes an int32 or a native int (Partition III 3.66); a native int with bits above the low 32 (an int64
    // converted to one) exists only where native ints are 64-bit.
    private static IEnumerable<StackTypeCase> Switch()
    {
        yield return new("switch", OpCodes.Switch, [Int32], Int32);
        yield return new("switch", OpCodes.Switch, [NativeInt], Int32);
        yield return new("switch", OpCodes.Switch, [Int64], Int32, InputDomain.SixtyFourBitOnly);
    }

    private static IEnumerable<StackTypeCase> Convert()
    {
        var targets = new (OpCode Operation, StackKind Result)[]
        {
            (OpCodes.Conv_I1, Int32), (OpCodes.Conv_I2, Int32), (OpCodes.Conv_I4, Int32), (OpCodes.Conv_I8, Int64),
            (OpCodes.Conv_U1, Int32), (OpCodes.Conv_U2, Int32), (OpCodes.Conv_U4, Int32), (OpCodes.Conv_U8, Int64),
            (OpCodes.Conv_I, NativeInt), (OpCodes.Conv_U, NativeInt),
            (OpCodes.Conv_R4, Float32), (OpCodes.Conv_R8, Float64), (OpCodes.Conv_R_Un, Float64),
            (OpCodes.Conv_Ovf_I1, Int32), (OpCodes.Conv_Ovf_I2, Int32), (OpCodes.Conv_Ovf_I4, Int32), (OpCodes.Conv_Ovf_I8, Int64),
            (OpCodes.Conv_Ovf_U1, Int32), (OpCodes.Conv_Ovf_U2, Int32), (OpCodes.Conv_Ovf_U4, Int32), (OpCodes.Conv_Ovf_U8, Int64),
            (OpCodes.Conv_Ovf_I, NativeInt), (OpCodes.Conv_Ovf_U, NativeInt),
            (OpCodes.Conv_Ovf_I1_Un, Int32), (OpCodes.Conv_Ovf_I2_Un, Int32), (OpCodes.Conv_Ovf_I4_Un, Int32), (OpCodes.Conv_Ovf_I8_Un, Int64),
            (OpCodes.Conv_Ovf_U1_Un, Int32), (OpCodes.Conv_Ovf_U2_Un, Int32), (OpCodes.Conv_Ovf_U4_Un, Int32), (OpCodes.Conv_Ovf_U8_Un, Int64),
            (OpCodes.Conv_Ovf_I_Un, NativeInt), (OpCodes.Conv_Ovf_U_Un, NativeInt),
        };
        foreach (var (operation, result) in targets)
        {
            var name = operation.Name!;
            var unsigned = name.EndsWith(".un", StringComparison.Ordinal);
            var toNative = result == NativeInt;
            foreach (var source in new[] { Int32, Int64, NativeInt, Float32, Float64 })
            {
                var floating = source is Float32 or Float64;
                // conv.r.un and the .un overflow checks read an integer as unsigned.
                if (floating && unsigned)
                {
                    continue;
                }
                var domain = InputDomain.All;
                if (floating && !name.Contains("ovf", StringComparison.Ordinal) &&
                    (name.StartsWith("conv.u", StringComparison.Ordinal)))
                {
                    domain = InputDomain.NotNegative;
                }
                else if (source == Int64 && toNative)
                {
                    domain = InputDomain.SixtyFourBitOnly;
                }
                else if (source == NativeInt && (unsigned || name is "conv.u8" or "conv.u") ||
                    source == Int32 && name is "conv.u" or "conv.ovf.u.un" or "conv.ovf.i.un")
                {
                    domain = InputDomain.PointerSizeDependent;
                }
                yield return new("convert", operation, [source], result, domain);
            }
        }
        // An address converts to a native or 64-bit integer.
        foreach (var (operation, result) in new[] { (OpCodes.Conv_I, NativeInt), (OpCodes.Conv_U, NativeInt), (OpCodes.Conv_I8, Int64), (OpCodes.Conv_U8, Int64) })
        {
            yield return new("convert", operation, [Address], result);
        }
    }
}
