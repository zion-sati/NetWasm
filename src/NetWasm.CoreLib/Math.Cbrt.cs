// Managed port of Sun fdlibm s_cbrt.c. Upstream: biosbits/fdlibm
// commit dfd9eaed985332a1c3af98c2bab4004eea217d3d.
// Adaptations: C# bit access.
/* @(#)s_cbrt.c 1.3 95/01/18 */
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

/* cbrt(x)
 * Return cube root of x
 */

namespace System;

public static partial class Math
{
    public static double Cbrt(double value)
    {
        const int B1 = 715094163; /* B1 = (682-0.03306235651)*2**20 */
        const int B2 = 696219795; /* B2 = (664-0.03306235651)*2**20 */
        const double C = 5.42857142857142815906e-01; /* 19/35     = 0x3FE15F15, 0xF15F15F1 */
        const double D = -7.05306122448979611050e-01; /* -864/1225 = 0xBFE691DE, 0x2532C834 */
        const double E = 1.41428571428571436819e+00; /* 99/70     = 0x3FF6A0EA, 0x0EA0EA0F */
        const double F = 1.60714285714285720630e+00; /* 45/28     = 0x3FF9B6DB, 0x6DB6DB6E */
        const double G = 3.57142857142857150787e-01; /* 5/14      = 0x3FD6DB6D, 0xB6DB6DB7 */

        int hx;
        double r, s, t = 0.0, w;
        int sign;
        hx = FdHigh(value);     /* high word of x */
        sign = hx & unchecked((int)0x80000000);       /* sign= sign(x) */
        hx ^= sign;
        if (hx >= 0x7ff00000) return (value + value); /* cbrt(NaN,INF) is itself */
        if ((hx | FdLow(value)) == 0)
            return (value);     /* cbrt(0) is itself */

        value = FdWithHigh(value, hx);   /* x <- |x| */
        /* rough cbrt to 5 bits */
        if (hx < 0x00100000)        /* subnormal number */
        {
            t = FdWithHigh(t, 0x43500000);      /* set t= 2**54 */
            t *= value; t = FdWithHigh(t, FdHigh(t) / 3 + B2);
        }
        else
            t = FdWithHigh(t, hx / 3 + B1);

        /* new cbrt to 23 bits, may be implemented in single precision */
        r = t * t / value;
        s = C + r * t;
        t *= G + F / (s + E + D / s);

        /* chopped to 20 bits and make it larger than cbrt(x) */
        t = FdWithLow(t, 0); t = FdWithHigh(t, FdHigh(t) + 0x00000001);

        /* one step newton iteration to 53 bits with error less than 0.667 ulps */
        s = t * t;      /* t*t is exact */
        r = value / s;
        w = t + t;
        r = (r - t) / (w + r);  /* r-s is exact */
        t = t + t * r;

        /* retore the sign bit */
        t = FdWithHigh(t, FdHigh(t) | sign);
        return (t);
    }
}
