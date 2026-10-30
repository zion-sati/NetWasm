// Portions of this invariant-only numeric implementation are adapted from
// dotnet/runtime System.Private.CoreLib Number.Parsing.cs and
// Number.Formatting.cs. The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
//
// Integer formatting is ported separately in Number.IntegerFormatting.cs.
// The remaining parsing and floating-point support here uses invariant culture.
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;

namespace System
{
    internal static partial class Number
    {
        internal static bool TryParseUnsigned(
            string? text,
            int bits,
            bool hexadecimal,
            out ulong value,
            out bool overflow)
        {
            value = 0;
            overflow = false;
            if (!TryBounds(text, out var start, out var end))
            {
                return false;
            }
            var source = text!;
            var negative = false;
            if (!hexadecimal && (source[start] is '+' or '-'))
            {
                negative = source[start] == '-';
                start++;
            }
            if (start == end)
            {
                return false;
            }
            var maximum = negative
                ? 0
                : bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
            var radix = hexadecimal ? 16UL : 10UL;
            var overflowed = false;
            for (var index = start; index < end; index++)
            {
                var digit = Digit(source[index], hexadecimal);
                if (digit < 0)
                {
                    value = 0;
                    return false;
                }
                if (overflowed)
                {
                    continue;
                }
                if (value > maximum / radix ||
                    (value == maximum / radix && (ulong)digit > maximum % radix))
                {
                    overflowed = true;
                    continue;
                }
                value = value * radix + (ulong)digit;
            }
            if (overflowed)
            {
                value = 0;
                overflow = true;
                return false;
            }
            return true;
        }

        internal static bool TryParseSigned(
            string? text,
            int bits,
            bool hexadecimal,
            out long value,
            out bool overflow)
        {
            value = 0;
            overflow = false;
            if (hexadecimal)
            {
                if (!TryParseUnsigned(text, bits, true, out var raw, out overflow))
                {
                    return false;
                }
                if (bits == 64)
                {
                    value = unchecked((long)raw);
                }
                else
                {
                    var sign = 1UL << (bits - 1);
                    value = (raw & sign) == 0 ? (long)raw : (long)(raw | ~((1UL << bits) - 1));
                }
                return true;
            }
            if (!TryBounds(text, out var start, out var end))
            {
                return false;
            }
            var negative = text![start] == '-';
            if (negative || text[start] == '+')
            {
                start++;
            }
            if (start == end)
            {
                return false;
            }
            var positiveMaximum = bits == 64 ? (ulong)long.MaxValue : (1UL << (bits - 1)) - 1;
            var maximum = negative ? positiveMaximum + 1 : positiveMaximum;
            var magnitude = 0UL;
            var overflowed = false;
            for (var index = start; index < end; index++)
            {
                var digit = Digit(text[index], false);
                if (digit < 0)
                {
                    value = 0;
                    return false;
                }
                if (overflowed)
                {
                    continue;
                }
                if (magnitude > maximum / 10 ||
                    (magnitude == maximum / 10 && (ulong)digit > maximum % 10))
                {
                    overflowed = true;
                    continue;
                }
                magnitude = magnitude * 10 + (ulong)digit;
            }
            if (overflowed)
            {
                overflow = true;
                return false;
            }
            value = negative
                ? magnitude == 1UL << (bits - 1) && bits == 64
                    ? long.MinValue
                    : -(long)magnitude
                : (long)magnitude;
            return true;
        }

        // ToString() with no format: the shortest round-trip form, without the
        // format string machinery.
        internal static string FormatSingle(float value) =>
            FormatShortestFloating<float>(BitConverter.SingleToUInt32Bits(value));

        internal static string FormatDouble(double value) =>
            FormatShortestFloating<double>(BitConverter.DoubleToUInt64Bits(value));

        internal static string FormatSingle(float value, string? format) =>
            FormatFloating<float>(BitConverter.SingleToUInt32Bits(value), format);

        internal static string FormatDouble(double value, string? format) =>
            FormatFloating<double>(BitConverter.DoubleToUInt64Bits(value), format);

        internal static bool TryParseSingle(string? text, out float value)
        {
            if (text == null)
            {
                value = default;
                return false;
            }

            return TryParseFloat(
                text.AsSpan(),
                Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands,
                Globalization.NumberFormatInfo.InvariantInfo,
                out value,
                out _);
        }

        internal static bool TryParseDouble(string? text, out double value)
        {
            if (text == null)
            {
                value = default;
                return false;
            }

            return TryParseFloat(
                text.AsSpan(),
                Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands,
                Globalization.NumberFormatInfo.InvariantInfo,
                out value,
                out _);
        }

        internal static TNumber RoundToDecimalDigits<TNumber>(
            TNumber value,
            int digits,
            MidpointRounding mode)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var bits = TNumber.FloatToBits(value);
            var fractionBits = TNumber.NormalMantissaBits - 1;
            var exponentMask = (1UL << TNumber.ExponentBits) - 1;
            var exponent = (int)((bits >> fractionBits) & exponentMask);
            var fraction = bits & ((1UL << fractionBits) - 1);
            if (exponent == (int)exponentMask || exponent == 0 && fraction == 0 ||
                digits > -TNumber.MinDecimalExponent + 2)
            {
                return value;
            }

            var negative = (bits >> (fractionBits + TNumber.ExponentBits)) != 0;
            var mantissa = exponent == 0 ? fraction : fraction | 1UL << fractionBits;
            var binaryPower = exponent == 0
                ? TNumber.MinBinaryExponent - fractionBits
                : exponent - TNumber.ExponentBias - fractionBits;
            var numerator = new BigInteger(mantissa);
            var denominator = BigInteger.One;
            if (binaryPower >= 0) numerator <<= binaryPower;
            else denominator <<= -binaryPower;

            var decimalScale = BigInteger.Pow(new BigInteger(10), digits);
            numerator *= decimalScale;
            var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
            if (remainder != BigInteger.Zero)
            {
                var comparison = (remainder << 1).CompareTo(denominator);
                var roundUp = mode switch
                {
                    MidpointRounding.ToEven => comparison > 0 || comparison == 0 && !quotient.IsEven,
                    MidpointRounding.AwayFromZero => comparison >= 0,
                    MidpointRounding.ToZero => false,
                    MidpointRounding.ToNegativeInfinity => negative,
                    MidpointRounding.ToPositiveInfinity => !negative,
                    _ => false,
                };
                if (roundUp) quotient += BigInteger.One;
            }

            var sign = negative ? 1UL << (fractionBits + TNumber.ExponentBits) : 0;
            if (quotient.IsZero) return TNumber.BitsToFloat(sign);
            var roundedDigits = quotient.ToString().ToCharArray();
            var roundedBits = ConvertDecimalToFloatingBits<TNumber>(
                roundedDigits,
                roundedDigits.Length,
                -digits,
                negative);
            return TNumber.BitsToFloat(roundedBits);
        }

        private static char ParseFloatingFormat(string? format, out int precision)
            => ParseFloatingFormat(format.AsSpan(), out precision);

        private static char ParseFloatingFormat(ReadOnlySpan<char> format, out int precision)
        {
            precision = -1;
            if (format.Length == 0)
            {
                return 'G';
            }

            var symbol = format[0];
            if (symbol is not ('G' or 'g' or 'R' or 'r' or 'F' or 'f' or 'E' or 'e'))
            {
                throw new FormatException();
            }
            if (format.Length > 1)
            {
                precision = 0;
                for (var index = 1; index < format.Length; index++)
                {
                    var digit = format[index] - '0';
                    if ((uint)digit > 9u || precision > 99)
                    {
                        throw new FormatException();
                    }
                    precision = precision * 10 + digit;
                }
            }
            if (precision > 999 || (symbol is 'R' or 'r') && precision >= 0) throw new FormatException();
            return symbol;
        }

        private static string FormatFixedFloating(ulong bits, bool single, int precision) => single
            ? FormatFixedFloating<float>(bits, precision)
            : FormatFixedFloating<double>(bits, precision);

        private static string FormatFixedFloating<TNumber>(ulong bits, int precision)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var builder = new ValueStringBuilder(32);
            AppendFixedFloating<TNumber>(ref builder, bits, precision);
            return builder.ToString();
        }

        private static void AppendFixedFloating<TNumber>(
            scoped ref ValueStringBuilder builder,
            ulong bits,
            int precision)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var fractionBits = TNumber.NormalMantissaBits - 1;
            var signMask = 1UL << (fractionBits + TNumber.ExponentBits);
            var exponentMask = ((1UL << TNumber.ExponentBits) - 1) << fractionBits;
            var fractionMask = (1UL << fractionBits) - 1;
            var negative = (bits & signMask) != 0;
            var magnitude = bits & ~signMask;
            if ((magnitude & exponentMask) == exponentMask)
            {
                AppendShortestFloating<TNumber>(ref builder, bits);
                return;
            }

            if ((magnitude & (exponentMask | fractionMask)) == 0)
            {
                AppendFixedDigits(ref builder, [], 0, 0, negative, precision);
                return;
            }

            GetFloatingRational(
                magnitude,
                out var numerator,
                out var denominator,
                TNumber.ExponentBits,
                TNumber.ExponentBias,
                TNumber.NormalMantissaBits);
            var decimalExponent = 0;
            if (CompareToPower10(numerator, denominator, 0) >= 0)
            {
                while (CompareToPower10(
                    numerator,
                    denominator,
                    decimalExponent + 1) >= 0)
                {
                    decimalExponent++;
                }
            }
            else
            {
                while (CompareToPower10(
                    numerator,
                    denominator,
                    decimalExponent) < 0)
                {
                    decimalExponent--;
                }
            }

            var significantPrecision = decimalExponent + precision + 1;
            if (significantPrecision <= 0)
            {
                if (decimalExponent < -precision - 1 ||
                    !RoundsSmallestFixedUnitUp(
                        numerator,
                        denominator,
                        precision))
                {
                    AppendFixedDigits(ref builder, [], 0, 0, negative, precision);
                    return;
                }

                AppendFixedDigits(
                    ref builder,
                    ['1'],
                    1,
                    -precision,
                    negative,
                    precision);
                return;
            }

            var digits = GenerateRoundedDigits(
                numerator,
                denominator,
                decimalExponent,
                significantPrecision,
                out var resultExponent,
                out var resultLength);
            AppendFixedDigits(
                ref builder,
                digits,
                resultLength,
                resultExponent,
                negative,
                precision);
        }

        private static bool RoundsSmallestFixedUnitUp(
            FloatingBigInteger numerator,
            FloatingBigInteger denominator,
            int precision)
        {
            var scaledNumerator = numerator.Clone();
            scaledNumerator.MultiplyPower10(precision + 1);
            var midpoint = denominator.Clone();
            midpoint.Multiply(5u);
            return FloatingBigInteger.Compare(scaledNumerator, midpoint) > 0;
        }

        private static string FormatFixedDigits(
            char[] digits,
            int digitCount,
            int decimalExponent,
            bool negative,
            int precision)
        {
            var builder = new ValueStringBuilder(32);
            AppendFixedDigits(ref builder, digits, digitCount, decimalExponent, negative, precision);
            return builder.ToString();
        }

        private static void AppendFixedDigits(
            scoped ref ValueStringBuilder builder,
            char[] digits,
            int digitCount,
            int decimalExponent,
            bool negative,
            int precision)
        {
            var integerDigitCount = decimalExponent >= 0 ? decimalExponent + 1 : 1;
            if (negative)
            {
                builder.Append('-');
            }

            for (var power = integerDigitCount - 1; power >= 0; power--)
            {
                builder.Append(ReadFixedDigit(
                    digits,
                    digitCount,
                    decimalExponent,
                    power));
            }

            if (precision != 0)
            {
                builder.Append('.');
                for (var power = -1; power >= -precision; power--)
                {
                    builder.Append(ReadFixedDigit(
                        digits,
                        digitCount,
                        decimalExponent,
                        power));
                }
            }
        }

        private static char ReadFixedDigit(
            char[] digits,
            int digitCount,
            int decimalExponent,
            int power)
        {
            var index = decimalExponent - power;
            return (uint)index < (uint)digitCount ? digits[index] : '0';
        }

        private static string FormatFloating(ulong bits, bool single, string? format) => single
            ? FormatFloating<float>(bits, format)
            : FormatFloating<double>(bits, format);

        internal static string FormatShortestFloating<TNumber>(ulong bits)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var builder = new ValueStringBuilder(32);
            AppendShortestFloating<TNumber>(ref builder, bits);
            return builder.ToString();
        }

        private static string FormatFloating<TNumber>(ulong bits, string? format)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var builder = new ValueStringBuilder(32);
            AppendFloating<TNumber>(ref builder, bits, format.AsSpan());
            return builder.ToString();
        }

        internal static void AppendFloating<TNumber>(
            scoped ref ValueStringBuilder builder,
            ulong bits,
            scoped ReadOnlySpan<char> format)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var symbol = ParseInvariantFormatSpecifier(format, out var precision);
            var customFormat = symbol == '\0';
            if (!customFormat)
            {
                symbol = ParseFloatingFormat(format, out precision);
            }
            if (symbol is 'F' or 'f')
            {
                AppendFixedFloating<TNumber>(ref builder, bits, precision < 0 ? 2 : precision);
                return;
            }
            var lowerExponent = symbol is 'g' or 'r' or 'e';
            if (!customFormat && symbol is not ('E' or 'e') && !(precision > 0 && symbol is 'G' or 'g'))
            {
                AppendRoundTripFloating<TNumber>(
                    ref builder, bits, precision > 0 ? precision : TNumber.MaxRoundTripDigits, lowerExponent);
                return;
            }
            var fractionBits = TNumber.NormalMantissaBits - 1;
            var exponentBits = TNumber.ExponentBits;
            var exponentMask = (1UL << exponentBits) - 1;
            var fractionMask = (1UL << fractionBits) - 1;
            var exponent = (bits >> fractionBits) & exponentMask;
            var fraction = bits & fractionMask;
            if (exponent == exponentMask)
            {
                builder.Append(fraction != 0 ? "NaN" :
                    (bits >> (fractionBits + exponentBits)) != 0 ? "-Infinity" : "Infinity");
                return;
            }
            var negative = (bits >> (fractionBits + exponentBits)) != 0;
            if (exponent == 0 && fraction == 0)
            {
                if (customFormat)
                {
                    AppendCustomInvariant(ref builder, [], 0, negative, NumberBufferKind.FloatingPoint, format);
                    return;
                }
                if (symbol is 'E' or 'e')
                {
                    AppendScientificDigits(ref builder, ['0'], 1, 0, negative, precision < 0 ? 6 : precision, lowerExponent);
                }
                else
                {
                    builder.Append(negative ? "-0" : "0");
                }
                return;
            }

            var decimalExponent = GetFloatingDecimal<TNumber>(bits, out var numerator, out var denominator);

            if (customFormat)
            {
                var customDigits = GenerateRoundedDigits(
                    numerator,
                    denominator,
                    decimalExponent,
                    TNumber.MaxPrecisionCustomFormat,
                    out var customExponent,
                    out var customLength);
                AppendCustomInvariant(
                    ref builder,
                    customDigits.AsSpan(0, customLength),
                    customExponent + 1,
                    negative,
                    NumberBufferKind.FloatingPoint,
                    format);
                return;
            }

            if (symbol is 'E' or 'e')
            {
                var fractionalDigits = precision < 0 ? 6 : precision;
                var scientificDigits = GenerateRoundedDigits(
                    numerator,
                    denominator,
                    decimalExponent,
                    fractionalDigits + 1,
                    out var scientificExponent,
                    out var scientificLength);
                AppendScientificDigits(
                    ref builder,
                    scientificDigits,
                    scientificLength,
                    scientificExponent,
                    negative,
                    fractionalDigits,
                    lowerExponent);
                return;
            }

            var generalDigits = GenerateRoundedDigits(
                numerator,
                denominator,
                decimalExponent,
                precision,
                out var generalExponent,
                out var generalLength);
            AppendGeneralDigits(
                ref builder,
                generalDigits,
                generalLength,
                generalExponent,
                negative,
                precision,
                lowerExponent);
        }

        // The shortest digits that round-trip, as ToString() writes them. It takes
        // no format string, so a program that never passes one does not reach the
        // custom format parser and formatter.
        internal static void AppendShortestFloating<TNumber>(scoped ref ValueStringBuilder builder, ulong bits)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber> =>
            AppendRoundTripFloating<TNumber>(ref builder, bits, TNumber.MaxRoundTripDigits, lowerExponent: false);

        // The fewest digits, up to maxDigits, that convert back to the same value.
        private static void AppendRoundTripFloating<TNumber>(
            scoped ref ValueStringBuilder builder,
            ulong bits,
            int maxDigits,
            bool lowerExponent)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var fractionBits = TNumber.NormalMantissaBits - 1;
            var exponentBits = TNumber.ExponentBits;
            var exponentMask = (1UL << exponentBits) - 1;
            var exponent = (bits >> fractionBits) & exponentMask;
            var fraction = bits & ((1UL << fractionBits) - 1);
            if (exponent == exponentMask)
            {
                builder.Append(fraction != 0 ? "NaN" :
                    (bits >> (fractionBits + exponentBits)) != 0 ? "-Infinity" : "Infinity");
                return;
            }
            var negative = (bits >> (fractionBits + exponentBits)) != 0;
            if (exponent == 0 && fraction == 0)
            {
                builder.Append(negative ? "-0" : "0");
                return;
            }

            var decimalExponent = GetFloatingDecimal<TNumber>(bits, out var numerator, out var denominator);
            var selected = new char[maxDigits];
            var selectedLength = maxDigits;
            for (var candidatePrecision = 1; candidatePrecision <= maxDigits; candidatePrecision++)
            {
                var candidate = GenerateRoundedDigits(
                    numerator,
                    denominator,
                    decimalExponent,
                    candidatePrecision,
                    out var candidateExponent,
                    out var candidateLength);
                var candidateBits = ConvertDecimalToFloatingBits<TNumber>(
                    candidate,
                    candidateLength,
                    candidateExponent - candidateLength + 1,
                    negative);
                if (candidateBits != bits) continue;
                selected = candidate;
                selectedLength = candidateLength;
                decimalExponent = candidateExponent;
                break;
            }
            AppendGeneralDigits(
                ref builder,
                selected,
                selectedLength,
                decimalExponent,
                negative,
                maxDigits,
                lowerExponent);
        }

        // The value as numerator / denominator, and the exponent of its leading
        // decimal digit.
        private static int GetFloatingDecimal<TNumber>(
            ulong bits,
            out FloatingBigInteger numerator,
            out FloatingBigInteger denominator)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            GetFloatingRational(
                bits,
                out numerator,
                out denominator,
                TNumber.ExponentBits,
                TNumber.ExponentBias,
                TNumber.NormalMantissaBits);
            var binaryExponent = FloorBinaryExponent(numerator, denominator);
            var decimalExponent = (int)(binaryExponent * 30_103L / 100_000L);
            while (CompareToPower10(numerator, denominator, decimalExponent) < 0)
            {
                decimalExponent--;
            }
            while (CompareToPower10(numerator, denominator, decimalExponent + 1) >= 0)
            {
                decimalExponent++;
            }
            return decimalExponent;
        }

        private static string FormatScientificDigits(
            char[] digits,
            int digitCount,
            int decimalExponent,
            bool negative,
            int fractionalDigits,
            bool lowerExponent)
        {
            var builder = new ValueStringBuilder(32);
            AppendScientificDigits(ref builder, digits, digitCount, decimalExponent, negative, fractionalDigits, lowerExponent);
            return builder.ToString();
        }

        private static void AppendScientificDigits(
            scoped ref ValueStringBuilder builder,
            char[] digits,
            int digitCount,
            int decimalExponent,
            bool negative,
            int fractionalDigits,
            bool lowerExponent)
        {
            var exponentMagnitude = decimalExponent < 0 ? -decimalExponent : decimalExponent;
            var exponentDigits = 3;
            for (var remaining = exponentMagnitude; remaining >= 1000; remaining /= 10) exponentDigits++;
            if (negative) builder.Append('-');
            builder.Append(digits[0]);
            if (fractionalDigits != 0)
            {
                builder.Append('.');
                for (var index = 0; index < fractionalDigits; index++)
                {
                    builder.Append(index + 1 < digitCount ? digits[index + 1] : '0');
                }
            }
            builder.Append(lowerExponent ? 'e' : 'E');
            builder.Append(decimalExponent < 0 ? '-' : '+');
            Span<char> exponentBuffer = stackalloc char[10];
            var position = exponentBuffer.Length;
            for (var index = exponentDigits - 1; index >= 0; index--)
            {
                exponentBuffer[--position] = (char)('0' + exponentMagnitude % 10);
                exponentMagnitude /= 10;
            }
            builder.Append(exponentBuffer[position..]);
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

        private static int CompareToPower10(
            FloatingBigInteger numerator,
            FloatingBigInteger denominator,
            int exponent)
        {
            var left = numerator.Clone();
            var right = denominator.Clone();
            if (exponent >= 0) right.MultiplyPower10(exponent);
            else left.MultiplyPower10(-exponent);
            return FloatingBigInteger.Compare(left, right);
        }

        private static char[] GenerateRoundedDigits(
            FloatingBigInteger numerator,
            FloatingBigInteger denominator,
            int decimalExponent,
            int precision,
            out int resultExponent,
            out int resultLength)
        {
            var scaledNumerator = numerator.Clone();
            var scaledDenominator = denominator.Clone();
            if (decimalExponent >= 0) scaledDenominator.MultiplyPower10(decimalExponent);
            else scaledNumerator.MultiplyPower10(-decimalExponent);
            var digits = new char[precision];
            var current = scaledNumerator;
            for (var index = 0; index < precision; index++)
            {
                var digit = FloatingBigInteger.DivideToUInt64(
                    current,
                    scaledDenominator,
                    out var remainder);
                digits[index] = (char)('0' + digit);
                current = remainder;
                if (index + 1 < precision) current.Multiply(10);
            }
            var twice = current.Clone();
            twice.Multiply(2);
            var comparison = FloatingBigInteger.Compare(twice, scaledDenominator);
            if (comparison > 0 || comparison == 0 && ((digits[precision - 1] - '0') & 1) != 0)
            {
                var index = precision - 1;
                while (index >= 0 && digits[index] == '9')
                {
                    digits[index--] = '0';
                }
                if (index < 0)
                {
                    digits[0] = '1';
                    decimalExponent++;
                }
                else
                {
                    digits[index]++;
                }
            }
            resultLength = precision;
            while (resultLength > 1 && digits[resultLength - 1] == '0') resultLength--;
            resultExponent = decimalExponent;
            return digits;
        }

        private static void GetFloatingRational(
            ulong bits,
            out FloatingBigInteger numerator,
            out FloatingBigInteger denominator,
            int exponentBits,
            int bias,
            int normalMantissaBits)
        {
            var fractionBits = normalMantissaBits - 1;
            var fractionMask = (1UL << fractionBits) - 1;
            var exponent = (int)((bits >> fractionBits) & ((1UL << exponentBits) - 1));
            var fraction = bits & fractionMask;
            var mantissa = exponent == 0 ? fraction : fraction | 1UL << fractionBits;
            var power = exponent == 0
                ? 1 - bias - fractionBits
                : exponent - bias - fractionBits;
            numerator = new FloatingBigInteger(mantissa);
            denominator = new FloatingBigInteger(1U);
            if (power >= 0) numerator.ShiftLeft(power);
            else denominator.ShiftLeft(-power);
        }

        private static string FormatGeneralDigits(
            char[] digits,
            int digitCount,
            int decimalExponent,
            bool negative,
            int maxRoundTripDigits,
            bool lowerExponent)
        {
            var builder = new ValueStringBuilder(32);
            AppendGeneralDigits(ref builder, digits, digitCount, decimalExponent, negative, maxRoundTripDigits, lowerExponent);
            return builder.ToString();
        }

        private static void AppendGeneralDigits(
            scoped ref ValueStringBuilder builder,
            char[] digits,
            int digitCount,
            int decimalExponent,
            bool negative,
            int maxRoundTripDigits,
            bool lowerExponent)
        {
            var scientific = decimalExponent < -4 || decimalExponent >= maxRoundTripDigits;
            if (!scientific)
            {
                if (negative) builder.Append('-');
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
                        builder.Append('.');
                        for (var index = wholeDigits; index < digitCount; index++) builder.Append(digits[index]);
                    }
                    return;
                }
                var zeros = -decimalExponent - 1;
                builder.Append('0');
                builder.Append('.');
                builder.Append('0', zeros);
                builder.Append(digits.AsSpan(0, digitCount));
                return;
            }

            var absoluteExponent = decimalExponent < 0 ? -decimalExponent : decimalExponent;
            var exponentDigits = absoluteExponent >= 100 ? 3 : 2;
            if (negative) builder.Append('-');
            builder.Append(digits[0]);
            if (digitCount > 1)
            {
                builder.Append('.');
                builder.Append(digits.AsSpan(1, digitCount - 1));
            }
            builder.Append(lowerExponent ? 'e' : 'E');
            builder.Append(decimalExponent < 0 ? '-' : '+');
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

        private static int Digit(char value, bool hexadecimal)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }
            if (hexadecimal && value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }
            if (hexadecimal && value >= 'A' && value <= 'F')
            {
                return value - 'A' + 10;
            }
            return -1;
        }

        private static bool TryBounds(string? text, out int start, out int end)
        {
            start = 0;
            end = text == null ? 0 : text.Length;
            if (text == null)
            {
                return false;
            }
            while (start < end && IsAsciiWhiteSpace(text[start]))
            {
                start++;
            }
            while (end > start && text[end - 1] == '\0')
            {
                end--;
            }
            while (end > start && IsAsciiWhiteSpace(text[end - 1]))
            {
                end--;
            }
            return start != end;
        }

        private static string Slice(char[] source, int start, int length)
        {
            var result = new char[length];
            for (var index = 0; index < length; index++)
            {
                result[index] = source[start + index];
            }
            return string.Create(result);
        }

        // Upstream's ThrowOverflowException<TInteger> reads TInteger.OverflowMessage, which the
        // primitive integers implement with the numeric parsing port; until then Decimal's
        // narrowing conversions throw upstream's messages here.
        [DoesNotReturn]
        internal static void ThrowOverflowException<TInteger>() =>
            throw new OverflowException(
                typeof(TInteger) == typeof(byte) ? SR.Overflow_Byte :
                typeof(TInteger) == typeof(sbyte) ? SR.Overflow_SByte :
                typeof(TInteger) == typeof(short) ? SR.Overflow_Int16 :
                typeof(TInteger) == typeof(ushort) ? SR.Overflow_UInt16 :
                throw new ArgumentException(null, nameof(TInteger)));
    }
}
