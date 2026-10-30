// Managed port of Sun fdlibm e_asin.c. Upstream: biosbits/fdlibm
// commit dfd9eaed985332a1c3af98c2bab4004eea217d3d.
// Adaptations: C# bit access.
/* @(#)e_asin.c 1.3 95/01/18 */
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

/* __ieee754_asin(x)
 * Method :                  
 *	Since  asin(x) = x + x^3/6 + x^5*3/40 + x^7*15/336 + ...
 *	we approximate asin(x) on [0,0.5] by
 *		asin(x) = x + x*x^2*R(x^2)
 *	where
 *		R(x^2) is a rational approximation of (asin(x)-x)/x^3 
 *	and its remez error is bounded by
 *		|(asin(x)-x)/x^3 - R(x^2)| < 2^(-58.75)
 *
 *	For x in [0.5,1]
 *		asin(x) = pi/2-2*asin(sqrt((1-x)/2))
 *	Let y = (1-x), z = y/2, s := sqrt(z), and pio2_hi+pio2_lo=pi/2;
 *	then for x>0.98
 *		asin(x) = pi/2 - 2*(s+s*z*R(z))
 *			= pio2_hi - (2*(s+s*z*R(z)) - pio2_lo)
 *	For x<=0.98, let pio4_hi = pio2_hi/2, then
 *		f = hi part of s;
 *		c = sqrt(z) - f = (z-f*f)/(s+f) 	...f+c=sqrt(z)
 *	and
 *		asin(x) = pi/2 - 2*(s+s*z*R(z))
 *			= pio4_hi+(pio4-2s)-(2s*z*R(z)-pio2_lo)
 *			= pio4_hi+(pio4-2f)-(2s*z*R(z)-(pio2_lo+2c))
 *
 * Special cases:
 *	if x is NaN, return x itself;
 *	if |x|>1, return NaN with invalid signal.
 *
 */



namespace System;

public static partial class Math
{
    public static double Asin(double value)
    {
        const double one = 1.00000000000000000000e+00; /* 0x3FF00000, 0x00000000 */
        const double huge = 1.000e+300;
        const double pio2_hi = 1.57079632679489655800e+00; /* 0x3FF921FB, 0x54442D18 */
        const double pio2_lo = 6.12323399573676603587e-17; /* 0x3C91A626, 0x33145C07 */
        const double pio4_hi = 7.85398163397448278999e-01; /* 0x3FE921FB, 0x54442D18 */
        /* coefficient for R(x^2) */
        const double pS0 = 1.66666666666666657415e-01; /* 0x3FC55555, 0x55555555 */
        const double pS1 = -3.25565818622400915405e-01; /* 0xBFD4D612, 0x03EB6F7D */
        const double pS2 = 2.01212532134862925881e-01; /* 0x3FC9C155, 0x0E884455 */
        const double pS3 = -4.00555345006794114027e-02; /* 0xBFA48228, 0xB5688F3B */
        const double pS4 = 7.91534994289814532176e-04; /* 0x3F49EFE0, 0x7501B288 */
        const double pS5 = 3.47933107596021167570e-05; /* 0x3F023DE1, 0x0DFDF709 */
        const double qS1 = -2.40339491173441421878e+00; /* 0xC0033A27, 0x1C8A2D4B */
        const double qS2 = 2.02094576023350569471e+00; /* 0x40002AE5, 0x9C598AC8 */
        const double qS3 = -6.88283971605453293030e-01; /* 0xBFE6066C, 0x1B8D0159 */
        const double qS4 = 7.70381505559019352791e-02; /* 0x3FB3B8C5, 0xB12E9282 */

        double t = 0, w, p, q, c, r, s;
        int hx, ix;
        hx = FdHigh(value);
        ix = hx & 0x7fffffff;
        if (ix >= 0x3ff00000)
        {       /* |x|>= 1 */
            if (((ix - 0x3ff00000) | FdLow(value)) == 0)
                /* asin(1)=+-pi/2 with inexact */
                return value * pio2_hi + value * pio2_lo;
            return (value - value) / (value - value);     /* asin(|x|>1) is NaN */
        }
        else if (ix < 0x3fe00000)
        {   /* |x|<0.5 */
            if (ix < 0x3e400000)
            {       /* if |x| < 2**-27 */
                if (huge + value > one) return value;/* return x with inexact if x!=0*/
            }
            else
                t = value * value;
            p = t * (pS0 + t * (pS1 + t * (pS2 + t * (pS3 + t * (pS4 + t * pS5)))));
            q = one + t * (qS1 + t * (qS2 + t * (qS3 + t * qS4)));
            w = p / q;
            return value + value * w;
        }
        /* 1> |x|>= 0.5 */
        w = one - Abs(value);
        t = w * 0.5;
        p = t * (pS0 + t * (pS1 + t * (pS2 + t * (pS3 + t * (pS4 + t * pS5)))));
        q = one + t * (qS1 + t * (qS2 + t * (qS3 + t * qS4)));
        s = Sqrt(t);
        if (ix >= 0x3FEF3333)
        {   /* if |x| > 0.975 */
            w = p / q;
            t = pio2_hi - (2.0 * (s + s * w) - pio2_lo);
        }
        else
        {
            w = s;
            w = FdWithLow(w, 0);
            c = (t - w * w) / (s + w);
            r = p / q;
            p = 2.0 * s * r - (pio2_lo - 2.0 * c);
            q = pio4_hi - 2.0 * w;
            t = pio4_hi - (p - q);
        }
        if (hx > 0) return t; else return -t;
    }
}
