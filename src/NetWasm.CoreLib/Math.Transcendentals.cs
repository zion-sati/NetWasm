// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.

namespace System;

public static partial class Math
{
    public static Int128 BigMul(long left, long right) =>
        (Int128)left * (Int128)right;

    public static UInt128 BigMul(ulong left, ulong right) =>
        (UInt128)left * (UInt128)right;
}
