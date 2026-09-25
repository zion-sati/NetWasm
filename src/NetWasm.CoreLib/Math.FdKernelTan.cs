// Managed port of Sun fdlibm k_tan.c. Upstream: biosbits/fdlibm
// commit dfd9eaed985332a1c3af98c2bab4004eea217d3d.
// Adaptations: C# bit access, immutable local tables and bounded stack scratch.
/* #pragma ident "@(#)k_tan.c 1.5 04/04/22 SMI" */

/*
 * ====================================================
 * Copyright 2004 Sun Microsystems, Inc.  All Rights Reserved.
 *
 * Permission to use, copy, modify, and distribute this
 * software is freely granted, provided that this notice
 * is preserved.
 * ====================================================
 */

/* INDENT OFF */
/* __kernel_tan( x, y, k )
 * kernel tan function on [-pi/4, pi/4], pi/4 ~ 0.7854
 * Input x is assumed to be bounded by ~pi/4 in magnitude.
 * Input y is the tail of x.
 * Input k indicates whether tan (if k = 1) or -1/tan (if k = -1) is returned.
 *
 * Algorithm
 *    1. Since tan(-x) = -tan(x), we need only to consider positive x.
 *    2. if x < 2^-28 (hx<0x3e300000 0), return x with inexact if x!=0.
 *    3. tan(x) is approximated by a odd polynomial of degree 27 on
 *       [0,0.67434]
 *                       3             27
 *           tan(x) ~ x + T1*x + ... + T13*x
 *       where
 *
 *             |tan(x)         2     4            26   |     -59.2
 *             |----- - (1+T1*x +T2*x +.... +T13*x    )| <= 2
 *             |  x                     |
 *
 *       Note: tan(x+y) = tan(x) + tan'(x)*y
 *                  ~ tan(x) + (1+x*x)*y
 *       Therefore, for better accuracy in computing tan(x+y), let
 *             3      2      2       2       2
 *        r = x *(T2+x *(T3+x *(...+x *(T12+x *T13))))
 *       then
 *                     3    2
 *        tan(x+y) = x + (T1*x + (x *(r+y)+y))
 *
 *      4. For x in [0.67434,pi/4],  let y = pi/4 - x, then
 *        tan(x) = tan(pi/4-y) = (1-tan(y))/(1+tan(y))
 *               = 1 - 2*(tan(y) - (tan(y)^2)/(1+tan(y)))
 */



/* INDENT ON */

namespace System;

public static partial class Math
{
    private static double FdKernelTan(double x, double y, int iy)
    {
        ReadOnlySpan<double> xxx = [
                 3.33333333333334091986e-01,
             1.33333333333201242699e-01,
             5.39682539762260521377e-02,
             2.18694882948595424599e-02,
             8.86323982359930005737e-03,
             3.59207910759131235356e-03,
             1.45620945432529025516e-03,
             5.88041240820264096874e-04,
             2.46463134818469906812e-04,
             7.81794442939557092300e-05,
             7.14072491382608190305e-05,
            -1.85586374855275456654e-05,
             2.59073051863633712884e-05,
         1.00000000000000000000e+00,
         7.85398163397448278999e-01,
         3.06161699786838301793e-17
        ];

        var z = 0.0;
        var r = 0.0;
        var v = 0.0;
        var w = 0.0;
        var s = 0.0;
        var ix = 0;
        var hx = 0;

        hx = FdHigh(x);     /* high word of x */
        ix = hx & 0x7fffffff;           /* high word of |x| */
        if (ix < 0x3e300000)
        {           /* x < 2**-28 */
            if ((int)x == 0)
            {       /* generate inexact */
                if (((ix | FdLow(x)) | (iy + 1)) == 0)
                    return xxx[13] / Abs(x);
                else
                {
                    if (iy == 1)
                        return x;
                    else
                    {   /* compute -1 / (x+y) carefully */
                        var a = 0.0;
                        var t = 0.0;

                        z = w = x + y;
                        z = FdWithLow(z, 0);
                        v = y - (z - x);
                        t = a = -xxx[13] / w;
                        t = FdWithLow(t, 0);
                        s = xxx[13] + t * z;
                        return t + a * (s + t * v);
                    }
                }
            }
        }
        if (ix >= 0x3FE59428)
        {   /* |x| >= 0.6744 */
            if (hx < 0)
            {
                x = -x;
                y = -y;
            }
            z = xxx[14] - x;
            w = xxx[15] - y;
            x = z + w;
            y = 0.0;
        }
        z = x * x;
        w = z * z;
        /*
         * Break x^5*(xxx[1]+x^2*xxx[2]+...) into
         * x^5(xxx[1]+x^4*xxx[3]+...+x^20*xxx[11]) +
         * x^5(x^2*(xxx[2]+x^4*xxx[4]+...+x^22*[T12]))
         */
        r = xxx[1] + w * (xxx[3] + w * (xxx[5] + w * (xxx[7] + w * (xxx[9] +
            w * xxx[11]))));
        v = z * (xxx[2] + w * (xxx[4] + w * (xxx[6] + w * (xxx[8] + w * (xxx[10] +
            w * xxx[12])))));
        s = z * x;
        r = y + z * (s * (r + v) + y);
        r += xxx[0] * s;
        w = x + r;
        if (ix >= 0x3FE59428)
        {
            v = (double)iy;
            return (double)(1 - ((hx >> 30) & 2)) *
                (v - 2.0 * (x - (w * w / (w + v) - r)));
        }
        if (iy == 1)
            return w;
        else
        {
            /*
             * if allow error up to 2 ulp, simply return
             * -1.0 / (x+r) here
             */
            /* compute -1.0 / (x+r) accurately */
            var a = 0.0;
            var t = 0.0;
            z = w;
            z = FdWithLow(z, 0);
            v = r - (z - x);    /* z+v = r+x */
            t = a = -1.0 / w;   /* a = -1.0/w */
            t = FdWithLow(t, 0);
            s = 1.0 + t * z;
            return t + a * (s + t * v);
        }
    }
}
