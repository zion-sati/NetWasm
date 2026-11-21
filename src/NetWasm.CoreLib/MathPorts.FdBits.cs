// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT
// Restored for 0.15.1 as MathPorts (NetWasm deviation, speed): Math calls these managed ports, which the
// compiler can inline, instead of the C library's functions. Each is bit-identical to musl's on the inputs its
// caller documents; see that caller's comment.

using static System.Math;

namespace System;

internal static partial class MathPorts
{
    // Endianness-independent equivalents of fdlibm's word-access macros.
    private static int FdHigh(double value) => (int)(BitConverter.DoubleToUInt64Bits(value) >> 32);
    private static int FdLow(double value) => (int)BitConverter.DoubleToUInt64Bits(value);
    private static double FdWithHigh(double value, int high) =>
        BitConverter.UInt64BitsToDouble(((ulong)(uint)high << 32) |
            (BitConverter.DoubleToUInt64Bits(value) & 0xFFFFFFFFUL));
    private static double FdWithLow(double value, int low) =>
        BitConverter.UInt64BitsToDouble((BitConverter.DoubleToUInt64Bits(value) & 0xFFFFFFFF00000000UL) |
            (uint)low);
}
