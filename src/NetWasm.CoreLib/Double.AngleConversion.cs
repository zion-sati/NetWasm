// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// The correctly rounded DegreesToRadians and RadiansToDegrees from System.Private.CoreLib
// Double.cs at runtime commit 474ed963699fc29809015392738a15231aaae2de.

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace System
{
    public partial struct Double
    {
        // `pi / 180` and `180 / pi` are not exactly representable, so each is carried as a
        // `head + mid + tail` triple giving about 159 significant bits, where each term is the
        // correctly rounded remainder left by the ones above it.
        //
        // Two limbs are not always enough. Their accumulated error is about `2^-53` ulp of the
        // result, while enumerating every significand whose exact product lands nearest a rounding
        // midpoint gives worst cases only `2^-55.58` ulp away for degrees-to-radians and `2^-55.99`
        // ulp for the reverse. Those sit well inside that error, so two limbs cannot decide them.
        // The third limb takes the error to about `2^-106` ulp, which clears the worst case by 50
        // bits.
        internal const double DegreesToRadiansHead = 0.017453292519943295;     // 0x1.1DF46A2529D39p-6
        internal const double DegreesToRadiansMid = 2.9486522708701687E-19;    // 0x1.5C1D8BECDD291p-62
        internal const double DegreesToRadiansTail = -1.3427726813345382E-35;   // -0x1.1D937FA428858p-116
        internal const double RadiansToDegreesHead = 57.29577951308232;        // 0x1.CA5DC1A63C1F8p+5
        internal const double RadiansToDegreesMid = -1.9878495670576283E-15;   // -0x1.1E7AB456405F9p-49
        internal const double RadiansToDegreesTail = -1.6833394980391744E-31;   // -0x1.B505196FABB41p-103

        // Recovering the roundoff of `value * mid` is what both the two limb and the three limb form
        // rest on, and below these that roundoff falls onto the subnormal grid, losing up to one
        // smallest subnormal. That loss has to stay under the closest an input brings the exact
        // product to a rounding boundary, which is the `2^-55.58` and `2^-55.99` ulp above. Writing
        // the smallest subnormal as `2^(emin - p + 1)` and an ulp of the result as
        // `2^(e + e_head - p + 1)`, the `p` cancels and the floor on the input exponent `e` is just
        // `emin - e_head + w`, giving `-960.42` and `-971.01`.
        internal const double DegreesToRadiansMin = 1.0261342003245941E-289;   // 0x1p-960
        internal const double RadiansToDegreesMin = 5.010420900022432E-293;    // 0x1p-971

        // At or above these `value * head` overflows, which the triple cannot recover from since the
        // roundoff of an infinite product is `inf - inf`. Paired with the minimums above they bound
        // the domain MultiplyWide covers, which is what the vector paths range test on. `pi / 180`
        // is under one, so nothing finite overflows there and the bound only excludes the non-finite;
        // the single unsigned range test covers that the same as any other value.
        internal const double DegreesToRadiansMax = PositiveInfinity;
        internal const double RadiansToDegreesMax = 3.137566414384587E+306;    // 0x1.1DF46A2529D39p+1018

        // The cold path lifts `value` by `ConversionScaleUp` so the whole computation runs as it
        // would in the normal range. Scaling by a power of two is exact in both directions, so any
        // exponent in the valid window gives bit-identical results; only the window itself matters.
        //
        // The floor is the larger of two conditions, evaluated for the smallest input `2^(emin-p+1)`
        // and taken over both directions, where `k` is the scale exponent being solved for,
        // `p` is the precision in bits including the implicit leading bit, `emin` is the minimum
        // normal exponent, and `e_head`/`e_mid` are the constants' own exponents. The `p - 1` term
        // is the gap from the smallest subnormal to the smallest normal, so it is the lift needed to
        // bring any input into the normal range:
        //
        //   k >= (p - 1) - e_mid           so `scaled * mid` stays normal
        //   k >= 2 * (p - 1) - e_head      so the smallest Veltkamp sub-product in the no-FMA
        //                                  MultiplyRoundoff stays normal, since it is `2^-(p-1)`
        //                                  relative to `scaled * head`
        //
        // For `double` those give 114 and 110 for degrees-to-radians, 101 and 99 for the reverse,
        // so the floor is `2^114`. The ceiling is `2^1022`, keeping `ConversionScaleDown` normal.
        //
        // `tail` gets no condition. Losing it outright costs `2^-110` relative, and since a result
        // is under `2^p` ulp that is under `2^-57` ulp, which the worst case above already clears.
        //
        // Both conditions are sufficient rather than tight, since an input small enough to violate
        // either produces a result with too few significand bits for what is lost to reach the
        // rounding position.
        private const double ConversionScaleUp = 2.076918743413931E+34;        // 0x1p+114
        private const double ConversionScaleDown = 4.81482486096809E-35;       // 0x1p-114

        // Keeps the top 26 significand bits, which is the Veltkamp split MultiplyRoundoff uses.
        internal const ulong ConversionSplitMask = 0xFFFF_FFFF_F800_0000;

        // Bounds the error of the two limb form relative to `value * head`, which is what lets that
        // form tell whether it resolved the rounding. Three things are dropped or rounded away
        // there, none of them reaching `2^-105`:
        //
        //   `value * (C - head - mid)`, which `tail` puts under `2^-108`
        //   the roundoff of `value * mid`, so `2^-53` of a term already under `2^-54`
        //   the roundoff of their sum, so `2^-53` of a sum already under `2^-52`
        //
        // `2^-100` clears the total by more than 16x, leaving room for rounding the ends of the
        // interval it forms as well. A wider bound stays correct and only sends more inputs to the
        // three limb form; at this width that is about one in `2^46`.
        internal const double ConversionErrorScale = 7.888609052210118E-31;     // 0x1p-100

        // The roundoff of `value * head` is itself exactly representable, and recovering it is
        // what lets each limb be spent without losing what fell off the end. A fused multiply-add
        // gives it directly; otherwise splitting both operands into 26 and 27 bit halves makes
        // every sub-product exact, which recovers the same value using only multiplies and adds.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double MultiplyRoundoff(double value, double head, double product)
        {
            if (Fma.IsSupported || AdvSimd.Arm64.IsSupported)
            {
                return FusedMultiplyAdd(value, head, -product);
            }

            double headHi = BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(head) & ConversionSplitMask);
            double headLo = head - headHi;

            double valueHi = BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(value) & ConversionSplitMask);
            double valueLo = value - valueHi;

            return (((((valueHi * headHi) - product) + (valueHi * headLo)) + (valueLo * headHi)) + (valueLo * headLo));
        }

        // Computes `value * (head + mid + tail)` as the unevaluated pair `result + residual`, where
        // `result` is the correctly rounded product. `value * head` must be known finite and
        // non-zero, which is what lets `head` be the nearest representable value rather than being
        // biased to keep a sign.
        //
        // The three limb products are accumulated exactly apart from the two terms already below
        // `2^-106` relative, then folded back onto `product` in decreasing order of magnitude. The
        // final fold rounds twice, so the correction is first rounded to odd, which is the standard
        // way of making the second rounding agree with rounding the exact value once: the odd
        // neighbour is never a midpoint, so nothing that decides the outcome can be discarded.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double MultiplyWide(double value, double head, double mid, double tail, out double residual)
        {
            double product = value * head;
            double productError = MultiplyRoundoff(value, head, product);   // exact
            double middle = value * mid;
            double middleError = MultiplyRoundoff(value, mid, middle);      // exact but for the
                                                                            // subnormal loss the
                                                                            // minimums bound
            double bottom = middleError + (value * tail);

            // The magnitudes of `productError` and `middle` are not ordered, so the sum needs the
            // form that does not assume it
            double sum = productError + middle;
            double bias = sum - productError;
            double sumError = (productError - (sum - bias)) + (middle - bias);
            double lower = sumError + bottom;

            double upper = product + sum;
            double upperError = sum - (upper - product);                    // exact, |product| >= |sum|

            double correction = upperError + lower;
            double lost = (upperError - correction) + lower;                 // exact

            if ((lost != 0) && ((BitConverter.DoubleToUInt64Bits(correction) & 1) == 0))
            {
                // Consecutive values alternate in significand parity, so exactly one of the two
                // bracketing values is odd and it lies in the direction of what was lost
                correction = (lost > 0) ? BitIncrement(correction) : BitDecrement(correction);
            }

            double result = upper + correction;
            residual = (upper - result) + correction;                       // exact
            return result;
        }

        // Computes `value * (head + mid + tail)` as a single rounding, over the domain the minimums
        // and maximums above bound. Two limbs are enough for all but the rare input, and whether
        // they were is itself decidable: their sum is known to within `bound`, so if both ends of
        // that interval round to the same value then so does the exact product.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double MultiplyWide(double value, double head, double mid, double tail)
        {
            double product = value * head;

            if (!IsFinite(product) || (product == 0))
            {
                // The roundoff of an infinite product is `inf - inf`, and a zero product would
                // come back from the sum having lost its sign
                return product;
            }

            double sum = MultiplyRoundoff(value, head, product) + (value * mid);
            double bound = Abs(product) * ConversionErrorScale;
            double result = product + (sum - bound);

            if (result == (product + (sum + bound)))
            {
                return result;
            }

            return MultiplyWide(value, head, mid, tail, out _);
        }

        // Computes `value * (head + mid + tail)` for the inputs too small for MultiplyWide, namely
        // those below the minimums above. Scaling up moves the product back into the range where
        // the triple is resolved exactly; scaling back down then rounds a second time, with
        // `residual` breaking any tie that rounding would otherwise resolve to even.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double ScaleAndMultiplyWide(double value, double head, double mid, double tail)
        {
            double scaled = value * ConversionScaleUp;

            if (scaled == 0)
            {
                // Multiplying keeps the sign of zero, which the sum would lose
                return scaled * head;
            }

            double sum = MultiplyWide(scaled, head, mid, tail, out double residual);
            double result = sum * ConversionScaleDown;

            if (residual == 0)
            {
                return result;
            }

            double dropped = sum - (result * ConversionScaleUp);            // exact

            if (dropped == 0)
            {
                // Scaling back down lost nothing, so it was already the single rounding
                return result;
            }

            double next = (dropped > 0) ? BitIncrement(result) : BitDecrement(result);

            if ((Abs(next - result) * ConversionScaleUp) != (2.0 * Abs(dropped)))
            {
                return result;
            }

            // `sum` sat exactly between `result` and `next`, so the true value did not
            return ((residual > 0) == (dropped > 0)) ? next : result;
        }

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.DegreesToRadians(TSelf)" />
        public static double DegreesToRadians(double degrees)
        {
            if (Abs(degrees) < DegreesToRadiansMin)
            {
                return ScaleAndMultiplyWide(degrees, DegreesToRadiansHead, DegreesToRadiansMid, DegreesToRadiansTail);
            }

            return MultiplyWide(degrees, DegreesToRadiansHead, DegreesToRadiansMid, DegreesToRadiansTail);
        }

        /// <inheritdoc cref="ITrigonometricFunctions{TSelf}.RadiansToDegrees(TSelf)" />
        public static double RadiansToDegrees(double radians)
        {
            if (Abs(radians) < RadiansToDegreesMin)
            {
                return ScaleAndMultiplyWide(radians, RadiansToDegreesHead, RadiansToDegreesMid, RadiansToDegreesTail);
            }

            return MultiplyWide(radians, RadiansToDegreesHead, RadiansToDegreesMid, RadiansToDegreesTail);
        }
    }
}
