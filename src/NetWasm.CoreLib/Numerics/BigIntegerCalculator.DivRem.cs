// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from dotnet/runtime BigIntegerCalculator at commit
// 474ed963699fc29809015392738a15231aaae2de (System.Private.CoreLib's
// BigIntegerCalculator.Shared.cs): the grammar-school division over
// NetWasm's fixed 32-bit BigInteger magnitude limbs.

namespace System.Numerics
{
    internal static partial class BigIntegerCalculator
    {
        // Leaves left % right in the low words of left and, when quotient is not
        // empty, stores left / right there.
        internal static void DivideGrammarSchool(Span<uint> left, ReadOnlySpan<uint> right, Span<uint> quotient)
        {
            var divisorHigh = right[^1];
            var divisorLow = right.Length > 1 ? right[^2] : 0;
            var shift = BitOperations.LeadingZeroCount(divisorHigh);
            var backShift = 32 - shift;

            if (shift > 0)
            {
                var divisorNext = right.Length > 2 ? right[^3] : 0;
                divisorHigh = (divisorHigh << shift) | (divisorLow >> backShift);
                divisorLow = (divisorLow << shift) | (divisorNext >> backShift);
            }

            for (var index = left.Length; index >= right.Length; index--)
            {
                var offset = index - right.Length;
                var existingHigh = (uint)index < (uint)left.Length ? left[index] : 0;
                var valueHigh = existingHigh;
                var valueMiddle = left[index - 1];
                var valueLow = index > 1 ? left[index - 2] : 0;

                if (shift > 0)
                {
                    var valueNext = index > 2 ? left[index - 3] : 0;
                    valueHigh = (valueHigh << shift) | (valueMiddle >> backShift);
                    valueMiddle = (valueMiddle << shift) | (valueLow >> backShift);
                    valueLow = (valueLow << shift) | (valueNext >> backShift);
                }

                var digit = valueHigh >= divisorHigh
                    ? uint.MaxValue
                    : Divide(valueHigh, valueMiddle, divisorHigh);

                while (DivideGuessTooBig(digit, valueHigh, valueMiddle, valueLow, divisorHigh, divisorLow))
                {
                    digit--;
                }

                if (digit != 0)
                {
                    var carry = SubtractDivisor(left.Slice(offset), right, digit);
                    if (carry != existingHigh)
                    {
                        // The guess was still one too high.
                        AddDivisor(left.Slice(offset), right);
                        digit--;
                    }
                }

                if ((uint)offset < (uint)quotient.Length) quotient[offset] = digit;
                if ((uint)index < (uint)left.Length) left[index] = 0;
            }
        }

        private static uint Divide(uint high, uint low, uint divisor) =>
            (uint)((((ulong)high << 32) | low) / divisor);

        private static bool DivideGuessTooBig(
            uint quotient,
            uint valueHigh,
            uint valueMiddle,
            uint valueLow,
            uint divisorHigh,
            uint divisorLow)
        {
            var highProduct = (ulong)divisorHigh * quotient;
            var lowProduct = (ulong)divisorLow * quotient;
            var checkHigh = (uint)(highProduct >> 32);
            var checkMiddle = (uint)highProduct;
            var checkLowHigh = (uint)(lowProduct >> 32);
            var checkLow = (uint)lowProduct;
            var middleSum = checkMiddle + checkLowHigh;
            if (middleSum < checkLowHigh) checkHigh++;

            return checkHigh > valueHigh
                || checkHigh == valueHigh
                && (middleSum > valueMiddle || middleSum == valueMiddle && checkLow > valueLow);
        }

        private static uint SubtractDivisor(Span<uint> left, ReadOnlySpan<uint> right, uint quotient)
        {
            uint carry = 0;
            for (var index = 0; index < right.Length; index++)
            {
                var product = (ulong)right[index] * quotient + carry;
                var low = (uint)product;
                var high = (uint)(product >> 32);
                var original = left[index];
                left[index] = original - low;
                if (original < low) high++;
                carry = high;
            }

            return carry;
        }

        private static uint AddDivisor(Span<uint> left, ReadOnlySpan<uint> right)
        {
            ulong carry = 0;
            for (var index = 0; index < right.Length; index++)
            {
                var sum = (ulong)left[index] + right[index] + carry;
                left[index] = (uint)sum;
                carry = sum >> 32;
            }

            return (uint)carry;
        }
    }
}
