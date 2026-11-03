// Managed port of musl's cbrtf.c: FreeBSD msun's s_cbrtf.c. Upstream: musl
// v1.2.5, commit 0784374d561435f7c787a555aeab8ede699ed298. Adaptations: C# bit
// access, and double_t as double, as on WebAssembly.
/*
 * Conversion to float by Ian Lance Taylor, Cygnus Support, ian@cygnus.com.
 * Debugged and optimized by Bruce D. Evans.
 */
/*
 * ====================================================
 * Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved.
 *
 * Developed at SunPro, a Sun Microsystems, Inc. business.
 * Permission to use, copy, modify, and distribute this
 * software is freely granted, provided that this notice
 * is preserved.
 * ====================================================
 */

namespace System;

public static partial class MathF
{
    private const uint CbrtB1 = 709958130; /* B1 = (127-127.0/3-0.03306235651)*2**23 */
    private const uint CbrtB2 = 642849266; /* B2 = (127-127.0/3-24/3-0.03306235651)*2**23 */

    public static float Cbrt(float x)
    {
        uint u = BitConverter.SingleToUInt32Bits(x);
        uint hx = u & 0x7fffffff;

        if (hx >= 0x7f800000) /* cbrt(NaN,INF) is itself */
            return x + x;

        /* rough cbrt to 5 bits */
        if (hx < 0x00800000) /* zero or subnormal? */
        {
            if (hx == 0)
                return x; /* cbrt(+-0) is itself */
            u = BitConverter.SingleToUInt32Bits(x * 16777216.0f); /* 0x1p24f */
            hx = u & 0x7fffffff;
            hx = hx / 3 + CbrtB2;
        }
        else
        {
            hx = hx / 3 + CbrtB1;
        }
        u &= 0x80000000;
        u |= hx;

        /*
         * First step Newton iteration (solving t*t-x/t == 0) to 16 bits.  In
         * double precision so that its terms can be arranged for efficiency
         * without causing overflow or underflow.
         */
        double t = BitConverter.UInt32BitsToSingle(u);
        double r = t * t * t;
        t = t * ((double)x + x + r) / (x + r + r);

        /*
         * Second step Newton iteration to 47 bits.  In double precision for
         * efficiency and accuracy.
         */
        r = t * t * t;
        t = t * ((double)x + x + r) / (x + r + r);

        /* rounding to 24 bits is perfect in round-to-nearest mode */
        return (float)t;
    }
}
