// Managed port of Sun fdlibm e_acos.c. Upstream: biosbits/fdlibm
// commit dfd9eaed985332a1c3af98c2bab4004eea217d3d.
// Adaptations: C# bit access.
/* @(#)e_acos.c 1.3 95/01/18 */
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

/* __ieee754_acos(x)
 * Method :                  
 *	acos(x)  = pi/2 - asin(x)
 *	acos(-x) = pi/2 + asin(x)
 * For |x|<=0.5
 *	acos(x) = pi/2 - (x + x*x^2*R(x^2))	(see asin.c)
 * For x>0.5
 * 	acos(x) = pi/2 - (pi/2 - 2asin(sqrt((1-x)/2)))
 *		= 2asin(sqrt((1-x)/2))  
 *		= 2s + 2s*z*R(z) 	...z=(1-x)/2, s=sqrt(z)
 *		= 2f + (2c + 2s*z*R(z))
 *     where f=hi part of s, and c = (z-f*f)/(s+f) is the correction term
 *     for f so that f+c ~ sqrt(z).
 * For x<-0.5
 *	acos(x) = pi - 2asin(sqrt((1-|x|)/2))
 *		= pi - 0.5*(s+s*z*R(z)), where z=(1-|x|)/2,s=sqrt(z)
 *
 * Special cases:
 *	if x is NaN, return x itself;
 *	if |x|>1, return NaN with invalid signal.
 *
 * Function needed: sqrt
 */



namespace System;

public static partial class Math
{
    public static double Acos(double value)
    {
        const double one = 1.00000000000000000000e+00; /* 0x3FF00000, 0x00000000 */
        const double pi = 3.14159265358979311600e+00; /* 0x400921FB, 0x54442D18 */
        const double pio2_hi = 1.57079632679489655800e+00; /* 0x3FF921FB, 0x54442D18 */
        const double pio2_lo = 6.12323399573676603587e-17; /* 0x3C91A626, 0x33145C07 */
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

        double z, p, q, r, w, s, c, df;
        int hx, ix;
        hx = FdHigh(value);
        ix = hx & 0x7fffffff;
        if (ix >= 0x3ff00000)
        {   /* |x| >= 1 */
            if (((ix - 0x3ff00000) | FdLow(value)) == 0)
            {  /* |x|==1 */
                if (hx > 0) return 0.0;     /* acos(1) = 0  */
                else return pi + 2.0 * pio2_lo; /* acos(-1)= pi */
            }
            return (value - value) / (value - value);     /* acos(|x|>1) is NaN */
        }
        if (ix < 0x3fe00000)
        {   /* |x| < 0.5 */
            if (ix <= 0x3c600000) return pio2_hi + pio2_lo;/*if|x|<2**-57*/
            z = value * value;
            p = z * (pS0 + z * (pS1 + z * (pS2 + z * (pS3 + z * (pS4 + z * pS5)))));
            q = one + z * (qS1 + z * (qS2 + z * (qS3 + z * qS4)));
            r = p / q;
            return pio2_hi - (value - (pio2_lo - value * r));
        }
        else if (hx < 0)
        {     /* x < -0.5 */
            z = (one + value) * 0.5;
            p = z * (pS0 + z * (pS1 + z * (pS2 + z * (pS3 + z * (pS4 + z * pS5)))));
            q = one + z * (qS1 + z * (qS2 + z * (qS3 + z * qS4)));
            s = Sqrt(z);
            r = p / q;
            w = r * s - pio2_lo;
            return pi - 2.0 * (s + w);
        }
        else
        {           /* x > 0.5 */
            z = (one - value) * 0.5;
            s = Sqrt(z);
            df = s;
            df = FdWithLow(df, 0);
            c = (z - df * df) / (s + df);
            p = z * (pS0 + z * (pS1 + z * (pS2 + z * (pS3 + z * (pS4 + z * pS5)))));
            q = one + z * (qS1 + z * (qS2 + z * (qS3 + z * qS4)));
            r = p / q;
            w = r * s + c;
            return 2.0 * (df + w);
        }
    }
}
