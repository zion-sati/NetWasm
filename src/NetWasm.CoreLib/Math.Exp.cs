// Managed port of Sun fdlibm e_exp.c. Upstream: biosbits/fdlibm
// commit dfd9eaed985332a1c3af98c2bab4004eea217d3d.
// Adaptations: C# bit access, immutable local tables and bounded stack scratch.
/* @(#)e_exp.c 1.6 04/04/22 */
/*
 * ====================================================
 * Copyright (C) 2004 by Sun Microsystems, Inc. All rights reserved.
 *
 * Permission to use, copy, modify, and distribute this
 * software is freely granted, provided that this notice
 * is preserved.
 * ====================================================
 */

/* __ieee754_exp(x)
 * Returns the exponential of x.
 *
 * Method
 *   1. Argument reduction:
 *      Reduce x to an r so that |r| <= 0.5*ln2 ~ 0.34658.
 *    Given x, find r and integer k such that
 *
 *               x = k*ln2 + r,  |r| <= 0.5*ln2.
 *
 *      Here r will be represented as r = hi-lo for better
 *    accuracy.
 *
 *   2. Approximation of exp(r) by a special rational function on
 *    the interval [0,0.34658]:
 *    Write
 *        R(r**2) = r*(exp(r)+1)/(exp(r)-1) = 2 + r*r/6 - r**4/360 + ...
 *      We use a special Remes algorithm on [0,0.34658] to generate
 *     a polynomial of degree 5 to approximate R. The maximum error
 *    of this polynomial approximation is bounded by 2**-59. In
 *    other words,
 *        R(z) ~ 2.0 + P1*z + P2*z**2 + P3*z**3 + P4*z**4 + P5*z**5
 *      (where z=r*r, and the values of P1 to P5 are listed below)
 *    and
 *        |                  5          |     -59
 *        | 2.0+P1*z+...+P5*z   -  R(z) | <= 2
 *        |                             |
 *    The computation of exp(r) thus becomes
 *                             2*r
 *        exp(r) = 1 + -------
 *                      R - r
 *                                 r*R1(r)
 *               = 1 + r + ----------- (for better accuracy)
 *                          2 - R1(r)
 *    where
 *                     2       4             10
 *        R1(r) = r - (P1*r  + P2*r  + ... + P5*r   ).
 *
 *   3. Scale back to obtain exp(x):
 *    From step 1, we have
 *       exp(x) = 2^k * exp(r)
 *
 * Special cases:
 *    exp(INF) is INF, exp(NaN) is NaN;
 *    exp(-INF) is 0, and
 *    for finite argument, only exp(0)=1 is exact.
 *
 * Accuracy:
 *    according to an error analysis, the error is always less than
 *    1 ulp (unit in the last place).
 *
 * Misc. info.
 *    For IEEE double
 *        if x >  7.09782712893383973096e+02 then exp(x) overflow
 *        if x < -7.45133219101941108420e+02 then exp(x) underflow
 *
 * Constants:
 * The hexadecimal values are the intended ones for the following
 * constants. The decimal values may be used, provided that the
 * compiler will convert from decimal to binary accurately enough
 * to produce the hexadecimal values shown.
 */


/* 0x3E663769, 0x72BEA4D0 */

namespace System;

public static partial class Math
{
    public static double Exp(double value)
    {
        const double one = 1.0;
        ReadOnlySpan<double> halF = [0.5, -0.5,];
        const double huge = 1.0e+300;
        const double twom1000 = 9.33263618503218878990e-302;
        const double o_threshold = 7.09782712893383973096e+02;
        const double u_threshold = -7.45133219101941108420e+02;
        ReadOnlySpan<double> ln2HI = [ 6.93147180369123816490e-01,
             -6.93147180369123816490e-01,];
        ReadOnlySpan<double> ln2LO = [ 1.90821492927058770002e-10,
             -1.90821492927058770002e-10,];
        const double invln2 = 1.44269504088896338700e+00;
        const double P1 = 1.66666666666666019037e-01;
        const double P2 = -2.77777777770155933842e-03;
        const double P3 = 6.61375632143793436117e-05;
        const double P4 = -1.65339022054652515390e-06;
        const double P5 = 4.13813679705723846039e-08;

        var y = 0.0;
        var hi = 0.0;
        var lo = 0.0;
        var c = 0.0;
        var t = 0.0;
        var k = 0;
        var xsb = 0;
        var hx = 0U;

        hx = (uint)FdHigh(value);   /* high word of value */
        xsb = (int)((hx >> 31) & 1);        /* sign bit of value */
        hx &= 0x7fffffff;       /* high word of |value| */

        /* filter out non-finite argument */
        if (hx >= 0x40862E42)
        {           /* if |value|>=709.78.0.. */
            if (hx >= 0x7ff00000)
            {
                if (((hx & 0xfffff) | (uint)FdLow(value)) != 0)
                    return value + value;       /* NaN */
                else return (xsb == 0) ? value : 0.0;   /* exp(+-inf)={inf,0} */
            }
            if (value > o_threshold) return huge * huge; /* overflow */
            if (value < u_threshold) return twom1000 * twom1000; /* underflow */
        }

        /* argument reduction */
        if (hx > 0x3fd62e42)
        {       /* if  |value| > 0.5 ln2 */
            if (hx < 0x3FF0A2B2)
            {   /* and |value| < 1.5 ln2 */
                hi = value - ln2HI[xsb]; lo = ln2LO[xsb]; k = 1 - xsb - xsb;
            }
            else
            {
                k = (int)(invln2 * value + halF[xsb]);
                t = k;
                hi = value - t * ln2HI[0];  /* t*ln2HI is exact here */
                lo = t * ln2LO[0];
            }
            value = hi - lo;
        }
        else if (hx < 0x3e300000)
        {   /* when |value|<2**-28 */
            if (huge + value > one) return one + value;/* trigger inexact */
        }
        else k = 0;

        /* value is now in primary range */
        t = value * value;
        c = value - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
        if (k == 0) return one - ((value * c) / (c - 2.0) - value);
        else y = one - ((lo - (value * c) / (2.0 - c)) - hi);
        if (k >= -1021)
        {
            y = FdWithHigh(y, FdHigh(y) + ((k << 20))); /* add k to y's exponent */
            return y;
        }
        else
        {
            y = FdWithHigh(y, FdHigh(y) + (((k + 1000) << 20)));/* add k to y's exponent */
            return y * twom1000;
        }
    }
}
