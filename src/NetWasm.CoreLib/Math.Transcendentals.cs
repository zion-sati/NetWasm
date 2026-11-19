// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Ported from dotnet/runtime System.Private.CoreLib; upstream commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Runtime.CompilerServices;

namespace System;

public static partial class Math
{
    public static Int128 BigMul(long left, long right) =>
        (Int128)left * (Int128)right;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UInt128 BigMul(ulong left, ulong right) =>
        (UInt128)left * (UInt128)right;
}
