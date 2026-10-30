// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 474ed963699fc29809015392738a15231aaae2de.

namespace System;

public static partial class Math
{
    public static Int128 BigMul(long left, long right) =>
        (Int128)left * (Int128)right;

    public static UInt128 BigMul(ulong left, ulong right) =>
        (UInt128)left * (UInt128)right;
}
