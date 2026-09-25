// Managed port of Sun fdlibm e_log.c. Upstream: biosbits/fdlibm
// commit dfd9eaed985332a1c3af98c2bab4004eea217d3d.
// Adaptations: C# bit access, immutable local tables and bounded stack scratch.
/* @(#)e_log.c 1.3 95/01/18 */
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

/* __ieee754_log(x)
 * Return the logrithm of x
 *
 * Method :
 *   1. Argument Reduction: find k and f such that
 *            x = 2^k * (1+f),
 *       where  sqrt(2)/2 < 1+f < sqrt(2) .
 *
 *   2. Approximation of log(1+f).
 *    Let s = f/(2+f) ; based on log(1+f) = log(1+s) - log(1-s)
 *         = 2s + 2/3 s**3 + 2/5 s**5 + .....,
 *              = 2s + s*R
 *      We use a special Reme algorithm on [0,0.1716] to generate
 *     a polynomial of degree 14 to approximate R The maximum error
 *    of this polynomial approximation is bounded by 2**-58.45. In
 *    other words,
 *                2      4      6      8      10      12      14
 *        R(z) ~ Lg1*s +Lg2*s +Lg3*s +Lg4*s +Lg5*s  +Lg6*s  +Lg7*s
 *      (the values of Lg1 to Lg7 are listed in the program)
 *    and
 *        |      2          14          |     -58.45
 *        | Lg1*s +...+Lg7*s    -  R(z) | <= 2
 *        |                             |
 *    Note that 2s = f - s*f = f - hfsq + s*hfsq, where hfsq = f*f/2.
 *    In order to guarantee error in log below 1ulp, we compute log
 *    by
 *        log(1+f) = f - s*(f - R)    (if f is not too large)
 *        log(1+f) = f - (hfsq - s*(hfsq+R)).    (better accuracy)
 *
 *    3. Finally,  log(x) = k*ln2 + log(1+f).
 *                = k*ln2_hi+(f-(hfsq-(s*(hfsq+R)+k*ln2_lo)))
 *       Here ln2 is split into two floating point number:
 *            ln2_hi + ln2_lo,
 *       where n*ln2_hi is always exact for |n| < 2000.
 *
 * Special cases:
 *    log(x) is NaN with signal if x < 0 (including -INF) ;
 *    log(+INF) is +INF; log(0) is -INF with signal;
 *    log(NaN) is that NaN with no signal.
 *
 * Accuracy:
 *    according to an error analysis, the error is always less than
 *    1 ulp (unit in the last place).
 *
 * Constants:
 * The hexadecimal values are the intended ones for the following
 * constants. The decimal values may be used, provided that the
 * compiler will convert from decimal to binary accurately enough
 * to produce the hexadecimal values shown.
 */


/* 3FC2F112 DF3E5244 */

namespace System;

public static partial class Math
{
    public static double Log(double value)
    {
        const double ln2_hi = 6.93147180369123816490e-01;
        const double ln2_lo = 1.90821492927058770002e-10;
        const double two54 = 1.80143985094819840000e+16;
        const double Lg1 = 6.666666666666735130e-01;
        const double Lg2 = 3.999999999940941908e-01;
        const double Lg3 = 2.857142874366239149e-01;
        const double Lg4 = 2.222219843214978396e-01;
        const double Lg5 = 1.818357216161805012e-01;
        const double Lg6 = 1.531383769920937332e-01;
        const double Lg7 = 1.479819860511658591e-01;
        const double zero = 0.0;

        var hfsq = 0.0;
        var f = 0.0;
        var s = 0.0;
        var z = 0.0;
        var R = 0.0;
        var w = 0.0;
        var t1 = 0.0;
        var t2 = 0.0;
        var dk = 0.0;
        var k = 0;
        var hx = 0;
        var i = 0;
        var j = 0;
        var lx = 0U;

        hx = FdHigh(value);     /* high word of value */
        lx = (uint)FdLow(value);        /* low  word of value */

        k = 0;
        if (hx < 0x00100000)
        {           /* value < 2**-1022  */
            if (((uint)(hx & 0x7fffffff) | lx) == 0)
                return -two54 / zero;       /* log(+-0)=-inf */
            if (hx < 0) return (value - value) / zero;  /* log(-#) = NaN */
            k -= 54; value *= two54; /* subnormal number, scale up value */
            hx = FdHigh(value);     /* high word of value */
        }
        if (hx >= 0x7ff00000) return value + value;
        k += (hx >> 20) - 1023;
        hx &= 0x000fffff;
        i = (hx + 0x95f64) & 0x100000;
        value = FdWithHigh(value, hx | (i ^ 0x3ff00000));   /* normalize value or value/2 */
        k += (i >> 20);
        f = value - 1.0;
        if ((0x000fffff & (2 + hx)) < 3)
        {   /* |f| < 2**-20 */
            if (f == zero) if (k == 0) return zero;
            else
            {
                dk = (double)k;
                return dk * ln2_hi + dk * ln2_lo;
            }
            R = f * f * (0.5 - 0.33333333333333333 * f);
            if (k == 0) return f - R;
            else
            {
                dk = (double)k;
                return dk * ln2_hi - ((R - dk * ln2_lo) - f);
            }
        }
        s = f / (2.0 + f);
        dk = (double)k;
        z = s * s;
        i = hx - 0x6147a;
        w = z * z;
        j = 0x6b851 - hx;
        t1 = w * (Lg2 + w * (Lg4 + w * Lg6));
        t2 = z * (Lg1 + w * (Lg3 + w * (Lg5 + w * Lg7)));
        i |= j;
        R = t2 + t1;
        if (i > 0)
        {
            hfsq = 0.5 * f * f;
            if (k == 0) return f - (hfsq - s * (hfsq + R));
            else
                return dk * ln2_hi - ((hfsq - (s * (hfsq + R) + dk * ln2_lo)) - f);
        }
        else
        {
            if (k == 0) return f - s * (f - R);
            else
                return dk * ln2_hi - ((s * (f - R) - dk * ln2_lo) - f);
        }
    }
}
