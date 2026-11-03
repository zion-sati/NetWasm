// Managed port of musl's log10f.c and log1pf.c: FreeBSD msun's e_log10f.c and
// s_log1pf.c. Upstream: musl v1.2.5, commit 0784374d561435f7c787a555aeab8ede699ed298.
// Adaptations: C# bit access, hexadecimal constants written as their exact decimal
// values, and log1pf's FORCE_EVAL of a subnormal argument, which only raises the
// underflow flag, left out.
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
    private const float IvLn10Hi = 4.3432617188e-01f; /* 0x3ede6000 */
    private const float IvLn10Lo = -3.1689971365e-05f; /* 0xb804ead9 */
    private const float Log10Of2Hi = 3.0102920532e-01f; /* 0x3e9a2080 */
    private const float Log10Of2Lo = 7.9034151668e-07f; /* 0x355427db */
    private const float Ln2Hi = 6.9313812256e-01f; /* 0x3f317180 */
    private const float Ln2Lo = 9.0580006145e-06f; /* 0x3717f7d1 */
    /* |(log(1+s)-log(1-s))/s - Lg(s)| < 2**-34.24 (~[-4.95e-11, 4.97e-11]). */
    private const float Lg1 = 0.66666662693023681640625f; /* 0xaaaaaa.0p-24 */
    private const float Lg2 = 0.4000097215175628662109375f; /* 0xccce13.0p-25 */
    private const float Lg3 = 0.284987866878509521484375f; /* 0x91e9ee.0p-25 */
    private const float Lg4 = 0.2427907884120941162109375f; /* 0xf89e26.0p-26 */

    public static float Log10(float x)
    {
        uint ix = BitConverter.SingleToUInt32Bits(x);
        int k = 0;
        if (ix < 0x00800000 || ix >> 31 != 0) /* x < 2**-126  */
        {
            if (ix << 1 == 0)
                return -1 / (x * x); /* log(+-0)=-inf */
            if (ix >> 31 != 0)
                return (x - x) / 0.0f; /* log(-#) = NaN */
            /* subnormal number, scale up x */
            k -= 25;
            x *= 33554432.0f; /* 0x1p25f */
            ix = BitConverter.SingleToUInt32Bits(x);
        }
        else if (ix >= 0x7f800000)
        {
            return x;
        }
        else if (ix == 0x3f800000)
        {
            return 0;
        }

        /* reduce x into [sqrt(2)/2, sqrt(2)] */
        ix += 0x3f800000 - 0x3f3504f3;
        k += (int)(ix >> 23) - 0x7f;
        ix = (ix & 0x007fffff) + 0x3f3504f3;
        x = BitConverter.UInt32BitsToSingle(ix);

        float f = x - 1.0f;
        float s = f / (2.0f + f);
        float z = s * s;
        float w = z * z;
        float t1 = w * (Lg2 + w * Lg4);
        float t2 = z * (Lg1 + w * Lg3);
        float r = t2 + t1;
        float hfsq = 0.5f * f * f;

        float hi = f - hfsq;
        hi = BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(hi) & 0xfffff000);
        float lo = f - hi - hfsq + s * (hfsq + r);
        float dk = k;
        return dk * Log10Of2Lo + (lo + hi) * IvLn10Lo + lo * IvLn10Hi + hi * IvLn10Hi + dk * Log10Of2Hi;
    }

    // log1pf: log(1 + x), which Acosh uses.
    private static float Log1P(float x)
    {
        uint ix = BitConverter.SingleToUInt32Bits(x);
        float c = 0, f = 0;
        int k = 1;
        if (ix < 0x3ed413d0 || ix >> 31 != 0) /* 1+x < sqrt(2)+  */
        {
            if (ix >= 0xbf800000) /* x <= -1.0 */
            {
                if (x == -1)
                    return x / 0.0f; /* log1p(-1)=+inf */
                return (x - x) / 0.0f; /* log1p(x<-1)=NaN */
            }
            if (ix << 1 < 0x33800000u << 1) /* |x| < 2**-24 */
                return x;
            if (ix <= 0xbe95f619) /* sqrt(2)/2- <= 1+x < sqrt(2)+ */
            {
                k = 0;
                c = 0;
                f = x;
            }
        }
        else if (ix >= 0x7f800000)
        {
            return x;
        }
        if (k != 0)
        {
            float u = 1 + x;
            uint iu = BitConverter.SingleToUInt32Bits(u);
            iu += 0x3f800000 - 0x3f3504f3;
            k = (int)(iu >> 23) - 0x7f;
            /* correction term ~ log(1+x)-log(u), avoid underflow in c/u */
            if (k < 25)
            {
                c = k >= 2 ? 1 - (u - x) : x - (u - 1);
                c /= u;
            }
            else
            {
                c = 0;
            }
            /* reduce u into [sqrt(2)/2, sqrt(2)] */
            iu = (iu & 0x007fffff) + 0x3f3504f3;
            u = BitConverter.UInt32BitsToSingle(iu);
            f = u - 1;
        }
        float s = f / (2.0f + f);
        float z = s * s;
        float w = z * z;
        float t1 = w * (Lg2 + w * Lg4);
        float t2 = z * (Lg1 + w * Lg3);
        float r = t2 + t1;
        float hfsq = 0.5f * f * f;
        float dk = k;
        return s * (hfsq + r) + (dk * Ln2Lo + c) - hfsq + f + dk * Ln2Hi;
    }
}
