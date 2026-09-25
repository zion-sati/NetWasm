// Managed port of Sun fdlibm e_acosh.c. Upstream: biosbits/fdlibm
// commit dfd9eaed985332a1c3af98c2bab4004eea217d3d.
// Adaptations: C# bit access, immutable local tables and bounded stack scratch.
/* @(#)e_acosh.c 1.3 95/01/18 */
/*
 * ====================================================
 * Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved.
 *
 * Developed at SunSoft, a Sun Microsystems, Inc. business.
 * Permission to use, copy, modify, and distribute this
 * software is freely granted, provided that this notice
 * is preserved.
 * ====================================================
 *
 */

/* __ieee754_acosh(x)
 * Method :
 *    Based on
 *        acosh(x) = log [ x + sqrt(x*x-1) ]
 *    we have
 *        acosh(x) := log(x)+ln2,    if x is large; else
 *        acosh(x) := log(2x-1/(sqrt(x*x-1)+x)) if x>2; else
 *        acosh(x) := log1p(t+sqrt(2.0*t+t*t)); where t=x-1.
 *
 * Special cases:
 *    acosh(x) is NaN with signal if x<1.
 *    acosh(NaN) is NaN without signal.
 */


/* 0x3FE62E42, 0xFEFA39EF */

namespace System;

public static partial class Math
{
    public static double Acosh(double value)
    {
        const double one = 1.0;
        const double ln2 = 6.93147180559945286227e-01;

        var t = 0.0;
        var hx = 0;
        hx = FdHigh(value);
        if (hx < 0x3ff00000)
        {       /* value < 1 */
            return (value - value) / (value - value);
        }
        else if (hx >= 0x41b00000)
        {   /* value > 2**28 */
            if (hx >= 0x7ff00000)
            {   /* value is inf of NaN */
                return value + value;
            }
            else
                return Log(value) + ln2;    /* acosh(huge)=log(2x) */
        }
        else if (((hx - 0x3ff00000) | FdLow(value)) == 0)
        {
            return 0.0;         /* acosh(1) = 0 */
        }
        else if (hx > 0x40000000)
        {   /* 2**28 > value > 2 */
            t = value * value;
            return Log(2.0 * value - one / (value + Sqrt(t - one)));
        }
        else
        {           /* 1<value<2 */
            t = value - one;
            return FdLog1P(t + Sqrt(2.0 * t + t * t));
        }
    }
}
