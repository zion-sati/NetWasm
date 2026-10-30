// Managed port of Sun fdlibm s_sin.c. Upstream: biosbits/fdlibm
// commit dfd9eaed985332a1c3af98c2bab4004eea217d3d.
// Adaptations: C# bit access, and huge arguments reduced in a separate method.
/* @(#)s_sin.c 1.3 95/01/18 */
/*
 * ====================================================
 * Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved.
 *
 * Developed at SunSoft, a Sun Microsystems, Inc. business.
 * Permission to use, copy, modify, and distribute this
 * software is freely granted, provided that this notice
 * is preserved.
 * ====================================================
 */

/* sin(x)
 * Return sine function of x.
 *
 * kernel function:
 *    __kernel_sin        ... sine function on [-pi/4,pi/4]
 *    __kernel_cos        ... cose function on [-pi/4,pi/4]
 *    __ieee754_rem_pio2    ... argument reduction routine
 *
 * Method.
 *      Let S,C and T denote the sin, cos and tan respectively on
 *    [-PI/4, +PI/4]. Reduce the argument x to y1+y2 = x-k*pi/2
 *    in [-pi/4 , +pi/4], and let n = k mod 4.
 *    We have
 *
 *          n        sin(x)      cos(x)        tan(x)
 *     ----------------------------------------------------------
 *        0           S       C         T
 *        1           C      -S        -1/T
 *        2          -S      -C         T
 *        3          -C       S        -1/T
 *     ----------------------------------------------------------
 *
 * Special cases:
 *      Let trig be any of sin, cos, or tan.
 *      trig(+-INF)  is NaN, with signals;
 *      trig(NaN)    is that NaN;
 *
 * Accuracy:
 *    TRIG(x) returns trig(x) nearly rounded
 */

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace System;

public static partial class Math
{
    public static double Sin(double value)
    {

        var z = 0.0;
        var ix = 0;

        /* High word of value. */
        ix = FdHigh(value);

        /* |value| ~< pi/4 */
        ix &= 0x7fffffff;
        if (ix <= 0x3fe921fb) return FdKernelSin(value, z, 0);

        /* Sin(Inf or NaN) is NaN */
        else if (ix >= 0x7ff00000) return value - value;

        /* argument reduction needed */
        else if (ix > 0x413921fb) return SinLarge(value);
        else return FdTrigReduced(value, 0).ToScalar();
    }

    // musl's sincos: one argument reduction for both results, each exactly what
    // Sin and Cos return. NetWasm: the pair comes back as one v128 value, so
    // neither the call nor the tuple needs a value frame.
    public static (double Sin, double Cos) SinCos(double value)
    {
        var both = SinCosPair(value);
        return (both.ToScalar(), both.GetElement(1));
    }

    private static Vector128<double> SinCosPair(double value)
    {
        var ix = FdHigh(value) & 0x7fffffff;

        /* |x| ~< pi/4 */
        if (ix <= 0x3fe921fb) return Vector128.Create(FdKernelSin(value, 0.0, 0), FdKernelCos(value, 0.0));

        /* sincos(Inf or NaN) is NaN */
        if (ix >= 0x7ff00000) return Vector128.Create(value - value);

        /* argument reduction needed */
        if (ix > 0x413921fb) return SinCosLarge(value);
        return FdTrigReduced(value, 3);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Vector128<double> SinCosLarge(double value)
    {
        var hx = FdHigh(value);
        var n = FdRemPio2Large(value, hx & 0x7fffffff, hx, out var y0, out var y1);
        return FdSinCosQuadrant(n, y0, y1);
    }

    // The huge-argument paths, |x| > 2^19*(pi/2), apart so that their reduction
    // scratch and out parameters stay out of the common paths.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double SinLarge(double value)
    {
        var hx = FdHigh(value);
        var n = FdRemPio2Large(value, hx & 0x7fffffff, hx, out var y0, out var y1);
        switch (n & 3)
        {
            case 0: return FdKernelSin(y0, y1, 1);
            case 1: return FdKernelCos(y0, y1);
            case 2: return -FdKernelSin(y0, y1, 1);
            default:
                return -FdKernelCos(y0, y1);
        }
    }
}
