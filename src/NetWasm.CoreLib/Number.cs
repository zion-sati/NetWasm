// Portions of this invariant-only numeric implementation are adapted from
// dotnet/runtime System.Private.CoreLib Number.Parsing.cs and
// Number.Formatting.cs. The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
//
// Integer formatting is ported separately in Number.IntegerFormatting.cs.
// The remaining parsing and floating-point support here uses invariant culture.
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace System
{
    internal static partial class Number
    {
        // ToString() with no format: the shortest round-trip form, without the
        // format string machinery.
        internal static string FormatSingle(float value) =>
            FormatShortestFloating<float>(BitConverter.SingleToUInt32Bits(value));

        internal static string FormatDouble(double value) =>
            FormatShortestFloating<double>(BitConverter.DoubleToUInt64Bits(value));

        internal static string FormatShortestFloating<TNumber>(ulong bits, NumberFormatInfo? info = null)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var builder = new ValueStringBuilder(32);
            AppendShortestFloating<TNumber>(ref builder, bits, info);
            return builder.ToString();
        }

        // The shortest digits that round-trip, as ToString() writes them, with the
        // provider's symbols, or the current culture's without one. It takes no
        // format string, so a program that never passes one does not reach the
        // custom format parser and formatter.
        internal static void AppendShortestFloating<TNumber>(scoped ref ValueStringBuilder builder, ulong bits, NumberFormatInfo? info = null)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber> =>
            AppendRoundTripFloating<TNumber>(ref builder, bits, TNumber.MaxRoundTripDigits, lowerExponent: false, info);

        // The fewest digits, up to maxDigits, that convert back to the same value.
        private static unsafe void AppendRoundTripFloating<TNumber>(
            scoped ref ValueStringBuilder builder,
            ulong bits,
            int maxDigits,
            bool lowerExponent,
            NumberFormatInfo? info)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var fractionBits = TNumber.NormalMantissaBits - 1;
            var exponentBits = TNumber.ExponentBits;
            var exponentMask = (1UL << exponentBits) - 1;
            var exponent = (bits >> fractionBits) & exponentMask;
            var fraction = bits & ((1UL << fractionBits) - 1);
            if (exponent == exponentMask)
            {
                builder.Append(fraction != 0 ? info?.NaNSymbol ?? NumberFormatInfo.CurrentNaNSymbol :
                    (bits >> (fractionBits + exponentBits)) != 0
                        ? info?.NegativeInfinitySymbol ?? NumberFormatInfo.CurrentNegativeInfinitySymbol
                        : info?.PositiveInfinitySymbol ?? NumberFormatInfo.CurrentPositiveInfinitySymbol);
                return;
            }
            var negative = (bits >> (fractionBits + exponentBits)) != 0;
            if (exponent == 0 && fraction == 0)
            {
                if (negative) builder.Append(info is null ? NumberFormatInfo.CurrentNegativeSignTChar<char>() : info.NegativeSignTChar<char>());
                builder.Append('0');
                return;
            }

            // The shortest digits that round-trip, from upstream's Grisu3 with its Dragon4 fallback, as desktop
            // computes them; a stack buffer holds them, so ToString allocates only its string.
            byte* pDigits = stackalloc byte[TNumber.NumberBufferLength];
            var number = new NumberBuffer(NumberBufferKind.FloatingPoint, pDigits, TNumber.NumberBufferLength);
            var value = TNumber.BitsToFloat(bits);
            if (!Grisu3.TryRun(value, -1, ref number))
            {
                Dragon4(value, -1, isSignificantDigits: true, ref number);
            }
            Span<char> selected = stackalloc char[number.DigitsCount];
            for (var index = 0; index < number.DigitsCount; index++)
            {
                selected[index] = (char)number.Digits[index];
            }
            var selectedLength = number.DigitsCount;
            var decimalExponent = number.Scale - 1;
            AppendGeneralDigits(
                ref builder,
                selected,
                selectedLength,
                decimalExponent,
                negative,
                maxDigits,
                lowerExponent,
                info is null ? NumberFormatInfo.CurrentNegativeSignTChar<char>() : info.NegativeSignTChar<char>(),
                info is null ? NumberFormatInfo.CurrentNumberDecimalSeparatorTChar<char>() : info.NumberDecimalSeparatorTChar<char>(),
                info is null ? NumberFormatInfo.CurrentPositiveSignTChar<char>() : info.PositiveSignTChar<char>());
        }

        internal static ulong ConvertDecimalToFloatingBits<TNumber>(
            ReadOnlySpan<char> digits,
            int digitCount,
            long exponent10,
            bool negative)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var fractionBits = TNumber.NormalMantissaBits - 1;
            var sign = negative ? 1UL << (fractionBits + TNumber.ExponentBits) : 0;
            if (digitCount == 0) return sign;
            var decimalExponent = exponent10 + digitCount - 1;
            if (decimalExponent > TNumber.MaxDecimalExponent)
            {
                return sign | TNumber.InfinityBits;
            }
            if (decimalExponent < TNumber.MinDecimalExponent - TNumber.MaxRoundTripDigits - 2) return sign;

            var numerator = new FloatingBigInteger(0U);
            for (var index = 0; index < digitCount; index++)
            {
                numerator.Multiply(10);
                numerator.Add((uint)(digits[index] - '0'));
            }
            var denominator = new FloatingBigInteger(1U);
            if (exponent10 > 0) numerator.MultiplyPower10((int)exponent10);
            if (exponent10 < 0) denominator.MultiplyPower10((int)-exponent10);

            var binaryExponent = FloorBinaryExponent(numerator, denominator);
            if (binaryExponent > TNumber.MaxBinaryExponent)
            {
                return sign | TNumber.InfinityBits;
            }
            if (binaryExponent >= TNumber.MinBinaryExponent)
            {
                var shift = fractionBits - binaryExponent;
                var scaledNumerator = numerator.Clone();
                var scaledDenominator = denominator.Clone();
                if (shift >= 0) scaledNumerator.ShiftLeft(shift);
                else scaledDenominator.ShiftLeft(-shift);
                var significand = FloatingBigInteger.DivideToUInt64(
                    scaledNumerator,
                    scaledDenominator,
                    out var remainder);
                significand = RoundToEven(significand, remainder, scaledDenominator);
                var carry = 1UL << (fractionBits + 1);
                if (significand == carry)
                {
                    significand >>= 1;
                    binaryExponent++;
                    if (binaryExponent > TNumber.MaxBinaryExponent)
                    {
                        return sign | TNumber.InfinityBits;
                    }
                }
                var encodedExponent = (ulong)(binaryExponent + TNumber.ExponentBias);
                var fractionMask = (1UL << fractionBits) - 1;
                return sign | encodedExponent << fractionBits | significand & fractionMask;
            }

            var subnormalShift = fractionBits - TNumber.MinBinaryExponent;
            var subnormalNumerator = numerator.Clone();
            subnormalNumerator.ShiftLeft(subnormalShift);
            var fraction = FloatingBigInteger.DivideToUInt64(
                subnormalNumerator,
                denominator,
                out var subnormalRemainder);
            fraction = RoundToEven(fraction, subnormalRemainder, denominator);
            return sign | fraction;
        }

        private static ulong RoundToEven(
            ulong value,
            FloatingBigInteger remainder,
            FloatingBigInteger denominator)
        {
            var twice = remainder.Clone();
            twice.Multiply(2);
            var comparison = FloatingBigInteger.Compare(twice, denominator);
            return comparison > 0 || comparison == 0 && (value & 1) != 0
                ? checked(value + 1)
                : value;
        }

        private static int FloorBinaryExponent(
            FloatingBigInteger numerator,
            FloatingBigInteger denominator)
        {
            var exponent = numerator.BitLength - denominator.BitLength;
            var left = numerator.Clone();
            var right = denominator.Clone();
            if (exponent >= 0) right.ShiftLeft(exponent);
            else left.ShiftLeft(-exponent);
            if (FloatingBigInteger.Compare(left, right) < 0) exponent--;
            return exponent;
        }

        private static void AppendGeneralDigits(
            scoped ref ValueStringBuilder builder,
            scoped ReadOnlySpan<char> digits,
            int digitCount,
            int decimalExponent,
            bool negative,
            int maxRoundTripDigits,
            bool lowerExponent,
            ReadOnlySpan<char> negativeSign,
            ReadOnlySpan<char> decimalSeparator,
            ReadOnlySpan<char> positiveSign)
        {
            var scientific = decimalExponent < -4 || decimalExponent >= maxRoundTripDigits;
            if (!scientific)
            {
                if (negative) builder.Append(negativeSign);
                if (decimalExponent >= 0)
                {
                    var wholeDigits = decimalExponent + 1;
                    var fractionDigits = digitCount > wholeDigits ? digitCount - wholeDigits : 0;
                    for (var index = 0; index < wholeDigits; index++)
                    {
                        builder.Append(index < digitCount ? digits[index] : '0');
                    }
                    if (fractionDigits != 0)
                    {
                        builder.Append(decimalSeparator);
                        for (var index = wholeDigits; index < digitCount; index++) builder.Append(digits[index]);
                    }
                    return;
                }
                var zeros = -decimalExponent - 1;
                builder.Append('0');
                builder.Append(decimalSeparator);
                builder.Append('0', zeros);
                builder.Append(digits.Slice(0, digitCount));
                return;
            }

            var absoluteExponent = decimalExponent < 0 ? -decimalExponent : decimalExponent;
            var exponentDigits = absoluteExponent >= 100 ? 3 : 2;
            if (negative) builder.Append(negativeSign);
            builder.Append(digits[0]);
            if (digitCount > 1)
            {
                builder.Append(decimalSeparator);
                builder.Append(digits.Slice(1, digitCount - 1));
            }
            builder.Append(lowerExponent ? 'e' : 'E');
            builder.Append(decimalExponent < 0 ? negativeSign : positiveSign);
            Span<char> exponentBuffer = stackalloc char[10];
            var position = exponentBuffer.Length;
            for (var index = exponentDigits - 1; index >= 0; index--)
            {
                exponentBuffer[--position] = (char)('0' + absoluteExponent % 10);
                absoluteExponent /= 10;
            }
            builder.Append(exponentBuffer[position..]);
        }

        private static bool IsAsciiWhiteSpace(char value) =>
            value is ' ' or '\t' or '\r' or '\n' or '\f' or '\v';

        internal static int Compare(long left, long right) => left < right ? -1 : left > right ? 1 : 0;
        internal static int Compare(ulong left, ulong right) => left < right ? -1 : left > right ? 1 : 0;

    }
}
