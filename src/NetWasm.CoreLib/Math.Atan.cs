// Managed port of Sun fdlibm s_atan.c. Upstream: biosbits/fdlibm
// commit dfd9eaed985332a1c3af98c2bab4004eea217d3d.
// Adaptations: C# bit access, and the atanhi/atanlo pair chosen by a switch.

/* @(#)s_atan.c 1.3 95/01/18 */
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

/* atan(x)
 * Method
 *   1. Reduce x to positive by atan(x) = -atan(-x).
 *   2. According to the integer k=4t+0.25 chopped, t=x, the argument
 *      is further reduced to one of the following intervals and the
 *      arctangent of t is evaluated by the corresponding formula:
 *
 *      [0,7/16]      atan(x) = t-t^3*(a1+t^2*(a2+...(a10+t^2*a11)...)
 *      [7/16,11/16]  atan(x) = atan(1/2) + atan( (t-0.5)/(1+t/2) )
 *      [11/16.19/16] atan(x) = atan( 1 ) + atan( (t-1)/(1+t) )
 *      [19/16,39/16] atan(x) = atan(3/2) + atan( (t-1.5)/(1+1.5t) )
 *      [39/16,INF]   atan(x) = atan(INF) + atan( -1/t )
 *
 * Constants:
 * The hexadecimal values are the intended ones for the following 
 * constants. The decimal values may be used, provided that the 
 * compiler will convert from decimal to binary accurately enough 
 * to produce the hexadecimal values shown.
 */

namespace System;

public static partial class Math
{
    public static double Atan(double value)
    {
        const double one = 1.0;
        const double huge = 1.0e300;
        const double aT0 = 3.33333333333329318027e-01; /* 0x3FD55555, 0x5555550D */
        const double aT1 = -1.99999999998764832476e-01; /* 0xBFC99999, 0x9998EBC4 */
        const double aT2 = 1.42857142725034663711e-01; /* 0x3FC24924, 0x920083FF */
        const double aT3 = -1.11111104054623557880e-01; /* 0xBFBC71C6, 0xFE231671 */
        const double aT4 = 9.09088713343650656196e-02; /* 0x3FB745CD, 0xC54C206E */
        const double aT5 = -7.69187620504482999495e-02; /* 0xBFB3B0F2, 0xAF749A6D */
        const double aT6 = 6.66107313738753120669e-02; /* 0x3FB10D66, 0xA0D03D51 */
        const double aT7 = -5.83357013379057348645e-02; /* 0xBFADDE2D, 0x52DEFD9A */
        const double aT8 = 4.97687799461593236017e-02; /* 0x3FA97B4B, 0x24760DEB */
        const double aT9 = -3.65315727442169155270e-02; /* 0xBFA2B444, 0x2C6A6C2F */
        const double aT10 = 1.62858201153657823623e-02; /* 0x3F90AD3A, 0xE322DA11 */
        /* atan(inf)hi and lo: 0x3FF921FB, 0x54442D18 and 0x3C91A626, 0x33145C07 */
        const double atanInfHi = 1.57079632679489655800e+00;
        const double atanInfLo = 6.12323399573676603587e-17;

        var x = value;
        double w, s1, s2, z;
        int ix, hx, id;

        hx = FdHigh(x);
        ix = hx & 0x7fffffff;
        if (ix >= 0x44100000)
        {   /* if |x| >= 2^66 */
            if (ix > 0x7ff00000 ||
                (ix == 0x7ff00000 && (FdLow(x) != 0)))
                return x + x;       /* NaN */
            if (hx > 0) return atanInfHi + atanInfLo;
            else return -atanInfHi - atanInfLo;
        }
        if (ix < 0x3fdc0000)
        {   /* |x| < 0.4375 */
            if (ix < 0x3e200000)
            {   /* |x| < 2^-29 */
                if (huge + x > one) return x;   /* raise inexact */
            }
            id = -1;
        }
        else
        {
            x = Abs(x);
            if (ix < 0x3ff30000)
            {       /* |x| < 1.1875 */
                if (ix < 0x3fe60000)
                {   /* 7/16 <=|x|<11/16 */
                    id = 0; x = (2.0 * x - one) / (2.0 + x);
                }
                else
                {           /* 11/16<=|x|< 19/16 */
                    id = 1; x = (x - one) / (x + one);
                }
            }
            else
            {
                if (ix < 0x40038000)
                {   /* |x| < 2.4375 */
                    id = 2; x = (x - 1.5) / (one + 1.5 * x);
                }
                else
                {           /* 2.4375 <= |x| < 2^66 */
                    id = 3; x = -1.0 / x;
                }
            }
        }
        /* end of argument reduction */
        z = x * x;
        w = z * z;
        /* break sum from i=0 to 10 aT[i]z**(i+1) into odd and even poly */
        s1 = z * (aT0 + w * (aT2 + w * (aT4 + w * (aT6 + w * (aT8 + w * aT10)))));
        s2 = w * (aT1 + w * (aT3 + w * (aT5 + w * (aT7 + w * aT9))));
        if (id < 0) return x - x * (s1 + s2);
        var (atanhi, atanlo) = id switch
        {
            0 => (4.63647609000806093515e-01, 2.26987774529616870924e-17), /* atan(0.5) */
            1 => (7.85398163397448278999e-01, 3.06161699786838301793e-17), /* atan(1.0) */
            2 => (9.82793723247329054082e-01, 1.39033110312309984516e-17), /* atan(1.5) */
            _ => (atanInfHi, atanInfLo),                                   /* atan(inf) */
        };
        z = atanhi - ((x * (s1 + s2) - atanlo) - x);
        return (hx < 0) ? -z : z;
    }
}
