// Managed port of Sun fdlibm s_tan.c. Upstream: biosbits/fdlibm
// commit dfd9eaed985332a1c3af98c2bab4004eea217d3d.
// Adaptations: C# bit access, the reduced argument returned as a value tuple, and
// huge arguments reduced in a separate method.
/* @(#)s_tan.c 1.3 95/01/18 */
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

/* tan(x)
 * Return tangent function of x.
 *
 * kernel function:
 *    __kernel_tan        ... tangent function on [-pi/4,pi/4]
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
    public static double Tan(double value)
    {

        var z = 0.0;
        var ix = 0;

        /* High word of value. */
        ix = FdHigh(value);

        /* |value| ~< pi/4 */
        ix &= 0x7fffffff;
        if (ix <= 0x3fe921fb) return FdKernelTan(value, z, 1);

        /* Tan(Inf or NaN) is NaN */
        else if (ix >= 0x7ff00000) return value - value;        /* NaN */

        /* argument reduction needed */
        else if (ix > 0x413921fb) return TanLarge(value);
        else return FdTrigReduced(value, 2).ToScalar();
    }

    // The huge-argument path, |x| > 2^19*(pi/2), apart so that its reduction
    // scratch and out parameters stay out of the common path.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double TanLarge(double value)
    {
        var hx = FdHigh(value);
        var n = FdRemPio2Large(value, hx & 0x7fffffff, hx, out var y0, out var y1);
        return FdKernelTan(y0, y1, 1 - ((n & 1) << 1));
    }
}
