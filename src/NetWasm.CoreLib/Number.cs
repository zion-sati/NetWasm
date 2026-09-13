// Portions of this invariant-only numeric implementation are adapted from
// dotnet/runtime System.Private.CoreLib Number.Parsing.cs and
// Number.Formatting.cs. The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
//
// NetWasm deliberately removes culture/provider, UTF-8, span, generic-math and
// runtime-intrinsic branches that are outside its current CoreLib profile.
namespace System
{
    internal static partial class Number
    {
        internal static string FormatUnsigned(ulong value, int bits, string? format)
        {
            var symbol = format == null || format.Length == 0 ? 'G' : format[0];
            var hexadecimal = symbol == 'x' || symbol == 'X';
            var binary = symbol == 'b' || symbol == 'B';
            if (!hexadecimal && !binary &&
                symbol != 'g' && symbol != 'G' &&
                symbol != 'd' && symbol != 'D')
            {
                throw new FormatException();
            }
            var precision = ParseFormatPrecision(format);
            var upper = hexadecimal && symbol == 'X';
            var radix = hexadecimal ? 16UL : binary ? 2UL : 10UL;
            var naturalMaximum = hexadecimal ? bits / 4 : binary ? bits : 20;
            var buffer = new char[Math.Max(naturalMaximum, precision)];
            var index = buffer.Length;
            do
            {
                var digit = (int)(value % radix);
                buffer[--index] = digit < 10
                    ? (char)('0' + digit)
                    : (char)((upper ? 'A' : 'a') + digit - 10);
                value /= radix;
            }
            while (value != 0);
            while (buffer.Length - index < precision)
            {
                buffer[--index] = '0';
            }
            return Slice(buffer, index, buffer.Length - index);
        }

        internal static string FormatSigned(long value, int bits, string? format)
        {
            var hexadecimalOrBinary = format != null && format.Length != 0 &&
                (format[0] == 'x' || format[0] == 'X' ||
                 format[0] == 'b' || format[0] == 'B');
            if (hexadecimalOrBinary)
            {
                var masked = bits == 64 ? (ulong)value : (ulong)value & ((1UL << bits) - 1);
                return FormatUnsigned(masked, bits, format);
            }
            var negative = value < 0;
            var magnitude = negative ? unchecked((ulong)(-(value + 1)) + 1) : (ulong)value;
            var result = FormatUnsigned(magnitude, bits, format);
            return negative ? string.Concat("-", result) : result;
        }

        private static int ParseFormatPrecision(string? format)
        {
            if (format == null || format.Length <= 1)
            {
                return 0;
            }
            var precision = 0;
            for (var index = 1; index < format.Length; index++)
            {
                var character = format[index];
                if (character < '0' || character > '9' || precision > 99)
                {
                    throw new FormatException();
                }
                precision = precision * 10 + character - '0';
            }
            return precision;
        }

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
            if (!hexadecimal && text![start] == '+')
            {
                start++;
            }
            else if (text![start] == '-')
            {
                return false;
            }
            if (start == end)
            {
                return false;
            }
            var maximum = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
            var radix = hexadecimal ? 16UL : 10UL;
            for (var index = start; index < end; index++)
            {
                var digit = Digit(text[index], hexadecimal);
                if (digit < 0)
                {
                    return false;
                }
                if (value > (maximum - (ulong)digit) / radix)
                {
                    value = 0;
                    overflow = true;
                    return false;
                }
                value = value * radix + (ulong)digit;
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
            for (var index = start; index < end; index++)
            {
                var digit = Digit(text[index], false);
                if (digit < 0)
                {
                    return false;
                }
                if (magnitude > (maximum - (ulong)digit) / 10)
                {
                    overflow = true;
                    return false;
                }
                magnitude = magnitude * 10 + (ulong)digit;
            }
            value = negative
                ? magnitude == 1UL << (bits - 1) && bits == 64
                    ? long.MinValue
                    : -(long)magnitude
                : (long)magnitude;
            return true;
        }

        internal static string FormatSingle(float value, string? format) =>
            TryParseFixedFormat(format, out var precision)
                ? FormatFixedFloating(BitConverter.SingleToUInt32Bits(value), true, precision)
                : FormatFloating(BitConverter.SingleToUInt32Bits(value), true, format);

        internal static string FormatDouble(double value, string? format) =>
            TryParseFixedFormat(format, out var precision)
                ? FormatFixedFloating(BitConverter.DoubleToUInt64Bits(value), false, precision)
                : FormatFloating(BitConverter.DoubleToUInt64Bits(value), false, format);

        internal static bool TryParseSingle(string? text, out float value)
        {
            var valid = TryParseFloating(text, true, out var bits);
            value = BitConverter.UInt32BitsToSingle((uint)bits);
            return valid;
        }

        internal static bool TryParseDouble(string? text, out double value)
        {
            var valid = TryParseFloating(text, false, out var bits);
            value = BitConverter.UInt64BitsToDouble(bits);
            return valid;
        }

        private static bool TryParseFixedFormat(string? format, out int precision)
        {
            precision = 2;
            if (format is null || format.Length == 0 ||
                (format[0] != 'F' && format[0] != 'f'))
            {
                return false;
            }

            if (format.Length == 1)
            {
                return true;
            }

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

            if (precision > 999)
            {
                throw new FormatException();
            }

            return true;
        }

        private static string FormatFixedFloating(ulong bits, bool single, int precision)
        {
            var signMask = single ? 0x80000000UL : 0x8000000000000000UL;
            var exponentMask = single ? 0x7F800000UL : 0x7FF0000000000000UL;
            var fractionMask = single ? 0x007FFFFFUL : 0x000FFFFFFFFFFFFFUL;
            var negative = (bits & signMask) != 0;
            var magnitude = bits & ~signMask;
            if ((magnitude & exponentMask) == exponentMask)
            {
                return FormatFloating(bits, single, null);
            }

            if ((magnitude & (exponentMask | fractionMask)) == 0)
            {
                return FormatFixedDigits([], 0, 0, negative, precision);
            }

            GetFloatingRational(
                magnitude,
                single,
                out var numerator,
                out var denominator);
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
                    return FormatFixedDigits([], 0, 0, negative, precision);
                }

                return FormatFixedDigits(
                    ['1'],
                    1,
                    -precision,
                    negative,
                    precision);
            }

            var digits = GenerateRoundedDigits(
                numerator,
                denominator,
                decimalExponent,
                significantPrecision,
                out var resultExponent,
                out var resultLength);
            return FormatFixedDigits(
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
            var integerDigitCount = decimalExponent >= 0
                ? decimalExponent + 1
                : 1;
            var signLength = negative ? 1 : 0;
            var fractionLength = precision == 0 ? 0 : precision + 1;
            var result = new char[
                signLength + integerDigitCount + fractionLength];
            var destination = 0;
            if (negative)
            {
                result[destination++] = '-';
            }

            for (var power = integerDigitCount - 1; power >= 0; power--)
            {
                result[destination++] = ReadFixedDigit(
                    digits,
                    digitCount,
                    decimalExponent,
                    power);
            }

            if (precision != 0)
            {
                result[destination++] = '.';
                for (var power = -1; power >= -precision; power--)
                {
                    result[destination++] = ReadFixedDigit(
                        digits,
                        digitCount,
                        decimalExponent,
                        power);
                }
            }

            return new string(result);
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

        private static string FormatFloating(ulong bits, bool single, string? format)
        {
            var lowerExponent = false;
            if (format != null && format.Length != 0)
            {
                if (format.Length != 1 || format[0] is not ('G' or 'g' or 'R' or 'r'))
                {
                    throw new FormatException();
                }
                lowerExponent = format[0] is 'g' or 'r';
            }
            var fractionBits = single ? 23 : 52;
            var exponentBits = single ? 8 : 11;
            var exponentMask = (1UL << exponentBits) - 1;
            var fractionMask = (1UL << fractionBits) - 1;
            var exponent = (bits >> fractionBits) & exponentMask;
            var fraction = bits & fractionMask;
            if (exponent == exponentMask)
            {
                if (fraction != 0) return "NaN";
                return (bits >> (fractionBits + exponentBits)) != 0
                    ? "-Infinity"
                    : "Infinity";
            }
            var negative = (bits >> (fractionBits + exponentBits)) != 0;
            if (exponent == 0 && fraction == 0) return negative ? "-0" : "0";

            GetFloatingRational(bits, single, out var numerator, out var denominator);
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

            var maxDigits = single ? 9 : 17;
            var selected = new char[maxDigits];
            var selectedLength = maxDigits;
            for (var precision = 1; precision <= maxDigits; precision++)
            {
                var candidate = GenerateRoundedDigits(
                    numerator,
                    denominator,
                    decimalExponent,
                    precision,
                    out var candidateExponent,
                    out var candidateLength);
                var candidateBits = ConvertDecimalToFloatingBits(
                    candidate,
                    candidateLength,
                    candidateExponent - candidateLength + 1,
                    negative,
                    single);
                if (candidateBits != bits) continue;
                selected = candidate;
                selectedLength = candidateLength;
                decimalExponent = candidateExponent;
                break;
            }
            return FormatGeneralDigits(
                selected,
                selectedLength,
                decimalExponent,
                negative,
                maxDigits,
                lowerExponent);
        }

        private static bool TryParseFloating(string? text, bool single, out ulong bits)
        {
            bits = 0;
            if (text == null) return false;
            var start = 0;
            var end = text.Length;
            while (start < end && IsAsciiWhiteSpace(text[start])) start++;
            while (end > start && IsAsciiWhiteSpace(text[end - 1])) end--;
            if (start == end) return false;
            var negative = false;
            if (text[start] is '+' or '-')
            {
                negative = text[start] == '-';
                start++;
                if (start == end) return false;
            }
            if (EqualsIgnoreCase(text, start, end, "NaN"))
            {
                bits = single ? 0x7fc0_0000UL : 0x7ff8_0000_0000_0000UL;
                return true;
            }
            if (EqualsIgnoreCase(text, start, end, "Infinity"))
            {
                bits = single ? 0x7f80_0000UL : 0x7ff0_0000_0000_0000UL;
                if (negative) bits |= single ? 0x8000_0000UL : 0x8000_0000_0000_0000UL;
                return true;
            }

            var digits = new char[end - start];
            var digitCount = 0;
            var fractionalDigits = 0;
            var decimalPoint = false;
            var sawDigit = false;
            while (start < end)
            {
                var value = text[start];
                if (value >= '0' && value <= '9')
                {
                    digits[digitCount++] = value;
                    if (decimalPoint) fractionalDigits++;
                    sawDigit = true;
                    start++;
                    continue;
                }
                if (value == '.' && !decimalPoint)
                {
                    decimalPoint = true;
                    start++;
                    continue;
                }
                break;
            }
            if (!sawDigit) return false;
            var explicitExponent = 0L;
            if (start < end && text[start] is 'e' or 'E')
            {
                start++;
                var exponentNegative = false;
                if (start < end && text[start] is '+' or '-')
                {
                    exponentNegative = text[start] == '-';
                    start++;
                }
                if (start == end || text[start] < '0' || text[start] > '9') return false;
                while (start < end && text[start] >= '0' && text[start] <= '9')
                {
                    if (explicitExponent < 1_000_000)
                    {
                        explicitExponent = explicitExponent * 10 + text[start] - '0';
                    }
                    start++;
                }
                if (exponentNegative) explicitExponent = -explicitExponent;
            }
            if (start != end) return false;

            var first = 0;
            while (first < digitCount && digits[first] == '0') first++;
            if (first == digitCount)
            {
                bits = negative
                    ? single ? 0x8000_0000UL : 0x8000_0000_0000_0000UL
                    : 0;
                return true;
            }
            var last = digitCount;
            while (last > first && digits[last - 1] == '0') last--;
            var exponent10 = explicitExponent - fractionalDigits + digitCount - last;
            var significant = new char[last - first];
            for (var index = 0; index < significant.Length; index++)
            {
                significant[index] = digits[first + index];
            }
            bits = ConvertDecimalToFloatingBits(
                significant,
                significant.Length,
                exponent10,
                negative,
                single);
            return true;
        }

        private static ulong ConvertDecimalToFloatingBits(
            char[] digits,
            int digitCount,
            long exponent10,
            bool negative,
            bool single)
        {
            var sign = negative
                ? single ? 0x8000_0000UL : 0x8000_0000_0000_0000UL
                : 0;
            if (digitCount == 0) return sign;
            var decimalExponent = exponent10 + digitCount - 1;
            var maximumDecimalExponent = single ? 39 : 309;
            var minimumDecimalExponent = single ? -60 : -400;
            if (decimalExponent > maximumDecimalExponent)
            {
                return sign | (single ? 0x7f80_0000UL : 0x7ff0_0000_0000_0000UL);
            }
            if (decimalExponent < minimumDecimalExponent) return sign;

            var numerator = new FloatingBigInteger(0U);
            for (var index = 0; index < digitCount; index++)
            {
                numerator.Multiply(10);
                numerator.Add((uint)(digits[index] - '0'));
            }
            var denominator = new FloatingBigInteger(1U);
            if (exponent10 > 0) numerator.MultiplyPower10((int)exponent10);
            if (exponent10 < 0) denominator.MultiplyPower10((int)-exponent10);

            var fractionBits = single ? 23 : 52;
            var bias = single ? 127 : 1023;
            var minimumNormalExponent = single ? -126 : -1022;
            var binaryExponent = FloorBinaryExponent(numerator, denominator);
            if (binaryExponent > bias)
            {
                return sign | (single ? 0x7f80_0000UL : 0x7ff0_0000_0000_0000UL);
            }
            if (binaryExponent >= minimumNormalExponent)
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
                    if (binaryExponent > bias)
                    {
                        return sign | (single ? 0x7f80_0000UL : 0x7ff0_0000_0000_0000UL);
                    }
                }
                var encodedExponent = (ulong)(binaryExponent + bias);
                var fractionMask = (1UL << fractionBits) - 1;
                return sign | encodedExponent << fractionBits | significand & fractionMask;
            }

            var subnormalShift = single ? 149 : 1074;
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
            bool single,
            out FloatingBigInteger numerator,
            out FloatingBigInteger denominator)
        {
            var fractionBits = single ? 23 : 52;
            var exponentBits = single ? 8 : 11;
            var bias = single ? 127 : 1023;
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
            var scientific = decimalExponent < -4 || decimalExponent >= maxRoundTripDigits;
            var signLength = negative ? 1 : 0;
            if (!scientific)
            {
                if (decimalExponent >= 0)
                {
                    var wholeDigits = decimalExponent + 1;
                    var fractionDigits = digitCount > wholeDigits ? digitCount - wholeDigits : 0;
                    var result = new char[signLength + wholeDigits + (fractionDigits == 0 ? 0 : fractionDigits + 1)];
                    var target = 0;
                    if (negative) result[target++] = '-';
                    for (var index = 0; index < wholeDigits; index++)
                    {
                        result[target++] = index < digitCount ? digits[index] : '0';
                    }
                    if (fractionDigits != 0)
                    {
                        result[target++] = '.';
                        for (var index = wholeDigits; index < digitCount; index++) result[target++] = digits[index];
                    }
                    return string.Create(result);
                }
                var zeros = -decimalExponent - 1;
                var fractionalResult = new char[signLength + 2 + zeros + digitCount];
                var fractionalPosition = 0;
                if (negative) fractionalResult[fractionalPosition++] = '-';
                fractionalResult[fractionalPosition++] = '0';
                fractionalResult[fractionalPosition++] = '.';
                for (var index = 0; index < zeros; index++) fractionalResult[fractionalPosition++] = '0';
                for (var index = 0; index < digitCount; index++) fractionalResult[fractionalPosition++] = digits[index];
                return string.Create(fractionalResult);
            }

            var absoluteExponent = decimalExponent < 0 ? -decimalExponent : decimalExponent;
            var exponentDigits = absoluteExponent >= 100 ? 3 : 2;
            var fractionLength = digitCount > 1 ? digitCount : 0;
            var scientificResult = new char[
                signLength + 1 + (fractionLength == 0 ? 0 : fractionLength) + 2 + exponentDigits];
            var position = 0;
            if (negative) scientificResult[position++] = '-';
            scientificResult[position++] = digits[0];
            if (fractionLength != 0)
            {
                scientificResult[position++] = '.';
                for (var index = 1; index < digitCount; index++) scientificResult[position++] = digits[index];
            }
            scientificResult[position++] = lowerExponent ? 'e' : 'E';
            scientificResult[position++] = decimalExponent < 0 ? '-' : '+';
            for (var index = exponentDigits - 1; index >= 0; index--)
            {
                scientificResult[position + index] = (char)('0' + absoluteExponent % 10);
                absoluteExponent /= 10;
            }
            return string.Create(scientificResult);
        }

        private static bool EqualsIgnoreCase(
            string value,
            int start,
            int end,
            string expected)
        {
            if (end - start != expected.Length) return false;
            for (var index = 0; index < expected.Length; index++)
            {
                var actual = value[start + index];
                var canonical = expected[index];
                if (actual >= 'A' && actual <= 'Z') actual = (char)(actual + ('a' - 'A'));
                if (canonical >= 'A' && canonical <= 'Z') canonical = (char)(canonical + ('a' - 'A'));
                if (actual != canonical) return false;
            }
            return true;
        }

        private static bool IsAsciiWhiteSpace(char value) =>
            value is ' ' or '\t' or '\r' or '\n' or '\f' or '\v';

        internal static int Compare(long left, long right) => left < right ? -1 : left > right ? 1 : 0;
        internal static int Compare(ulong left, ulong right) => left < right ? -1 : left > right ? 1 : 0;

        internal static bool IsHexadecimal(Globalization.NumberStyles style)
        {
            if (style == Globalization.NumberStyles.Integer)
            {
                return false;
            }
            if (style == Globalization.NumberStyles.HexNumber)
            {
                return true;
            }
            throw new ArgumentException();
        }

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
            while (start < end && text[start] == ' ')
            {
                start++;
            }
            while (end > start && text[end - 1] == ' ')
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
    }
}
