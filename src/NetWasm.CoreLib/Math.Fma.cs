// Managed port of musl's fma.c and fmaf.c. Upstream: musl v1.2.5, commit
// 0784374d561435f7c787a555aeab8ede699ed298. fma.c is musl's own (MIT, see
// THIRD-PARTY-NOTICES); fmaf.c is FreeBSD msun's s_fmaf.c, notice below.
// WebAssembly has no scalar fused multiply-add, and these are the routines Mono
// links. Adaptations: C# bit access, hexadecimal constants written as their exact
// decimal values, the significand product through Math.BigMul, no
// floating-point status flags or rounding modes, which .NET does not expose,
// and a zero or non-finite addend answered as desktop's fused instruction does.
/*-
 * Copyright (c) 2005-2011 David Schultz <das@FreeBSD.ORG>
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions
 * are met:
 * 1. Redistributions of source code must retain the above copyright
 *    notice, this list of conditions and the following disclaimer.
 * 2. Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 *
 * THIS SOFTWARE IS PROVIDED BY THE AUTHOR AND CONTRIBUTORS ``AS IS'' AND
 * ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED.  IN NO EVENT SHALL THE AUTHOR OR CONTRIBUTORS BE LIABLE
 * FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
 * DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS
 * OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
 * HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
 * LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY
 * OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF
 * SUCH DAMAGE.
 */

using System.Numerics;

namespace System;

public static partial class Math
{
    private const int FmaZeroInfNaN = 0x7ff - 0x3ff - 52 - 1;

    /* normalize so top 10bits and last bit are 0 */
    private static ulong FmaNormalize(double x, out int exponent, out int sign)
    {
        var ix = BitConverter.DoubleToUInt64Bits(x);
        var e = (int)(ix >> 52);
        sign = e & 0x800;
        e &= 0x7ff;
        if (e == 0)
        {
            ix = BitConverter.DoubleToUInt64Bits(x * 9.223372036854776e+18); /* 0x1p63 */
            e = (int)(ix >> 52) & 0x7ff;
            e = e != 0 ? e - 63 : 0x800;
        }
        ix &= (1UL << 52) - 1;
        ix |= 1UL << 52;
        ix <<= 1;
        exponent = e - (0x3ff + 52 + 1);
        return ix;
    }

    public static double FusedMultiplyAdd(double left, double right, double addend)
    {
        var nxm = FmaNormalize(left, out var nxe, out var nxsign);
        var nym = FmaNormalize(right, out var nye, out var nysign);
        var nzm = FmaNormalize(addend, out var nze, out var nzsign);

        if (nxe >= FmaZeroInfNaN || nye >= FmaZeroInfNaN)
            return left * right + addend;
        if (nze >= FmaZeroInfNaN)
        {
            /* NetWasm: a product that underflows keeps its sign, and a
               signaling NaN addend comes back quiet, as a fused instruction
               gives them. */
            if (nze > FmaZeroInfNaN) /* z==0 */
                return left * right;
            return addend + 0.0;
        }

        /* mul: r = x*y */
        ulong zhi, zlo;
        var rhi = BigMul(nxm, nym, out var rlo);
        /* either top 20 or 21 bits of rhi and last 2 bits of rlo are 0 */

        /* align exponents */
        var e = nxe + nye;
        var d = nze - e;
        /* shift bits z<<=kz, r>>=kr, so kz+kr == d, set e = e+kr (== ez-kz) */
        if (d > 0)
        {
            if (d < 64)
            {
                zlo = nzm << d;
                zhi = nzm >> (64 - d);
            }
            else
            {
                zlo = 0;
                zhi = nzm;
                e = nze - 64;
                d -= 64;
                if (d == 0)
                {
                }
                else if (d < 64)
                {
                    rlo = rhi << (64 - d) | rlo >> d | ((rlo << (64 - d)) != 0 ? 1UL : 0UL);
                    rhi >>= d;
                }
                else
                {
                    rlo = 1;
                    rhi = 0;
                }
            }
        }
        else
        {
            zhi = 0;
            d = -d;
            if (d == 0)
            {
                zlo = nzm;
            }
            else if (d < 64)
            {
                zlo = nzm >> d | ((nzm << (64 - d)) != 0 ? 1UL : 0UL);
            }
            else
            {
                zlo = 1;
            }
        }

        /* add */
        var sign = nxsign ^ nysign;
        var samesign = (sign ^ nzsign) == 0;
        var nonzero = true;
        if (samesign)
        {
            /* r += z */
            rlo += zlo;
            rhi += zhi + (rlo < zlo ? 1UL : 0UL);
        }
        else
        {
            /* r -= z */
            var t = rlo;
            rlo -= zlo;
            rhi = rhi - zhi - (t < rlo ? 1UL : 0UL);
            if ((rhi >> 63) != 0)
            {
                rlo = 0 - rlo;
                rhi = 0 - rhi - (rlo != 0 ? 1UL : 0UL);
                sign = sign == 0 ? 1 : 0;
            }
            nonzero = rhi != 0;
        }

        /* set rhi to top 63bit of the result (last bit is sticky) */
        if (nonzero)
        {
            e += 64;
            d = BitOperations.LeadingZeroCount(rhi) - 1;
            /* note: d > 0 */
            rhi = rhi << d | rlo >> (64 - d) | ((rlo << d) != 0 ? 1UL : 0UL);
        }
        else if (rlo != 0)
        {
            d = BitOperations.LeadingZeroCount(rlo) - 1;
            if (d < 0)
                rhi = rlo >> 1 | (rlo & 1);
            else
                rhi = rlo << d;
        }
        else
        {
            /* exact +-0 */
            return left * right + addend;
        }
        e -= d;

        /* convert to double */
        var i = (long)rhi; /* i is in [1<<62,(1<<63)-1] */
        if (sign != 0)
            i = -i;
        double r = i; /* |r| is in [0x1p62,0x1p63] */

        if (e < -1022 - 62)
        {
            /* result is subnormal before rounding */
            if (e == -1022 - 63)
            {
                var c = 9.223372036854776e+18; /* 0x1p63 */
                if (sign != 0)
                    c = -c;
                if (r == c)
                {
                    /* min normal after rounding, underflow depends
                       on arch behaviour which can be imitated by
                       a double to float conversion */
                    var fltmin = (float)(1.0842021401737618e-19 /* 0x0.ffffff8p-63 */ * 1.1754943508222875e-38 /* FLT_MIN */ * r);
                    return 1.8928834978668395e-270 /* DBL_MIN/FLT_MIN */ * fltmin;
                }
                /* one bit is lost when scaled, add another top bit to
                   only round once at conversion if it is inexact */
                if ((rhi << 53) != 0)
                {
                    i = (long)(rhi >> 1 | (rhi & 1) | 1UL << 62);
                    if (sign != 0)
                        i = -i;
                    r = i;
                    r = 2 * r - c; /* remove top bit */
                }
            }
            else
            {
                /* only round once when scaled */
                d = 10;
                i = (long)((rhi >> d | ((rhi << (64 - d)) != 0 ? 1UL : 0UL)) << d);
                if (sign != 0)
                    i = -i;
                r = i;
            }
        }
        return ScaleB(r, e);
    }
}

public static partial class MathF
{
    /*
     * Fused multiply-add: Compute x * y + z with a single rounding error.
     *
     * A double has more than twice as much precision than a float, so
     * direct double-precision arithmetic suffices, except where double
     * rounding occurs.
     */
    public static float FusedMultiplyAdd(float x, float y, float z)
    {
        var xy = (double)x * y;
        var result = xy + z;
        var ui = BitConverter.DoubleToUInt64Bits(result);
        var e = (int)(ui >> 52) & 0x7ff;
        /* Common case: The double precision result is fine. */
        if ((ui & 0x1fffffff) != 0x10000000 || /* not a halfway case */
            e == 0x7ff ||                       /* NaN */
            (result - xy == z && result - z == xy)) /* exact */
        {
            return (float)result;
        }

        /*
         * If result is inexact, and exactly halfway between two float values,
         * we need to adjust the low-order bit in the direction of the error.
         */
        double err;
        var neg = (ui >> 63) != 0;
        if (neg == (z > xy))
            err = xy - result + z;
        else
            err = z - result + xy;
        if (neg == (err < 0))
            ui++;
        else
            ui--;
        return (float)BitConverter.UInt64BitsToDouble(ui);
    }
}
