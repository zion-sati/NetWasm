// Managed port of Sun fdlibm e_rem_pio2.c. Upstream: biosbits/fdlibm
// commit dfd9eaed985332a1c3af98c2bab4004eea217d3d.
// Adaptations: C# bit access, immutable tables (npio2_hw as constant data), the
// medium-size reduction written into FdTrigReduced with plain locals, and the
// large-argument path in its own method that only the functions' cold paths
// call, so that the common paths need no value frame.
/* @(#)e_rem_pio2.c 1.4 95/01/18 */
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

/* __ieee754_rem_pio2(x,y)
 *
 * return the remainder of x rem pi/2 in y0+y1
 * use __kernel_rem_pio2()
 */


/*
 * Table of constants for 2/pi, 396 Hex digits (476 decimal) of 2/pi
 */




/*
 * invpio2:  53 bits of 2/pi
 * pio2_1:   first  33 bit of pi/2
 * pio2_1t:  pi/2 - pio2_1
 * pio2_2:   second 33 bit of pi/2
 * pio2_2t:  pi/2 - (pio2_1+pio2_2)
 * pio2_3:   third  33 bit of pi/2
 * pio2_3t:  pi/2 - (pio2_1+pio2_2+pio2_3)
 */

/* 0x397B839A, 0x252049C1 */

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace System;

public static partial class Math
{
    // fdlibm's sin, cos and tan for pi/4 < |x| ~<= 2^19*(pi/2): e_rem_pio2's
    // reduction to y0 + y1 in [-pi/4, pi/4] and n, then the kernel for n mod 4.
    // kind is 0 for sin, 1 for cos and 2 for tan. The reduction is written
    // here with plain locals: returning its three
    // values through out parameters or a tuple keeps them in a value frame that
    // every call allocates.
    // Kind 0, 1 and 2 return sin, cos and tan in lane 0; kind 3 returns sin and
    // cos, from the one reduction, in lanes 0 and 1.
    private static Vector128<double> FdTrigReduced(double x, int kind)
    {
        double y0, y1;
        const double half = 5.00000000000000000000e-01;
        const double invpio2 = 6.36619772367581382433e-01;
        const double pio2_1 = 1.57079632673412561417e+00;
        const double pio2_1t = 6.07710050650619224932e-11;
        const double pio2_2 = 6.07710050630396597660e-11;
        const double pio2_2t = 2.02226624879595063154e-21;
        const double pio2_3 = 2.02226624871116645580e-21;
        const double pio2_3t = 8.47842766036889956997e-32;

        var z = 0.0;
        var w = 0.0;
        var t = 0.0;
        var r = 0.0;
        var fn = 0.0;
        var i = 0;
        var j = 0;
        var n = 0;
        var ix = 0;
        var hx = 0;

        hx = FdHigh(x);     /* high word of x */
        ix = hx & 0x7fffffff;
        if (ix <= 0x3fe921fb)   /* |x| ~<= pi/4 , no need for reduction */
        { n = 0; y0 = x; y1 = 0; goto reduced; }
        if (ix < 0x4002d97c)
        {  /* |x| < 3pi/4, special case with n=+-1 */
            if (hx > 0)
            {
                z = x - pio2_1;
                if (ix != 0x3ff921fb)
                {   /* 33+53 bit pi is good enough */
                    y0 = z - pio2_1t;
                    y1 = (z - y0) - pio2_1t;
                }
                else
                {       /* near pi/2, use 33+33+53 bit pi */
                    z -= pio2_2;
                    y0 = z - pio2_2t;
                    y1 = (z - y0) - pio2_2t;
                }
                n = 1;
                goto reduced;
            }
            else
            {   /* negative x */
                z = x + pio2_1;
                if (ix != 0x3ff921fb)
                {   /* 33+53 bit pi is good enough */
                    y0 = z + pio2_1t;
                    y1 = (z - y0) + pio2_1t;
                }
                else
                {       /* near pi/2, use 33+33+53 bit pi */
                    z += pio2_2;
                    y0 = z + pio2_2t;
                    y1 = (z - y0) + pio2_2t;
                }
                n = -1;
                goto reduced;
            }
        }
        if (ix <= 0x413921fb)
        { /* |x| ~<= 2^19*(pi/2), medium size */
            t = Abs(x);
            n = (int)(t * invpio2 + half);
            fn = (double)n;
            r = t - fn * pio2_1;
            w = fn * pio2_1t;   /* 1st round good to 85 bit */
            if (n < 32 && ix != Unsafe.Add(ref MemoryMarshal.GetReference(FdNpio2Hw), n - 1))
            {
                y0 = r - w;   /* quick check no cancellation */
            }
            else
            {
                j = ix >> 20;
                y0 = r - w;
                i = j - (((FdHigh(y0)) >> 20) & 0x7ff);
                if (i > 16)
                {  /* 2nd iteration needed, good to 118 */
                    t = r;
                    w = fn * pio2_2;
                    r = t - w;
                    w = fn * pio2_2t - ((t - r) - w);
                    y0 = r - w;
                    i = j - (((FdHigh(y0)) >> 20) & 0x7ff);
                    if (i > 49)
                    {   /* 3rd iteration need, 151 bits acc */
                        t = r;  /* will cover all possible cases */
                        w = fn * pio2_3;
                        r = t - w;
                        w = fn * pio2_3t - ((t - r) - w);
                        y0 = r - w;
                    }
                }
            }
            y1 = (r - y0) - w;
            if (hx < 0) { n = -n; y0 = -y0; y1 = -y1; }
            goto reduced;
        }
        /*
         * all other (large) arguments: the callers reduce these through
         * FdRemPio2Large themselves and handle inf and NaN first, so this is
         * not reached.
         */
        n = 0;
        y0 = y1 = x - x;
    reduced:
        return kind switch
        {
            0 => Vector128.CreateScalarUnsafe((n & 3) switch
            {
                0 => FdKernelSin(y0, y1, 1),
                1 => FdKernelCos(y0, y1),
                2 => -FdKernelSin(y0, y1, 1),
                _ => -FdKernelCos(y0, y1),
            }),
            1 => Vector128.CreateScalarUnsafe((n & 3) switch
            {
                0 => FdKernelCos(y0, y1),
                1 => -FdKernelSin(y0, y1, 1),
                2 => -FdKernelCos(y0, y1),
                _ => FdKernelSin(y0, y1, 1),
            }),
            2 => Vector128.CreateScalarUnsafe(FdKernelTan(y0, y1, 1 - ((n & 1) << 1))), /* 1 -- n even, -1 -- n odd */
            _ => FdSinCosQuadrant(n, y0, y1),
        };
    }

    // musl's sincos: sin and cos of y0 + y1, rotated by the quadrant n.
    private static Vector128<double> FdSinCosQuadrant(int n, double y0, double y1)
    {
        var s = FdKernelSin(y0, y1, 1);
        var c = FdKernelCos(y0, y1);
        switch (n & 3)
        {
            case 0: return Vector128.Create(s, c);
            case 1: return Vector128.Create(c, -s);
            case 2: return Vector128.Create(-s, -c);
            default:
                return Vector128.Create(-c, s);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int FdRemPio2Large(double x, int ix, int hx, out double y0, out double y1)
    {
        const double zero = 0.00000000000000000000e+00;
        const double two24 = 1.67772160000000000000e+07;

        var z = 0.0;
        Span<double> tx = stackalloc double[3];
        Span<double> y = stackalloc double[2];
        var e0 = 0;
        var i = 0;
        var nx = 0;
        var n = 0;

        /* set z = ScaleB(|x|,ilogb(x)-23) */
        z = FdWithLow(z, FdLow(x));
        e0 = (ix >> 20) - 1046; /* e0 = ilogb(z)-23; */
        z = FdWithHigh(z, ix - (e0 << 20));
        for (i = 0; i < 2; i++)
        {
            tx[i] = (double)((int)(z));
            z = (z - tx[i]) * two24;
        }
        tx[2] = z;
        nx = 3;
        while (tx[nx - 1] == zero) nx--;    /* skip zero term */
        n = FdKernelRemPio2(tx, y, e0, nx, 2, TwoOverPi);
        if (hx < 0) { y0 = -y[0]; y1 = -y[1]; return -n; }
        y0 = y[0]; y1 = y[1];
        return n;
    }

    // 2/pi in base-2^24 digits, shared by the double and float reductions.
    private static ReadOnlySpan<int> TwoOverPi => [

        0xA2F983, 0x6E4E44, 0x1529FC, 0x2757D1, 0xF534DD, 0xC0DB62,
    0x95993C, 0x439041, 0xFE5163, 0xABDEBB, 0xC561B7, 0x246E3A,
    0x424DD2, 0xE00649, 0x2EEA09, 0xD1921C, 0xFE1DEB, 0x1CB129,
    0xA73EE8, 0x8235F5, 0x2EBB44, 0x84E99C, 0x7026B4, 0x5F7E41,
    0x3991D6, 0x398353, 0x39F49C, 0x845F8B, 0xBDF928, 0x3B1FF8,
    0x97FFDE, 0x05980F, 0xEF2F11, 0x8B5A0A, 0x6D1F6D, 0x367ECF,
    0x27CB09, 0xB74F46, 0x3F669E, 0x5FEA2D, 0x7527BA, 0xC7EBE5,
    0xF17B3D, 0x0739F7, 0x8A5292, 0xEA6BFB, 0x5FB11F, 0x8D5D08,
    0x560330, 0x46FC7B, 0x6BABF0, 0xCFBC20, 0x9AF436, 0x1DA9E3,
    0x91615E, 0xE61B08, 0x659985, 0x5F14A0, 0x68408D, 0xFFD880,
    0x4D7327, 0x310606, 0x1556CA, 0x73A8C9, 0x60E27B, 0xC08C6B,
    ];

    // __rem_pio2f's huge-argument path for MathF: x scaled into [2^23, 2^24-1]
    // and reduced to 24 bits by the same Payne-Hanek kernel. Its own method, so
    // that the scratch stays out of the float functions' common paths.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int RemPio2fHuge(float x, out double y)
    {
        var u = BitConverter.SingleToUInt32Bits(x);
        var ix = u & 0x7fffffff;
        var sign = u >> 31;
        /* scale x into [2^23, 2^24-1] */
        var e0 = (int)(ix >> 23) - (0x7f + 23);  /* e0 = ilogb(|x|)-23, positive */
        Span<double> tx = stackalloc double[1];
        Span<double> ty = stackalloc double[1];
        tx[0] = BitConverter.UInt32BitsToSingle(ix - ((uint)e0 << 23));
        var n = FdKernelRemPio2(tx, ty, e0, 1, 0, TwoOverPi);
        if (sign != 0)
        {
            y = -ty[0];
            return -n;
        }
        y = ty[0];
        return n;
    }

    // High words of n*pi/2 for n in [1, 32], read at n - 1 without bounds checks.
    private static ReadOnlySpan<int> FdNpio2Hw =>
    [
        0x3FF921FB, 0x400921FB, 0x4012D97C, 0x401921FB, 0x401F6A7A, 0x4022D97C,
        0x4025FDBB, 0x402921FB, 0x402C463A, 0x402F6A7A, 0x4031475C, 0x4032D97C,
        0x40346B9C, 0x4035FDBB, 0x40378FDB, 0x403921FB, 0x403AB41B, 0x403C463A,
        0x403DD85A, 0x403F6A7A, 0x40407E4C, 0x4041475C, 0x4042106C, 0x4042D97C,
        0x4043A28C, 0x40446B9C, 0x404534AC, 0x4045FDBB, 0x4046C6CB, 0x40478FDB,
        0x404858EB, 0x404921FB,
    ];
}
