// Managed port of musl's expf.c, logf.c, log2f.c and powf.c with their data
// (exp2f_data.c, logf_data.c, log2f_data.c, powf_data.c), Arm's optimized
// routines. Upstream: musl v1.2.5, commit 0784374d561435f7c787a555aeab8ede699ed298.
// Adaptations: C# bit access, hexadecimal constants written as their exact
// decimal values, each table as constant data read without bounds checks at
// the masked index, TOINT_INTRINSICS off (so POWF_SCALE is 1 and powf's
// log2 table is log2f's), WANT_SNAN off as in musl, and the overflow, underflow,
// divide-by-zero and invalid helpers as the expressions they return.
/*
 * Single-precision e^x, log, log2 and pow functions and their data.
 *
 * Copyright (c) 2017-2018, Arm Limited.
 * SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System;

public static partial class MathF
{
    private const int Exp2fTableBits = 5;
    private const ulong Exp2fTableMask = (1 << Exp2fTableBits) - 1;
    // exp2f_data: shift, invln2_scaled and poly_scaled for expf, shift_scaled
    // and poly for powf's exp2.
    private const double Exp2fShift = 6755399441055744.0; // 0x1.8p+52
    private const double Exp2fShiftScaled = 211106232532992.0; // 0x1.8p+52 / N
    private const double Exp2fInvLn2Scaled = 46.16624130844683; // 0x1.71547652b82fep+0 * N
    private const double Exp2fC0 = 0.05550361559341535; // 0x1.c6af84b912394p-5
    private const double Exp2fC1 = 0.2402284522445722; // 0x1.ebfce50fac4f3p-3
    private const double Exp2fC2 = 0.6931471806916203; // 0x1.62e42ff0c52d6p-1
    private const double Exp2fScaledC0 = 1.6938359250920212e-06; // 0x1.c6af84b912394p-5/N/N/N
    private const double Exp2fScaledC1 = 0.00023459809789509004; // 0x1.ebfce50fac4f3p-3/N/N
    private const double Exp2fScaledC2 = 0.021660849396613134; // 0x1.62e42ff0c52d6p-1/N

    private const int LogfTableBits = 4;
    private const uint LogfTableMask = (1 << LogfTableBits) - 1;
    private const uint LogfOff = 0x3f330000;

    // tab[i] = uint(2^(i/N)) - (i << 52-BITS)
    // used for computing 2^(k/N) for an int |k| < 150 N as
    // double(tab[k%N] + (k << 52-BITS))
    private static ReadOnlySpan<ulong> Exp2fTable =>
    [
        0x3ff0000000000000, 0x3fefd9b0d3158574, 0x3fefb5586cf9890f, 0x3fef9301d0125b51,
        0x3fef72b83c7d517b, 0x3fef54873168b9aa, 0x3fef387a6e756238, 0x3fef1e9df51fdee1,
        0x3fef06fe0a31b715, 0x3feef1a7373aa9cb, 0x3feedea64c123422, 0x3feece086061892d,
        0x3feebfdad5362a27, 0x3feeb42b569d4f82, 0x3feeab07dd485429, 0x3feea47eb03a5585,
        0x3feea09e667f3bcd, 0x3fee9f75e8ec5f74, 0x3feea11473eb0187, 0x3feea589994cce13,
        0x3feeace5422aa0db, 0x3feeb737b0cdc5e5, 0x3feec49182a3f090, 0x3feed503b23e255d,
        0x3feee89f995ad3ad, 0x3feeff76f2fb5e47, 0x3fef199bdd85529c, 0x3fef3720dcef9069,
        0x3fef5818dcfba487, 0x3fef7c97337b9b5f, 0x3fefa4afa2a490da, 0x3fefd0765b6e4540,
    ];

    // logf_data: invc, logc pairs.
    private static ReadOnlySpan<double> LogfTable =>
    [
        1.398907162146528, -0.33569133332882284,
        1.3403141896637998, -0.2929040563774074,
        1.286432210124115, -0.2518726580937369,
        1.2367150214269895, -0.21245868807117255,
        1.1906977166711752, -0.17453945183745634,
        1.1479821020556429, -0.1380057072319758,
        1.1082251448272158, -0.10275976698545139,
        1.0711297413057381, -0.06871392447020525,
        1.036437278977283, -0.0357891387398228,
        1.0, 0.0,
        0.9492859795739057, 0.05204517742929496,
        0.8951049428609004, 0.11081431298787942,
        0.8476821620351103, 0.1652495223695143,
        0.8050314851692001, 0.21687389031699977,
        0.7664671008843108, 0.2659635028121397,
        0.731428603316328, 0.3127556664073557,
    ];

    // log2f_data and powf_log2_data (equal while POWF_SCALE is 1): invc, logc pairs.
    private static ReadOnlySpan<double> Log2fTable =>
    [
        1.398907162146528, -0.48430022186289673,
        1.3403141896637998, -0.42257122959194704,
        1.286432210124115, -0.3633754347673556,
        1.2367150214269895, -0.30651309567405577,
        1.1906977166711752, -0.25180720160537634,
        1.1479821020556429, -0.19910014943794563,
        1.1082251448272158, -0.14825100623281615,
        1.0711297413057381, -0.09913323807318392,
        1.036437278977283, -0.051632812977629436,
        1.0, 0.0,
        0.9492859795739057, 0.07508531937943004,
        0.8951049428609004, 0.15987125980713107,
        0.8476821620351103, 0.2384046664317681,
        0.8050314851692001, 0.31288288605863257,
        0.7664671008843108, 0.38370422656453185,
        0.731428603316328, 0.451211048935815,
    ];

    public static float Exp(float x)
    {
        uint abstop;
        ulong ki, t;
        double kd, xd, z, r, r2, y, s;

        xd = x;
        abstop = Top12(x) & 0x7ff;
        if (abstop >= 0x42b) /* top12(88.0f) */
        {   /* |x| >= 88 or x is nan.  */
            if (BitConverter.SingleToUInt32Bits(x) == 0xff800000) /* -INFINITY */
                return 0.0f;
            if (abstop >= 0x7f8) /* top12(INFINITY) */
                return x + x;
            if (x > 88.72283f) /* x > log(0x1p128) ~= 88.72 */
                return OverflowF(false);
            if (x < -103.97208f) /* x < log(0x1p-150) ~= -103.97 */
                return UnderflowF(false);
        }

        /* x*N/Ln2 = k + r with r in [-1/2, 1/2] and int k.  */
        z = Exp2fInvLn2Scaled * xd;

        /* Round and convert z to int, the result is in [-150*N, 128*N] and
           ideally ties-to-even rule is used, otherwise the magnitude of r
           can be bigger which gives larger approximation error.  */
        kd = z + Exp2fShift;
        ki = BitConverter.DoubleToUInt64Bits(kd);
        kd -= Exp2fShift;
        r = z - kd;

        /* exp(x) = 2^(k/N) * 2^(r/N) ~= s * (C0*r^3 + C1*r^2 + C2*r + 1) */
        t = Exp2fEntry(ki);
        t += ki << (52 - Exp2fTableBits);
        s = BitConverter.UInt64BitsToDouble(t);
        z = Exp2fScaledC0 * r + Exp2fScaledC1;
        r2 = r * r;
        y = Exp2fScaledC2 * r + 1;
        y = z * r2 + y;
        y = y * s;
        return (float)y;
    }

    public static float Log(float x)
    {
        const double ln2 = 0.6931471805599453; // 0x1.62e42fefa39efp-1
        const double A0 = -0.25089342214237154; // -0x1.00ea348b88334p-2
        const double A1 = 0.333456765744066; // 0x1.5575b0be00b6ap-2
        const double A2 = -0.4999997485802103; // -0x1.ffffef20a4123p-2

        double z, r, r2, y, y0, invc, logc;
        uint ix, iz, tmp;
        int k;

        ix = BitConverter.SingleToUInt32Bits(x);
        /* Fix sign of zero with downward rounding when x==1.  */
        if (ix == 0x3f800000)
            return 0;
        if (ix - 0x00800000 >= 0x7f800000 - 0x00800000)
        {   /* x < 0x1p-126 or inf or nan.  */
            if (ix * 2 == 0)
                return DivideByZeroF(true);
            if (ix == 0x7f800000) /* log(inf) == inf.  */
                return x;
            if ((ix & 0x80000000) != 0 || ix * 2 >= 0xff000000)
                return InvalidF(x);
            /* x is subnormal, normalize it.  */
            ix = BitConverter.SingleToUInt32Bits(x * 8388608f /* 0x1p23f */);
            ix -= 23 << 23;
        }

        /* x = 2^k z; where z is in range [OFF,2*OFF] and exact.
           The range is split into N subintervals.
           The ith subinterval contains z and c is near its center.  */
        tmp = ix - LogfOff;
        k = (int)tmp >> 23; /* arithmetic shift */
        iz = ix - (tmp & 0xff800000);
        /* The index is masked below the table's N entries.  */
        ref var entry = ref Unsafe.Add(ref MemoryMarshal.GetReference(LogfTable),
            (nint)(2 * ((tmp >> (23 - LogfTableBits)) & LogfTableMask)));
        invc = entry;
        logc = Unsafe.Add(ref entry, 1);
        z = BitConverter.UInt32BitsToSingle(iz);

        /* log(x) = log1p(z/c-1) + log(c) + k*Ln2 */
        r = z * invc - 1;
        y0 = logc + (double)k * ln2;

        /* Pipelined polynomial evaluation to approximate log1p(r).  */
        r2 = r * r;
        y = A1 * r + A2;
        y = A0 * r2 + y;
        y = y * r2 + (y0 + r);
        return (float)y;
    }

    public static float Log2(float x)
    {
        const double A0 = -0.36051725506874704; // -0x1.712b6f70a7e4dp-2
        const double A1 = 0.4811247078767291; // 0x1.ecabf496832ep-2
        const double A2 = -0.7213476299867769; // -0x1.715479ffae3dep-1
        const double A3 = 1.4426950186867042; // 0x1.715475f35c8b8p0

        double z, r, r2, p, y, y0, invc, logc;
        uint ix, iz, top, tmp;
        int k;

        ix = BitConverter.SingleToUInt32Bits(x);
        /* Fix sign of zero with downward rounding when x==1.  */
        if (ix == 0x3f800000)
            return 0;
        if (ix - 0x00800000 >= 0x7f800000 - 0x00800000)
        {   /* x < 0x1p-126 or inf or nan.  */
            if (ix * 2 == 0)
                return DivideByZeroF(true);
            if (ix == 0x7f800000) /* log2(inf) == inf.  */
                return x;
            if ((ix & 0x80000000) != 0 || ix * 2 >= 0xff000000)
                return InvalidF(x);
            /* x is subnormal, normalize it.  */
            ix = BitConverter.SingleToUInt32Bits(x * 8388608f /* 0x1p23f */);
            ix -= 23 << 23;
        }

        /* x = 2^k z; where z is in range [OFF,2*OFF] and exact.
           The range is split into N subintervals.
           The ith subinterval contains z and c is near its center.  */
        tmp = ix - LogfOff;
        top = tmp & 0xff800000;
        iz = ix - top;
        k = (int)tmp >> 23; /* arithmetic shift */
        /* The index is masked below the table's N entries.  */
        ref var entry = ref Unsafe.Add(ref MemoryMarshal.GetReference(Log2fTable),
            (nint)(2 * ((tmp >> (23 - LogfTableBits)) & LogfTableMask)));
        invc = entry;
        logc = Unsafe.Add(ref entry, 1);
        z = BitConverter.UInt32BitsToSingle(iz);

        /* log2(x) = log1p(z/c-1)/ln2 + log2(c) + k */
        r = z * invc - 1;
        y0 = logc + (double)k;

        /* Pipelined polynomial evaluation to approximate log1p(r)/ln2.  */
        r2 = r * r;
        y = A1 * r + A2;
        y = A0 * r2 + y;
        p = A3 * r + y0;
        y = y * r2 + p;
        return (float)y;
    }

    public static float Pow(float x, float y)
    {
        const uint signBias = 1 << (Exp2fTableBits + 11);
        uint sign = 0;
        uint ix, iy;

        ix = BitConverter.SingleToUInt32Bits(x);
        iy = BitConverter.SingleToUInt32Bits(y);
        if (ix - 0x00800000 >= 0x7f800000 - 0x00800000 || ZeroInfNan(iy))
        {
            /* Either (x < 0x1p-126 or inf or nan) or (y is 0 or inf or nan).  */
            if (ZeroInfNan(iy))
            {
                if (2 * iy == 0)
                    return 1.0f;
                if (ix == 0x3f800000)
                    return 1.0f;
                if (2 * ix > 2u * 0x7f800000 || 2 * iy > 2u * 0x7f800000)
                    return x + y;
                if (2 * ix == 2 * 0x3f800000)
                    return 1.0f;
                if ((2 * ix < 2 * 0x3f800000) == ((iy & 0x80000000) == 0))
                    return 0.0f; /* |x|<1 && y==inf or |x|>1 && y==-inf.  */
                return y * y;
            }
            if (ZeroInfNan(ix))
            {
                float x2 = x * x;
                if ((ix & 0x80000000) != 0 && CheckInt(iy) == 1)
                    x2 = -x2;
                return (iy & 0x80000000) != 0 ? 1 / x2 : x2;
            }
            /* x and y are non-zero finite.  */
            if ((ix & 0x80000000) != 0)
            {
                /* Finite x < 0.  */
                int yint = CheckInt(iy);
                if (yint == 0)
                    return InvalidF(x);
                if (yint == 1)
                    sign = signBias;
                ix &= 0x7fffffff;
            }
            if (ix < 0x00800000)
            {
                /* Normalize subnormal x so exponent becomes negative.  */
                ix = BitConverter.SingleToUInt32Bits(x * 8388608f /* 0x1p23f */);
                ix &= 0x7fffffff;
                ix -= 23 << 23;
            }
        }

        // log2_inline(ix)
        const double A0 = 0.288457581109214; // 0x1.27616c9496e0bp-2
        const double A1 = -0.36092606229713164; // -0x1.71969a075c67ap-2
        const double A2 = 0.480898481472577; // 0x1.ec70a6ca7baddp-2
        const double A3 = -0.7213474675006291; // -0x1.7154748bef6c8p-1
        const double A4 = 1.4426950408774342; // 0x1.71547652ab82bp0
        double z, r, r2, r4, p, q, logx, y0, invc, logc;
        uint iz, top, tmp;
        int k;

        /* x = 2^k z; where z is in range [OFF,2*OFF] and exact.
           The range is split into N subintervals.
           The ith subinterval contains z and c is near its center.  */
        tmp = ix - LogfOff;
        top = tmp & 0xff800000;
        iz = ix - top;
        k = (int)top >> 23; /* arithmetic shift */
        /* The index is masked below the table's N entries.  */
        ref var entry = ref Unsafe.Add(ref MemoryMarshal.GetReference(Log2fTable),
            (nint)(2 * ((tmp >> (23 - LogfTableBits)) & LogfTableMask)));
        invc = entry;
        logc = Unsafe.Add(ref entry, 1);
        z = BitConverter.UInt32BitsToSingle(iz);

        /* log2(x) = log1p(z/c-1)/ln2 + log2(c) + k */
        r = z * invc - 1;
        y0 = logc + (double)k;

        /* Pipelined polynomial evaluation to approximate log1p(r)/ln2.  */
        r2 = r * r;
        logx = A0 * r + A1;
        p = A2 * r + A3;
        r4 = r2 * r2;
        q = A4 * r + y0;
        q = p * r2 + q;
        logx = logx * r4 + q;

        double ylogx = y * logx; /* cannot overflow, y is single prec.  */
        if (((BitConverter.DoubleToUInt64Bits(ylogx) >> 47) & 0xffff) >= 0x80bf /* asuint64(126.0) >> 47 */)
        {
            /* |y*log(x)| >= 126.  */
            if (ylogx > 127.99999995700433 /* 0x1.fffffffd1d571p+6 */)
                return OverflowF(sign != 0);
            if (ylogx <= -150.0)
                return UnderflowF(sign != 0);
        }

        // exp2_inline(ylogx, sign)
        ulong ki, ski, t;
        double kd, s;

        /* x = k/N + r with r in [-1/(2N), 1/(2N)] */
        kd = ylogx + Exp2fShiftScaled;
        ki = BitConverter.DoubleToUInt64Bits(kd);
        kd -= Exp2fShiftScaled; /* k/N */
        r = ylogx - kd;

        /* exp2(x) = 2^(k/N) * 2^r ~= s * (C0*r^3 + C1*r^2 + C2*r + 1) */
        t = Exp2fEntry(ki);
        ski = ki + sign;
        t += ski << (52 - Exp2fTableBits);
        s = BitConverter.UInt64BitsToDouble(t);
        z = Exp2fC0 * r + Exp2fC1;
        r2 = r * r;
        double result = Exp2fC2 * r + 1;
        result = z * r2 + result;
        result = result * s;
        return (float)result;
    }

    private static ulong Exp2fEntry(ulong ki) =>
        Unsafe.Add(ref MemoryMarshal.GetReference(Exp2fTable), (nint)(ki & Exp2fTableMask));

    /* Returns 0 if not int, 1 if odd int, 2 if even int.  The argument is
       the bit representation of a non-zero finite floating-point value.  */
    private static int CheckInt(uint iy)
    {
        int e = (int)(iy >> 23 & 0xff);
        if (e < 0x7f)
            return 0;
        if (e > 0x7f + 23)
            return 2;
        if ((iy & ((1u << (0x7f + 23 - e)) - 1)) != 0)
            return 0;
        if ((iy & (1u << (0x7f + 23 - e))) != 0)
            return 1;
        return 2;
    }

    private static bool ZeroInfNan(uint ix) => 2 * ix - 1 >= 2u * 0x7f800000 - 1;

    /* Top 12 bits of a float (sign, exponent and three mantissa bits).  */
    private static uint Top12(float x) => BitConverter.SingleToUInt32Bits(x) >> 20;

    // __math_oflowf, __math_uflowf, __math_divzerof and __math_invalidf:
    // +-0x1p97f * 0x1p97f, +-0x1p-95f * 0x1p-95f, +-1 / 0 and (x - x) / (x - x).
    private static float OverflowF(bool negative) => (negative ? -1.5845633e+29f : 1.5845633e+29f) * 1.5845633e+29f;
    private static float UnderflowF(bool negative) => (negative ? -2.524355e-29f : 2.524355e-29f) * 2.524355e-29f;
    private static float DivideByZeroF(bool negative) => (negative ? -1.0f : 1.0f) / 0.0f;
    private static float InvalidF(float x) => (x - x) / (x - x);
}
