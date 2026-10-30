// Managed port of musl's pow.c and pow_data.c, Arm's optimized routines.
// Upstream: musl v1.2.5, commit 0784374d561435f7c787a555aeab8ede699ed298.
// Adaptations: C# bit access, hexadecimal constants written as their exact
// decimal values (the scaled coefficients as their exact products), the log
// table as constant data read without bounds checks at the masked index, the
// earlier pow's direct answers for y of 2, 1, -1 and 0.5, the non-fma
// evaluation, log_inline written into pow (an out parameter for its tail would
// keep that local in memory), and the exp kernel sharing Math.Exp.cs's
// constants, table and special case.
/*
 * Double-precision x^y function.
 *
 * Copyright (c) 2018, Arm Limited.
 * SPDX-License-Identifier: MIT
 */

/*
Worst-case error: 0.54 ULP (~= ulperr_exp + 1024*Ln2*relerr_log*2^53)
relerr_log: 1.3 * 2^-68 (Relative error of log, 1.5 * 2^-68 without fma)
ulperr_exp: 0.509 ULP (ULP error of exp, 0.511 ULP without fma)
*/

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System;

public static partial class Math
{
    private const int PowLogTableBits = 7;
    private const ulong PowLogOff = 0x3fe6955500000000;
    private const uint PowSignBias = 0x800 << ExpTableBits;

    // invc, pad, logc and logctail of each of the N subintervals of
    // [0x1.69555p-1, 0x1.69555p0]: 1/c, log(c) rounded to 43 fractional bits,
    // and the rest of log(c).
    private static ReadOnlySpan<double> PowLogTable =>
    [
        1.4140625, 0, -0.3464667673462145, 5.929407345889625e-15,
        1.40625, 0, -0.34092658697056777, -2.544157440035963e-14,
        1.3984375, 0, -0.3353555419211034, -3.443525940775045e-14,
        1.390625, 0, -0.3297532863724655, -2.500123826022799e-15,
        1.3828125, 0, -0.32411946865420305, -8.929337133850617e-15,
        1.375, 0, -0.31845373111855224, 1.7625431312172662e-14,
        1.3671875, 0, -0.31275571000389846, 1.5688303180062087e-15,
        1.359375, 0, -0.3070250352949415, 2.9655274673691784e-14,
        1.3515625, 0, -0.3012613305781997, 3.7923164802093147e-14,
        1.34375, 0, -0.2954642128938758, 3.993416384387844e-14,
        1.3359375, 0, -0.28963329258306203, 1.9352855826489123e-14,
        1.3359375, 0, -0.28963329258306203, 1.9352855826489123e-14,
        1.328125, 0, -0.28376817313062475, -1.9852665484979036e-14,
        1.3203125, 0, -0.27786845100342816, -2.814323765595281e-14,
        1.3125, 0, -0.2719337154836694, 2.7643769993528702e-14,
        1.3046875, 0, -0.2659635484970977, -4.025092402293806e-14,
        1.296875, 0, -0.25995752443691345, -1.2621729398885316e-14,
        1.2890625, 0, -0.25391520998095984, -3.600176732637335e-15,
        1.2890625, 0, -0.25391520998095984, -3.600176732637335e-15,
        1.28125, 0, -0.2478361639045943, 1.3029797173308663e-14,
        1.2734375, 0, -0.2417199368871934, 4.8230289429940886e-14,
        1.265625, 0, -0.23556607131274632, -2.0592242769647135e-14,
        1.2578125, 0, -0.22937410106487732, 3.149265065191484e-14,
        1.25, 0, -0.22314355131425145, 4.169796584527195e-14,
        1.25, 0, -0.22314355131425145, 4.169796584527195e-14,
        1.2421875, 0, -0.21687393830063684, 2.2477465222466186e-14,
        1.234375, 0, -0.21056476910735, 3.6507188831790577e-16,
        1.2265625, 0, -0.2042155414286526, -3.827767260205414e-14,
        1.2265625, 0, -0.2042155414286526, -3.827767260205414e-14,
        1.21875, 0, -0.19782574332987224, -4.7641388950792196e-14,
        1.2109375, 0, -0.19139485299967873, 4.9278276214647115e-14,
        1.203125, 0, -0.18492233849406148, 4.9485167661250996e-14,
        1.203125, 0, -0.18492233849406148, 4.9485167661250996e-14,
        1.1953125, 0, -0.1784076574728033, -1.5003333854266542e-14,
        1.1875, 0, -0.17185025692663203, -2.7194441649495324e-14,
        1.1875, 0, -0.17185025692663203, -2.7194441649495324e-14,
        1.1796875, 0, -0.1652495728952772, -2.99659267292569e-14,
        1.171875, 0, -0.15860503017665906, 2.0472357800461955e-14,
        1.171875, 0, -0.15860503017665906, 2.0472357800461955e-14,
        1.1640625, 0, -0.15191604202584585, 3.879296723063646e-15,
        1.15625, 0, -0.1451820098444614, -3.6506824353335045e-14,
        1.1484375, 0, -0.13840232285906495, -5.4183331379008994e-14,
        1.1484375, 0, -0.13840232285906495, -5.4183331379008994e-14,
        1.140625, 0, -0.131576357788731, 1.1729485484531301e-14,
        1.140625, 0, -0.131576357788731, 1.1729485484531301e-14,
        1.1328125, 0, -0.12470347850091912, -3.811763084710266e-14,
        1.125, 0, -0.11778303565643, 4.654729747598445e-14,
        1.125, 0, -0.11778303565643, 4.654729747598445e-14,
        1.1171875, 0, -0.11081436634026431, -2.5799991283069902e-14,
        1.109375, 0, -0.10379679368168127, 3.7700471749674615e-14,
        1.109375, 0, -0.10379679368168127, 3.7700471749674615e-14,
        1.1015625, 0, -0.09672962645856842, 1.7306161136093256e-14,
        1.1015625, 0, -0.09672962645856842, 1.7306161136093256e-14,
        1.09375, 0, -0.089612158689647, -4.012913552726574e-14,
        1.0859375, 0, -0.08244366921110213, 2.7541708360737882e-14,
        1.0859375, 0, -0.08244366921110213, 2.7541708360737882e-14,
        1.078125, 0, -0.07522342123763792, 5.0396178134370583e-14,
        1.078125, 0, -0.07522342123763792, 5.0396178134370583e-14,
        1.0703125, 0, -0.06795066190852594, 1.8195060030168815e-14,
        1.0625, 0, -0.06062462181648698, 5.213620639136504e-14,
        1.0625, 0, -0.06062462181648698, 5.213620639136504e-14,
        1.0546875, 0, -0.053244514518837605, 2.532168943117445e-14,
        1.0546875, 0, -0.053244514518837605, 2.532168943117445e-14,
        1.046875, 0, -0.045809536031242715, -5.148849572685811e-14,
        1.046875, 0, -0.045809536031242715, -5.148849572685811e-14,
        1.0390625, 0, -0.038318864302141264, 4.6652946995830086e-15,
        1.0390625, 0, -0.038318864302141264, 4.6652946995830086e-15,
        1.03125, 0, -0.03077165866670839, -4.529814257790929e-14,
        1.03125, 0, -0.03077165866670839, -4.529814257790929e-14,
        1.0234375, 0, -0.023167059281490765, -4.361324067851568e-14,
        1.015625, 0, -0.015504186535963527, -1.7274567499706107e-15,
        1.015625, 0, -0.015504186535963527, -1.7274567499706107e-15,
        1.0078125, 0, -0.0077821404420319595, -2.298941004620351e-14,
        1.0078125, 0, -0.0077821404420319595, -2.298941004620351e-14,
        1.0, 0, 0.0, 0.0,
        1.0, 0, 0.0, 0.0,
        0.9921875, 0, 0.007843177461040796, -1.4902732911301337e-14,
        0.984375, 0, 0.01574835696817445, -3.527980389655325e-14,
        0.9765625, 0, 0.023716526617363343, -4.730054772033249e-14,
        0.96875, 0, 0.03174869831457272, 7.580310369375161e-15,
        0.9609375, 0, 0.039845908547249564, -4.9893776716773285e-14,
        0.953125, 0, 0.048009219186383234, -2.262629393030674e-14,
        0.9453125, 0, 0.056239718322899535, -2.345674491018699e-14,
        0.94140625, 0, 0.06038051098892083, -1.3352588834854848e-14,
        0.93359375, 0, 0.06871389254808946, -3.765296820388875e-14,
        0.92578125, 0, 0.07711730334438016, 5.1128335719851986e-14,
        0.91796875, 0, 0.08559193033545398, -5.046674438470119e-14,
        0.9140625, 0, 0.08985632912185793, 3.1218748807418837e-15,
        0.90625, 0, 0.09844007281321865, 3.3871241029241416e-14,
        0.8984375, 0, 0.10709813555638448, -1.7376727386423858e-14,
        0.89453125, 0, 0.11145544092528326, 3.957125899799804e-14,
        0.88671875, 0, 0.12022742699821265, -5.2849453521890294e-14,
        0.8828125, 0, 0.12464244520731427, -3.767012502308738e-14,
        0.875, 0, 0.13353139262449076, 3.1859736349078334e-14,
        0.87109375, 0, 0.13800567301939282, 5.0900642926060466e-14,
        0.86328125, 0, 0.14701474296180095, 8.710783796122478e-15,
        0.859375, 0, 0.15154989812720032, 6.157896229122976e-16,
        0.8515625, 0, 0.16068238169043525, 3.821577743916796e-14,
        0.84765625, 0, 0.16528009093906348, 3.9440046718453496e-14,
        0.83984375, 0, 0.17453941635187675, 2.2924522154618074e-14,
        0.8359375, 0, 0.17920142945774842, -3.742530094732263e-14,
        0.83203125, 0, 0.18388527877016259, -2.5223102140407338e-14,
        0.82421875, 0, 0.1933193110035063, -1.0320443688698849e-14,
        0.8203125, 0, 0.19806991376208316, 1.0634128304268335e-14,
        0.8125, 0, 0.20763936477828793, -4.3425422595242564e-14,
        0.80859375, 0, 0.21245865121420593, -1.2527395755711364e-14,
        0.8046875, 0, 0.21730127569003344, -5.204008743405884e-14,
        0.80078125, 0, 0.22216746534115828, -3.979844515951702e-15,
        0.79296875, 0, 0.2319714654378231, -4.7955860343296286e-14,
        0.7890625, 0, 0.2369097470783572, 5.015686013791602e-16,
        0.78515625, 0, 0.24187253642048745, -7.252318953240293e-16,
        0.78125, 0, 0.2468600779315011, 2.4688324156011588e-14,
        0.7734375, 0, 0.2569104137850218, 5.465121253624792e-15,
        0.76953125, 0, 0.26197371574153294, 4.102651071698446e-14,
        0.765625, 0, 0.2670627852490952, -4.996736502345936e-14,
        0.76171875, 0, 0.27217788591576664, 4.903580708156347e-14,
        0.7578125, 0, 0.27731928541618345, 5.089628039500759e-14,
        0.75390625, 0, 0.28248725557466514, 1.1782016386565151e-14,
        0.74609375, 0, 0.29290401643288533, 4.727452940514406e-14,
        0.7421875, 0, 0.29815337231912054, -4.4204083338755686e-14,
        0.73828125, 0, 0.3034304294199046, 1.548345993498083e-14,
        0.734375, 0, 0.30873548164959175, 2.1522127491642888e-14,
        0.73046875, 0, 0.3140688276249648, 1.1054030169005386e-14,
        0.7265625, 0, 0.31943077076641657, -5.534326352070679e-14,
        0.72265625, 0, 0.3248216194012912, -5.351646604259541e-14,
        0.71875, 0, 0.33024168687052224, 5.4612144489920215e-14,
        0.71484375, 0, 0.3356912916381134, 2.8136969901227338e-14,
        0.7109375, 0, 0.3411707574027787, -1.156568624616423e-14,
    ];

    // NetWasm: a square is the most common use of pow. Pow is small enough to
    // inline, so a constant exponent of 2 becomes x * x at the call, which is
    // correctly rounded and keeps pow's results for zeros, infinities and NaN.
    public static double Pow(double x, double y) => y == 2 ? x * x : PowCore(x, y);

    private static double PowCore(double x, double y)
    {
        uint signBias = 0;
        ulong ix, iy;
        uint topx, topy;

        ix = BitConverter.DoubleToUInt64Bits(x);
        iy = BitConverter.DoubleToUInt64Bits(y);
        // NetWasm: the exponents the earlier fdlibm pow answered directly, as
        // code writes roots and reciprocals with pow. x, 1/x and Sqrt(x) are
        // correctly rounded and keep pow's results for zeros, infinities and NaN.
        switch (iy)
        {
            case 0x3ff0000000000000: /* y is 1 */
                return x;
            case 0xbff0000000000000: /* y is -1 */
                return 1 / x;
            case 0x3fe0000000000000 when (ix >> 63) == 0: /* y is 0.5, x >= +0 */
                return Sqrt(x);
        }
        topx = Top12(x);
        topy = Top12(y);
        if (topx - 0x001 >= 0x7ff - 0x001 ||
            (topy & 0x7ff) - 0x3be >= 0x43e - 0x3be)
        {
            /* Note: if |y| > 1075 * ln2 * 2^53 ~= 0x1.749p62 then pow(x,y) = inf/0
               and if |y| < 2^-54 / 1075 ~= 0x1.e7b6p-65 then pow(x,y) = +-1.  */
            /* Special cases: (x < 0x1p-126 or inf or nan) or
               (|y| < 0x1p-65 or |y| >= 0x1p63 or nan).  */
            if (PowZeroInfNaN(iy))
            {
                if (2 * iy == 0)
                    return 1.0;
                if (ix == 0x3ff0000000000000) /* 1.0 */
                    return 1.0;
                if (2 * ix > 2 * 0x7ff0000000000000UL ||
                    2 * iy > 2 * 0x7ff0000000000000UL)
                    return x + y;
                if (2 * ix == 2 * 0x3ff0000000000000UL)
                    return 1.0;
                if ((2 * ix < 2 * 0x3ff0000000000000UL) == ((iy >> 63) == 0))
                    return 0.0; /* |x|<1 && y==inf or |x|>1 && y==-inf.  */
                return y * y;
            }
            if (PowZeroInfNaN(ix))
            {
                var x2 = x * x;
                if ((ix >> 63) != 0 && PowCheckInt(iy) == 1)
                    x2 = -x2;
                return (iy >> 63) != 0 ? 1 / x2 : x2;
            }
            /* Here x and y are non-zero finite.  */
            if ((ix >> 63) != 0)
            {
                /* Finite x < 0.  */
                var yint = PowCheckInt(iy);
                if (yint == 0)
                    return (x - x) / (x - x); /* __math_invalid(x) */
                if (yint == 1)
                    signBias = PowSignBias;
                ix &= 0x7fffffffffffffff;
                topx &= 0x7ff;
            }
            if ((topy & 0x7ff) - 0x3be >= 0x43e - 0x3be)
            {
                /* Note: sign_bias == 0 here because y is not odd.  */
                if (ix == 0x3ff0000000000000) /* 1.0 */
                    return 1.0;
                if ((topy & 0x7ff) < 0x3be)
                {
                    /* |y| < 2^-65, x^y ~= 1 + y*log(x).  */
                    return ix > 0x3ff0000000000000 ? 1.0 + y : 1.0 - y;
                }
                return (ix > 0x3ff0000000000000) == (topy < 0x800)
                    ? ExpOverflow(false)
                    : ExpUnderflow(false);
            }
            if (topx == 0)
            {
                /* Normalize subnormal x so exponent becomes negative.  */
                ix = BitConverter.DoubleToUInt64Bits(x * 4503599627370496.0 /* 0x1p52 */);
                ix &= 0x7fffffffffffffff;
                ix -= 52UL << 52;
            }
        }

        double hi, lo;
        {
            /* log_inline: compute y+TAIL = log(x) where the rounded result is y and TAIL has about
               additional 15 bits precision.  IX is the bit representation of x, but
               normalized in the subnormal range using the sign bit for the exponent.  */
            const double ln2hi = 0.6931471805598903; // 0x1.62e42fefa3800p-1
            const double ln2lo = 5.497923018708371e-14; // 0x1.ef35793c76730p-45
            const double A0 = -0.5; // -0x1p-1
            const double A1 = -0.6666666666666679; // 0x1.555555555556p-2 * -2
            const double A2 = 0.5000000000000007; // -0x1.0000000000006p-2 * -2
            const double A3 = 0.7999999995323976; // 0x1.999999959554ep-3 * 4
            const double A4 = -0.6666666663487739; // -0x1.555555529a47ap-3 * 4
            const double A5 = -1.142909628459501; // 0x1.2495b9b4845e9p-3 * -8
            const double A6 = 1.0000415263675542; // -0x1.0002b8b263fc3p-3 * -8

            double z, r, invc, logc, logctail, kd, loghi, t1, t2, loglo, lo1, lo2, p;
            ulong iz, tmp;
            int k, i;

            /* x = 2^k z; where z is in range [OFF,2*OFF) and exact.
               The range is split into N subintervals.
               The ith subinterval contains z and c is near its center.  */
            tmp = ix - PowLogOff;
            i = (int)((tmp >> (52 - PowLogTableBits)) & ((1 << PowLogTableBits) - 1));
            k = (int)((long)tmp >> 52); /* arithmetic shift */
            iz = ix - (tmp & (0xfffUL << 52));
            z = BitConverter.UInt64BitsToDouble(iz);
            kd = k;

            /* log(x) = k*Ln2 + log(c) + log1p(z/c-1).  */
            /* i is below N; each entry is invc, pad, logc and logctail.  */
            ref var entry = ref Unsafe.Add(ref MemoryMarshal.GetReference(PowLogTable), 4 * i);
            invc = entry;
            logc = Unsafe.Add(ref entry, 2);
            logctail = Unsafe.Add(ref entry, 3);

            /* Note: 1/c is j/N or j/N/2 where j is an integer in [N,2N) and
               |z/c - 1| < 1/N, so r = z/c - 1 is exactly representible.  */
            /* Split z such that rhi, rlo and rhi*rhi are exact and |rlo| <= |r|.  */
            var zhi = BitConverter.UInt64BitsToDouble((iz + (1UL << 31)) & (ulong.MaxValue << 32));
            var zlo = z - zhi;
            var rhi = zhi * invc - 1.0;
            var rlo = zlo * invc;
            r = rhi + rlo;

            /* k*Ln2 + log(c) + r.  */
            t1 = kd * ln2hi + logc;
            t2 = t1 + r;
            lo1 = kd * ln2lo + logctail;
            lo2 = t1 - t2 + r;

            /* Evaluation is optimized assuming superscalar pipelined execution.  */
            double ar, ar2, ar3, lo3, lo4;
            ar = A0 * r; /* A[0] = -0.5.  */
            ar2 = r * ar;
            ar3 = r * ar2;
            /* k*Ln2 + log(c) + r + A[0]*r*r.  */
            var arhi = A0 * rhi;
            var arhi2 = rhi * arhi;
            loghi = t2 + arhi2;
            lo3 = rlo * (ar + arhi);
            lo4 = t2 - loghi + arhi2;
            /* p = log1p(r) - r - A[0]*r*r.  */
            p = (ar3 * (A1 + r * A2 +
                    ar2 * (A3 + r * A4 + ar2 * (A5 + r * A6))));
            loglo = lo1 + lo2 + lo3 + lo4 + p;
            hi = loghi + loglo;
            lo = loghi - hi + loglo;
        }
        double ehi, elo;
        var yhi = BitConverter.UInt64BitsToDouble(iy & (ulong.MaxValue << 27));
        var ylo = y - yhi;
        var lhi = BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(hi) & (ulong.MaxValue << 27));
        var llo = hi - lhi + lo;
        ehi = yhi * lhi;
        elo = ylo * lhi + y * llo; /* |elo| < |ehi| * 2^-25.  */
        return PowExp(ehi, elo, signBias);
    }

    /* Computes sign*exp(x+xtail) where |xtail| < 2^-8/N and |xtail| <= |x|.
       The sign_bias argument is SIGN_BIAS or 0 and sets the sign to -1 or 1.  */
    private static double PowExp(double x, double xtail, uint signBias)
    {
        uint abstop;
        ulong ki, idx, top, sbits;
        double kd, z, r, r2, scale, tail, tmp;

        abstop = Top12(x) & 0x7ff;
        if (abstop - 0x3c9u >= 0x408u - 0x3c9u)
        {   /* |x| < 0x1p-54 or |x| >= 512 */
            if (abstop - 0x3c9u >= 0x80000000)
            {
                /* Avoid spurious underflow for tiny x.  */
                /* Note: 0 is common input.  */
                var one = 1.0 + x;
                return signBias != 0 ? -one : one;
            }
            if (abstop >= 0x409)
            {   /* |x| >= 1024 */
                /* Note: inf and nan are already handled.  */
                if ((BitConverter.DoubleToUInt64Bits(x) >> 63) != 0)
                    return ExpUnderflow(signBias != 0);
                else
                    return ExpOverflow(signBias != 0);
            }
            /* Large x is special cased below.  */
            abstop = 0;
        }

        /* exp(x) = 2^(k/N) * exp(r), with exp(r) in [2^(-1/2N),2^(1/2N)].  */
        /* x = ln2/N*k + r, with int k and r in [-ln2/2N, ln2/2N].  */
        z = ExpInvLn2N * x;
        /* z - kd is in [-1, 1] in non-nearest rounding modes.  */
        kd = z + ExpShift;
        ki = BitConverter.DoubleToUInt64Bits(kd);
        kd -= ExpShift;
        r = x + kd * ExpNegLn2HiN + kd * ExpNegLn2LoN;
        /* The code assumes 2^-200 < |xtail| < 2^-8/N.  */
        r += xtail;
        /* 2^(k/N) ~= scale * (1 + tail).  */
        idx = 2 * (ki & ExpTableMask);
        top = (ki + signBias) << (52 - ExpTableBits);
        /* idx is below the table's 2*N entries.  */
        ref var entry = ref Unsafe.Add(ref MemoryMarshal.GetReference(ExpTable), (nint)idx);
        tail = BitConverter.UInt64BitsToDouble(entry);
        /* This is only a valid scale when -1023*N < k < 1024*N.  */
        sbits = Unsafe.Add(ref entry, 1) + top;
        /* exp(x) = 2^(k/N) * exp(r) ~= scale + scale * (tail + exp(r) - 1).  */
        /* Evaluation is optimized assuming superscalar pipelined execution.  */
        r2 = r * r;
        /* Without fma the worst case error is 0.25/N ulp larger.  */
        /* Worst case error is less than 0.5+1.11/N+(abs poly error * 2^53) ulp.  */
        tmp = tail + r + r2 * (ExpC2 + r * ExpC3) + r2 * r2 * (ExpC4 + r * ExpC5);
        if (abstop == 0)
            return ExpSpecialCase(tmp, sbits, ki, 1009, 5.486124068793689e+303 /* 0x1p1009 */);
        scale = BitConverter.UInt64BitsToDouble(sbits);
        /* Note: tmp == 0 or |tmp| > 2^-200 and scale > 2^-739, so there
           is no spurious underflow here even without fma.  */
        return scale + scale * tmp;
    }

    /* Returns 0 if not int, 1 if odd int, 2 if even int.  The argument is
       the bit representation of a non-zero finite floating-point value.  */
    private static int PowCheckInt(ulong iy)
    {
        var e = (int)(iy >> 52 & 0x7ff);
        if (e < 0x3ff)
            return 0;
        if (e > 0x3ff + 52)
            return 2;
        if ((iy & ((1UL << (0x3ff + 52 - e)) - 1)) != 0)
            return 0;
        if ((iy & (1UL << (0x3ff + 52 - e))) != 0)
            return 1;
        return 2;
    }

    /* Returns 1 if input is the bit representation of 0, infinity or nan.  */
    private static bool PowZeroInfNaN(ulong i) => 2 * i - 1 >= 2 * 0x7ff0000000000000UL - 1;
}
