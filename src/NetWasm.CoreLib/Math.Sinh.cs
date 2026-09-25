// Managed port of Sun fdlibm e_sinh.c. Upstream: biosbits/fdlibm
// commit dfd9eaed985332a1c3af98c2bab4004eea217d3d.
// Adaptations: C# bit access, immutable local tables and bounded stack scratch.
/* @(#)e_sinh.c 1.3 95/01/18 */
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

/* __ieee754_sinh(x)
 * Method :
 * mathematically sinh(x) if defined to be (exp(x)-exp(-x))/2
 *    1. Replace x by |x| (sinh(-x) = -sinh(x)).
 *    2.
 *                                            E + E/(E+1)
 *        0        <= x <= 22     :  sinh(x) := --------------, E=expm1(x)
 *                                       2
 *
 *        22       <= x <= lnovft :  sinh(x) := exp(x)/2
 *        lnovft   <= x <= ln2ovft:  sinh(x) := exp(x/2)/2 * exp(x/2)
 *        ln2ovft  <  x        :  sinh(x) := x*shuge (overflow)
 *
 * Special cases:
 *    sinh(x) is |x| if x is +INF, -INF, or NaN.
 *    only sinh(0)=0 is exact for finite x.
 */

namespace System;

public static partial class Math
{
    public static double Sinh(double value)
    {
        const double one = 1.0;
        const double shuge = 1.0e307;

        var t = 0.0;
        var w = 0.0;
        var h = 0.0;
        var ix = 0;
        var jx = 0;
        var lx = 0U;

        /* High word of |value|. */
        jx = FdHigh(value);
        ix = jx & 0x7fffffff;

        /* value is INF or NaN */
        if (ix >= 0x7ff00000) return value + value;

        h = 0.5;
        if (jx < 0) h = -h;
        /* |value| in [0,22], return sign(value)*0.5*(E+E/(E+1))) */
        if (ix < 0x40360000)
        {       /* |value|<22 */
            if (ix < 0x3e300000)        /* |value|<2**-28 */
                if (shuge + value > one) return value;/* sinh(tiny) = tiny with inexact */
            t = FdExpm1(Abs(value));
            if (ix < 0x3ff00000) return h * (2.0 * t - t * t / (t + one));
            return h * (t + t / (t + one));
        }

        /* |value| in [22, log(maxdouble)] return 0.5*exp(|value|) */
        if (ix < 0x40862E42) return h * Exp(Abs(value));

        /* |value| in [log(maxdouble), overflowthresold] */
        lx = (uint)FdLow(value);
        if (ix < 0x408633CE || (ix == 0x408633ce) && (lx <= (uint)0x8fb9f87d))
        {
            w = Exp(0.5 * Abs(value));
            t = h * w;
            return t * w;
        }

        /* |value| > overflowthresold, sinh(value) overflow */
        return value * shuge;
    }
}
