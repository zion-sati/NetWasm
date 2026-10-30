// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from dotnet/runtime BigIntegerCalculator at commit
// 474ed963699fc29809015392738a15231aaae2de. NetWasm retains the managed
// scalar square-and-multiply and grammar-school reduction paths over its
// fixed 32-bit BigInteger magnitude limbs.

using System.Buffers;

namespace System.Numerics
{
    internal static partial class BigIntegerCalculator
    {
        public static uint[] Pow(
            ReadOnlySpan<uint> value,
            ReadOnlySpan<uint> power,
            ReadOnlySpan<uint> modulus)
        {
            if (modulus.Length == 1)
            {
                var scalar = Pow(value, power, modulus[0]);
                return scalar == 0 ? Array.Empty<uint>() : new uint[] { scalar };
            }

            var bufferLength = checked(modulus.Length * 2);
            var valueBufferLength = Math.Max(value.Length, bufferLength);
            var valueArray = ArrayPool<uint>.Shared.Rent(valueBufferLength);
            var resultArray = ArrayPool<uint>.Shared.Rent(bufferLength);
            var tempArray = ArrayPool<uint>.Shared.Rent(bufferLength);

            try
            {
                Span<uint> valueBuffer = valueArray.AsSpan(0, valueBufferLength);
                Span<uint> resultBuffer = resultArray.AsSpan(0, bufferLength);
                Span<uint> tempBuffer = tempArray.AsSpan(0, bufferLength);
                valueBuffer.Clear();
                resultBuffer.Clear();
                tempBuffer.Clear();

                value.CopyTo(valueBuffer);
                var valueLength = Reduce(valueBuffer, value.Length, modulus);
                resultBuffer[0] = 1;
                var resultLength = 1;

                var powerLength = ActualLength(power);
                for (var limbIndex = 0; limbIndex < powerLength; limbIndex++)
                {
                    var limb = power[limbIndex];
                    var bitCount = limbIndex == powerLength - 1
                        ? 32 - BitOperations.LeadingZeroCount(limb)
                        : 32;

                    for (var bitIndex = 0; bitIndex < bitCount; bitIndex++)
                    {
                        if ((limb & 1) != 0)
                        {
                            resultLength = MultiplySelf(
                                ref resultBuffer,
                                resultLength,
                                valueBuffer.Slice(0, valueLength),
                                ref tempBuffer,
                                modulus);
                        }

                        var isLastBit = limbIndex == powerLength - 1 && bitIndex == bitCount - 1;
                        if (!isLastBit)
                        {
                            valueLength = SquareSelf(
                                ref valueBuffer,
                                valueLength,
                                ref tempBuffer,
                                modulus);
                        }

                        limb >>= 1;
                    }
                }

                if (resultLength == 0) return Array.Empty<uint>();
                var result = new uint[resultLength];
                resultBuffer.Slice(0, resultLength).CopyTo(result);
                return result;
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(valueArray);
                ArrayPool<uint>.Shared.Return(resultArray);
                ArrayPool<uint>.Shared.Return(tempArray);
            }
        }

        private static uint Pow(ReadOnlySpan<uint> value, ReadOnlySpan<uint> power, uint modulus)
        {
            var reducedValue = Remainder(value, modulus);
            var result = 1u % modulus;
            var powerLength = ActualLength(power);

            for (var limbIndex = 0; limbIndex < powerLength; limbIndex++)
            {
                var limb = power[limbIndex];
                var bitCount = limbIndex == powerLength - 1
                    ? 32 - BitOperations.LeadingZeroCount(limb)
                    : 32;

                for (var bitIndex = 0; bitIndex < bitCount; bitIndex++)
                {
                    if ((limb & 1) != 0)
                    {
                        result = (uint)((ulong)result * reducedValue % modulus);
                    }

                    var isLastBit = limbIndex == powerLength - 1 && bitIndex == bitCount - 1;
                    if (!isLastBit)
                    {
                        reducedValue = (uint)((ulong)reducedValue * reducedValue % modulus);
                    }

                    limb >>= 1;
                }
            }

            return result;
        }

        private static int MultiplySelf(
            ref Span<uint> left,
            int leftLength,
            ReadOnlySpan<uint> right,
            ref Span<uint> temp,
            ReadOnlySpan<uint> modulus)
        {
            var productLength = leftLength + right.Length;
            var product = temp.Slice(0, productLength);
            product.Clear();
            Multiply(left.Slice(0, leftLength), right, product);
            left.Clear();
            Swap(ref left, ref temp);
            return Reduce(left, productLength, modulus);
        }

        private static int SquareSelf(
            ref Span<uint> value,
            int valueLength,
            ref Span<uint> temp,
            ReadOnlySpan<uint> modulus)
        {
            var productLength = valueLength * 2;
            var product = temp.Slice(0, productLength);
            product.Clear();
            Multiply(value.Slice(0, valueLength), value.Slice(0, valueLength), product);
            value.Clear();
            Swap(ref value, ref temp);
            return Reduce(value, productLength, modulus);
        }

        private static void Swap(ref Span<uint> left, ref Span<uint> right)
        {
            var temporary = left;
            left = right;
            right = temporary;
        }

        private static void Multiply(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right, Span<uint> result)
        {
            if (left.Length < right.Length)
            {
                var temporary = left;
                left = right;
                right = temporary;
            }

            for (var leftIndex = 0; leftIndex < left.Length; leftIndex++)
            {
                ulong carry = 0;
                for (var rightIndex = 0; rightIndex < right.Length; rightIndex++)
                {
                    var resultIndex = leftIndex + rightIndex;
                    var product = (ulong)left[leftIndex] * right[rightIndex] + result[resultIndex] + carry;
                    result[resultIndex] = (uint)product;
                    carry = product >> 32;
                }

                var carryIndex = leftIndex + right.Length;
                while (carry != 0)
                {
                    var sum = result[carryIndex] + carry;
                    result[carryIndex] = (uint)sum;
                    carry = sum >> 32;
                    carryIndex++;
                }
            }
        }

        private static int Reduce(Span<uint> value, int valueLength, ReadOnlySpan<uint> modulus)
        {
            valueLength = ActualLength(value.Slice(0, valueLength));
            if (valueLength < modulus.Length) return valueLength;

            DivideGrammarSchool(value.Slice(0, valueLength), modulus, default);
            return ActualLength(value.Slice(0, Math.Min(valueLength, modulus.Length)));
        }

        private static uint Remainder(ReadOnlySpan<uint> left, uint right)
        {
            ulong remainder = 0;
            for (var index = left.Length - 1; index >= 0; index--)
            {
                remainder = ((remainder << 32) | left[index]) % right;
            }

            return (uint)remainder;
        }

        private static int ActualLength(ReadOnlySpan<uint> value)
        {
            var length = value.Length;
            while (length > 0 && value[length - 1] == 0) length--;
            return length;
        }
    }
}
