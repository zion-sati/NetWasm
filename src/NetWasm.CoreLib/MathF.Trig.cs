// Managed port of musl's sinf.c, cosf.c, sincosf.c, tanf.c, __sindf.c, __cosdf.c,
// __tandf.c and __rem_pio2f.c: FreeBSD msun's float trigonometry, converted to
// float by Ian Lance Taylor and optimized by Bruce D. Evans. Upstream: musl v1.2.5,
// commit 0784374d561435f7c787a555aeab8ede699ed298.
// Adaptations: C# bit access, hexadecimal constants written as their exact decimal
// values, __rem_pio2f's medium-size reduction returning only the multiple of pi/2
// (an out parameter would keep the reduced argument in memory), huge arguments
// reduced in their own methods through Math's Payne-Hanek kernel, and the kernels'
// float results kept in double until the caller's single rounding, as C returns
// them.
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

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace System;

public static partial class MathF
{
    /* Small multiples of pi/2 rounded to double precision. */
    private const double Pio2x1 = 1.5707963267948966; /* 0x3FF921FB, 0x54442D18 */
    private const double Pio2x2 = 3.141592653589793; /* 0x400921FB, 0x54442D18 */
    private const double Pio2x3 = 4.71238898038469; /* 0x4012D97C, 0x7F3321D2 */
    private const double Pio2x4 = 6.283185307179586; /* 0x401921FB, 0x54442D18 */

    /* __sindf: |sin(x)/x - s(x)| < 2**-37.5 (~[-4.89e-12, 4.824e-12]). */
    /* __cosdf: |cos(x) - c(x)| < 2**-34.1 (~[-5.37e-11, 5.295e-11]). */
    /* __tandf: |tan(x)/x - t(x)| < 2**-25.5 (~[-2e-08, 2e-08]). */
    private const double S1 = -0.16666666641626524; // -0x15555554cbac77.0p-55
    private const double S2 = 0.008333329385889463; // 0x111110896efbb2.0p-59
    private const double S3 = -0.00019839334836096632; // -0x1a00f9e2cae774.0p-65
    private const double S4 = 2.718311493989822e-06; // 0x16cd878c3b46a7.0p-71
    private const double C0 = -0.499999997251031; // -0x1ffffffd0c5e81.0p-54
    private const double C1 = 0.04166662332373906; // 0x155553e1053a42.0p-57
    private const double C2 = -0.001388676377460993; // -0x16c087e80f1e27.0p-62
    private const double C3 = 2.439044879627741e-05; // 0x199342e0ee5069.0p-68
    private const double T0 = 0.3333313950307914; // 0x15554d3418c99f.0p-54
    private const double T1 = 0.13339200271297674; // 0x1112fd38999f72.0p-55
    private const double T2 = 0.05338123784456704; // 0x1b54c91d865afe.0p-57
    private const double T3 = 0.024528318116654728; // 0x191df3908c33ce.0p-58
    private const double T4 = 0.002974357433599673; // 0x185dadfcecf44e.0p-61
    private const double T5 = 0.009465647849436732; // 0x1362b9bf971bcd.0p-59

    /*
     * __rem_pio2f:
     * invpio2:  53 bits of 2/pi
     * pio2_1:   first 25 bits of pi/2
     * pio2_1t:  pi/2 - pio2_1
     */
    private const double ToInt = 6755399441055744.0; /* 1.5/EPS */
    private const double Pio4 = 0.7853981852531433; /* 0x1.921fb6p-1 */
    private const double InvPio2 = 6.36619772367581382433e-01; /* 0x3FE45F30, 0x6DC9C883 */
    private const double Pio2_1 = 1.57079631090164184570e+00; /* 0x3FF921FB, 0x50000000 */
    private const double Pio2_1t = 1.58932547735281966916e-08; /* 0x3E5110b4, 0x611A6263 */

    public static float Sin(float x)
    {
        double y;
        uint ix;
        int n, sign;

        ix = BitConverter.SingleToUInt32Bits(x);
        sign = (int)(ix >> 31);
        ix &= 0x7fffffff;

        if (ix <= 0x3f490fda)
        {  /* |x| ~<= pi/4 */
            if (ix < 0x39800000)
            {  /* |x| < 2**-12 */
                return x;
            }
            return (float)SinD(x);
        }
        if (ix <= 0x407b53d1)
        {  /* |x| ~<= 5*pi/4 */
            if (ix <= 0x4016cbe3)
            {  /* |x| ~<= 3pi/4 */
                if (sign != 0)
                    return (float)-CosD(x + Pio2x1);
                else
                    return (float)CosD(x - Pio2x1);
            }
            return (float)SinD(sign != 0 ? -(x + Pio2x2) : -(x - Pio2x2));
        }
        if (ix <= 0x40e231d5)
        {  /* |x| ~<= 9*pi/4 */
            if (ix <= 0x40afeddf)
            {  /* |x| ~<= 7*pi/4 */
                if (sign != 0)
                    return (float)CosD(x + Pio2x3);
                else
                    return (float)-CosD(x - Pio2x3);
            }
            return (float)SinD(sign != 0 ? x + Pio2x4 : x - Pio2x4);
        }

        /* sin(Inf or NaN) is NaN */
        if (ix >= 0x7f800000)
            return x - x;

        /* general argument reduction needed */
        if (ix >= 0x4dc90fdb)
            return SinHuge(x);
        var fn = RemPio2fMultiple(x);
        n = (int)fn;
        y = x - fn * Pio2_1 - fn * Pio2_1t;
        switch (n & 3)
        {
            case 0: return (float)SinD(y);
            case 1: return (float)CosD(y);
            case 2: return (float)SinD(-y);
            default:
                return (float)-CosD(y);
        }
    }

    public static float Cos(float x)
    {
        double y;
        uint ix;
        int n, sign;

        ix = BitConverter.SingleToUInt32Bits(x);
        sign = (int)(ix >> 31);
        ix &= 0x7fffffff;

        if (ix <= 0x3f490fda)
        {  /* |x| ~<= pi/4 */
            if (ix < 0x39800000)
            {  /* |x| < 2**-12 */
                return 1.0f;
            }
            return (float)CosD(x);
        }
        if (ix <= 0x407b53d1)
        {  /* |x| ~<= 5*pi/4 */
            if (ix > 0x4016cbe3)  /* |x|  ~> 3*pi/4 */
                return (float)-CosD(sign != 0 ? x + Pio2x2 : x - Pio2x2);
            else
            {
                if (sign != 0)
                    return (float)SinD(x + Pio2x1);
                else
                    return (float)SinD(Pio2x1 - x);
            }
        }
        if (ix <= 0x40e231d5)
        {  /* |x| ~<= 9*pi/4 */
            if (ix > 0x40afeddf)  /* |x| ~> 7*pi/4 */
                return (float)CosD(sign != 0 ? x + Pio2x4 : x - Pio2x4);
            else
            {
                if (sign != 0)
                    return (float)SinD(-x - Pio2x3);
                else
                    return (float)SinD(x - Pio2x3);
            }
        }

        /* cos(Inf or NaN) is NaN */
        if (ix >= 0x7f800000)
            return x - x;

        /* general argument reduction needed */
        if (ix >= 0x4dc90fdb)
            return CosHuge(x);
        var fn = RemPio2fMultiple(x);
        n = (int)fn;
        y = x - fn * Pio2_1 - fn * Pio2_1t;
        switch (n & 3)
        {
            case 0: return (float)CosD(y);
            case 1: return (float)SinD(-y);
            case 2: return (float)-CosD(y);
            default:
                return (float)SinD(y);
        }
    }

    // musl's sincosf: one argument reduction for both results, each exactly
    // what Sin and Cos return. NetWasm: the pair comes back as one v128 value,
    // so neither the call nor the tuple needs a value frame.
    public static (float Sin, float Cos) SinCos(float x)
    {
        var both = SinCosPair(x);
        return (both.ToScalar(), both.GetElement(1));
    }

    private static Vector128<float> SinCosPair(float x)
    {
        uint ix;
        int sign;

        ix = BitConverter.SingleToUInt32Bits(x);
        sign = (int)(ix >> 31);
        ix &= 0x7fffffff;

        /* |x| ~<= pi/4 */
        if (ix <= 0x3f490fda)
        {
            /* |x| < 2**-12 */
            if (ix < 0x39800000)
                return Vector128.Create(x, 1.0f, 0f, 0f);
            return Pair(SinD(x), CosD(x));
        }

        /* |x| ~<= 5*pi/4 */
        if (ix <= 0x407b53d1)
        {
            if (ix <= 0x4016cbe3)
            {  /* |x| ~<= 3pi/4 */
                if (sign != 0)
                    return Pair(-CosD(x + Pio2x1), SinD(x + Pio2x1));
                else
                    return Pair(CosD(Pio2x1 - x), SinD(Pio2x1 - x));
            }
            /* -sin(x+c) is not correct if x+c could be 0: -0 vs +0 */
            return Pair(-SinD(sign != 0 ? x + Pio2x2 : x - Pio2x2), -CosD(sign != 0 ? x + Pio2x2 : x - Pio2x2));
        }

        /* |x| ~<= 9*pi/4 */
        if (ix <= 0x40e231d5)
        {
            if (ix <= 0x40afeddf)
            {  /* |x| ~<= 7*pi/4 */
                if (sign != 0)
                    return Pair(CosD(x + Pio2x3), -SinD(x + Pio2x3));
                else
                    return Pair(-CosD(x - Pio2x3), SinD(x - Pio2x3));
            }
            return Pair(SinD(sign != 0 ? x + Pio2x4 : x - Pio2x4), CosD(sign != 0 ? x + Pio2x4 : x - Pio2x4));
        }

        /* sin(Inf or NaN) is NaN */
        if (ix >= 0x7f800000)
            return Vector128.Create(x - x);

        /* general argument reduction needed */
        if (ix >= 0x4dc90fdb)
            return SinCosHuge(x);
        var fn = RemPio2fMultiple(x);
        return Quadrant((int)fn, x - fn * Pio2_1 - fn * Pio2_1t);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Vector128<float> SinCosHuge(float x)
    {
        var n = Math.RemPio2fHuge(x, out var y);
        return Quadrant(n, y);
    }

    private static Vector128<float> Quadrant(int n, double y)
    {
        var s = SinD(y);
        var c = CosD(y);
        switch (n & 3)
        {
            case 0: return Pair(s, c);
            case 1: return Pair(c, -s);
            case 2: return Pair(-s, -c);
            default:
                return Pair(-c, s);
        }
    }

    private static Vector128<float> Pair(double sin, double cos) =>
        Vector128.Create((float)sin, (float)cos, 0f, 0f);

    public static float Tan(float x)
    {
        double y;
        uint ix;
        int n, sign;

        ix = BitConverter.SingleToUInt32Bits(x);
        sign = (int)(ix >> 31);
        ix &= 0x7fffffff;

        if (ix <= 0x3f490fda)
        {  /* |x| ~<= pi/4 */
            if (ix < 0x39800000)
            {  /* |x| < 2**-12 */
                return x;
            }
            return (float)TanD(x, false);
        }
        if (ix <= 0x407b53d1)
        {  /* |x| ~<= 5*pi/4 */
            if (ix <= 0x4016cbe3)  /* |x| ~<= 3pi/4 */
                return (float)TanD(sign != 0 ? x + Pio2x1 : x - Pio2x1, true);
            else
                return (float)TanD(sign != 0 ? x + Pio2x2 : x - Pio2x2, false);
        }
        if (ix <= 0x40e231d5)
        {  /* |x| ~<= 9*pi/4 */
            if (ix <= 0x40afeddf)  /* |x| ~<= 7*pi/4 */
                return (float)TanD(sign != 0 ? x + Pio2x3 : x - Pio2x3, true);
            else
                return (float)TanD(sign != 0 ? x + Pio2x4 : x - Pio2x4, false);
        }

        /* tan(Inf or NaN) is NaN */
        if (ix >= 0x7f800000)
            return x - x;

        /* argument reduction */
        if (ix >= 0x4dc90fdb)
            return TanHuge(x);
        var fn = RemPio2fMultiple(x);
        n = (int)fn;
        y = x - fn * Pio2_1 - fn * Pio2_1t;
        return (float)TanD(y, (n & 1) != 0);
    }

    // __rem_pio2f for |x| ~< 2^28*(pi/2), medium size: the multiple of pi/2
    // whose remainder x - fn*pio2_1 - fn*pio2_1t lies in [-pi/4, pi/4].
    // 25+53 bit pi is good enough for medium size.
    private static double RemPio2fMultiple(float x)
    {
        /* Use a specialized rint() to get fn. */
        var fn = (double)x * InvPio2 + ToInt - ToInt;
        var y = x - fn * Pio2_1 - fn * Pio2_1t;
        /* Matters with directed rounding. */
        if (y < -Pio4)
            fn--;
        else if (y > Pio4)
            fn++;
        return fn;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float SinHuge(float x)
    {
        var n = Math.RemPio2fHuge(x, out var y);
        switch (n & 3)
        {
            case 0: return (float)SinD(y);
            case 1: return (float)CosD(y);
            case 2: return (float)SinD(-y);
            default:
                return (float)-CosD(y);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float CosHuge(float x)
    {
        var n = Math.RemPio2fHuge(x, out var y);
        switch (n & 3)
        {
            case 0: return (float)CosD(y);
            case 1: return (float)SinD(-y);
            case 2: return (float)-CosD(y);
            default:
                return (float)SinD(y);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float TanHuge(float x)
    {
        var n = Math.RemPio2fHuge(x, out var y);
        return (float)TanD(y, (n & 1) != 0);
    }

    // __sindf, __cosdf and __tandf return float in C; the double here is the
    // same value before the caller's one rounding to float.
    private static double SinD(double x)
    {
        double r, s, w, z;

        /* Try to optimize for parallel evaluation as in __tandf.c. */
        z = x * x;
        w = z * z;
        r = S3 + z * S4;
        s = z * x;
        return (x + s * (S1 + z * S2)) + s * w * r;
    }

    private static double CosD(double x)
    {
        double r, w, z;

        /* Try to optimize for parallel evaluation as in __tandf.c. */
        z = x * x;
        w = z * z;
        r = C2 + z * C3;
        return ((1.0 + z * C0) + w * C1) + (w * z) * r;
    }

    private static double TanD(double x, bool odd)
    {
        double z, r, w, s, t, u;

        z = x * x;
        /*
         * Split up the polynomial into small independent terms to give
         * opportunities for parallel evaluation.  The chosen splitting is
         * micro-optimized for Athlons (XP, X64).  It costs 2 multiplications
         * relative to Horner's method on sequential machines.
         *
         * We add the small terms from lowest degree up for efficiency on
         * non-sequential machines (the lowest degree terms tend to be ready
         * earlier).  Apart from this, we don't care about order of
         * operations, and don't need to to care since we have precision to
         * spare.  However, the chosen splitting is good for accuracy too,
         * and would give results as accurate as Horner's method if the
         * small terms were added from highest degree down.
         */
        r = T4 + z * T5;
        t = T2 + z * T3;
        w = z * z;
        s = z * x;
        u = T0 + z * T1;
        r = (x + s * u) + (s * w) * (t + w * r);
        return odd ? -1.0 / r : r;
    }
}
