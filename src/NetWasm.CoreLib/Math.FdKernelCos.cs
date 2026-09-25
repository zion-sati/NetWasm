// Managed port of Sun fdlibm k_cos.c. Upstream: biosbits/fdlibm
// commit dfd9eaed985332a1c3af98c2bab4004eea217d3d.
// Adaptations: C# bit access, immutable local tables and bounded stack scratch.
/* @(#)k_cos.c 1.3 95/01/18 */
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

/*
 * __kernel_cos( x,  y )
 * kernel cos function on [-pi/4, pi/4], pi/4 ~ 0.785398164
 * Input x is assumed to be bounded by ~pi/4 in magnitude.
 * Input y is the tail of x.
 *
 * Algorithm
 *    1. Since cos(-x) = cos(x), we need only to consider positive x.
 *    2. if x < 2^-27 (hx<0x3e400000 0), return 1 with inexact if x!=0.
 *    3. cos(x) is approximated by a polynomial of degree 14 on
 *       [0,pi/4]
 *                               4            14
 *           cos(x) ~ 1 - x*x/2 + C1*x + ... + C6*x
 *       where the remez error is
 *
 *     |              2     4     6     8     10    12     14 |     -58
 *     |cos(x)-(1-.5*x +C1*x +C2*x +C3*x +C4*x +C5*x  +C6*x  )| <= 2
 *     |                                       |
 *
 *                    4     6     8     10    12     14
 *    4. let r = C1*x +C2*x +C3*x +C4*x +C5*x  +C6*x  , then
 *           cos(x) = 1 - x*x/2 + r
 *       since cos(x+y) ~ cos(x) - sin(x)*y
 *              ~ cos(x) - x*y,
 *       a correction term is necessary in cos(x) and hence
 *        cos(x+y) = 1 - (x*x/2 - (r - x*y))
 *       For better accuracy when x > 0.3, let qx = |x|/4 with
 *       the last 32 bits mask off, and if x > 0.78125, let qx = 0.28125.
 *       Then
 *        cos(x+y) = (1-qx) - ((x*x/2-qx) - (r-x*y)).
 *       Note that 1-qx and (x*x/2-qx) is EXACT here, and the
 *       magnitude of the latter is at least a quarter of x*x/2,
 *       thus, reducing the rounding error in the subtraction.
 */


/* 0xBDA8FAE9, 0xBE8838D4 */

namespace System;

public static partial class Math
{
    private static double FdKernelCos(double x, double y)
    {
        const double one = 1.00000000000000000000e+00;
        const double C1 = 4.16666666666666019037e-02;
        const double C2 = -1.38888888888741095749e-03;
        const double C3 = 2.48015872894767294178e-05;
        const double C4 = -2.75573143513906633035e-07;
        const double C5 = 2.08757232129817482790e-09;
        const double C6 = -1.13596475577881948265e-11;

        var a = 0.0;
        var hz = 0.0;
        var z = 0.0;
        var r = 0.0;
        var qx = 0.0;
        var ix = 0;
        ix = FdHigh(x) & 0x7fffffff;    /* ix = |x|'s high word*/
        if (ix < 0x3e400000)
        {           /* if x < 2**27 */
            if (((int)x) == 0) return one;      /* generate inexact */
        }
        z = x * x;
        r = z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
        if (ix < 0x3FD33333)            /* if |x| < 0.3 */
            return one - (0.5 * z - (z * r - x * y));
        else
        {
            if (ix > 0x3fe90000)
            {       /* x > 0.78125 */
                qx = 0.28125;
            }
            else
            {
                qx = FdWithHigh(qx, ix - 0x00200000);   /* x/4 */
                qx = FdWithLow(qx, 0);
            }
            hz = 0.5 * z - qx;
            a = one - qx;
            return a - (hz - (z * r - x * y));
        }
    }
}
