// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Core;

// How many values an instruction pops and pushes, for the instructions whose operation alone decides it; null for
// the others, among them calls and newobj, whose effect depends on the method they name.
public static class CilStackEffects
{
    public static (int Pops, int Pushes)? Of(CilInstruction instruction) => instruction.Operation switch
    {
        CilOperation.Nop or CilOperation.Constrained or CilOperation.Readonly or CilOperation.Volatile or CilOperation.Unaligned => (0, 0),
        CilOperation.LoadArgument or CilOperation.LoadArgumentAddress or CilOperation.LoadLocal or CilOperation.LoadLocalAddress or
            CilOperation.LoadInt32 or CilOperation.LoadInt64 or CilOperation.LoadFloat32 or CilOperation.LoadFloat64 or
            CilOperation.LoadNull or CilOperation.LoadString or CilOperation.LoadStaticField or CilOperation.LoadStaticFieldAddress or
            CilOperation.SizeOf => (0, 1),
        CilOperation.StoreArgument or CilOperation.StoreLocal or CilOperation.Pop or CilOperation.StoreStaticField or
            CilOperation.InitializeObject or CilOperation.BranchIfTrue or CilOperation.BranchIfFalse => (1, 0),
        CilOperation.Duplicate => (1, 2),
        CilOperation.LoadField or CilOperation.LoadFieldAddress or CilOperation.LoadObject or CilOperation.Box or
            CilOperation.Unbox or CilOperation.UnboxAny or CilOperation.CastClass or CilOperation.IsInstance or
            CilOperation.LoadArrayLength or CilOperation.NewArray or CilOperation.Negate or CilOperation.OnesComplement or
            CilOperation.ConvertInt32 or CilOperation.ConvertInt32Unsigned or CilOperation.ConvertInt64 or
            CilOperation.ConvertInt64Unsigned or CilOperation.ConvertNativeInt or CilOperation.ConvertNativeUInt or
            CilOperation.ConvertFloat32 or CilOperation.ConvertFloat64 or CilOperation.ConvertFloatUnsigned or
            CilOperation.ConvertNumeric or CilOperation.CheckFinite => (1, 1),
        CilOperation.StoreField or CilOperation.StoreObject => (2, 0),
        CilOperation.Add or CilOperation.Subtract or CilOperation.Multiply or CilOperation.Divide or CilOperation.DivideUnsigned or
            CilOperation.Remainder or CilOperation.RemainderUnsigned or CilOperation.BitwiseAnd or CilOperation.BitwiseOr or
            CilOperation.BitwiseXor or CilOperation.ShiftLeft or CilOperation.ShiftRightSigned or CilOperation.ShiftRightUnsigned or
            CilOperation.AddChecked or CilOperation.AddCheckedUnsigned or CilOperation.SubtractChecked or
            CilOperation.SubtractCheckedUnsigned or CilOperation.MultiplyChecked or CilOperation.MultiplyCheckedUnsigned or
            CilOperation.CompareEqual or CilOperation.CompareGreaterThanSigned or CilOperation.CompareGreaterThanUnsigned or
            CilOperation.CompareLessThanSigned or CilOperation.CompareLessThanUnsigned or CilOperation.LoadArrayElement or
            CilOperation.LoadArrayElementAddress or CilOperation.LoadArrayElementReference => (2, 1),
        CilOperation.StoreArrayElement or CilOperation.StoreArrayElementReference => (3, 0),
        CilOperation.Select => (3, 1),
        _ => null,
    };
}
