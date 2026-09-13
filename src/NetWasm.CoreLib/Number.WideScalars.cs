// Adapted from dotnet/runtime Number parsing and formatting contracts.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.

using System.Globalization;
using System.Runtime.InteropServices;

namespace System
{
    internal static partial class Number
    {
        // The flag is private in the desktop implementation.  The scalar ports
        // use it to distinguish strict parsing from TryParsePartial.
        internal const NumberStyles AllowTrailingInvalidCharacters = (NumberStyles)0x4000_0000;
        internal const int HalfNumberBufferLength = 16;

        internal enum ParsingStatus
        {
            OK,
            Failed,
            Overflow,
        }

        internal static string Int128ToDecStr(Int128 value) => FormatInt128(value, null, null);
        internal static string UInt128ToDecStr(UInt128 value) => FormatUInt128(value, null, null);

        internal static string FormatInt128(Int128 value, string? format, object? provider) =>
            FormatInteger(value < Int128.Zero, value < Int128.Zero ? (UInt128)(~value + Int128.One) : (UInt128)value, format, signed: true);

        internal static string FormatUInt128(UInt128 value, string? format, object? provider) =>
            FormatInteger(false, value, format, signed: false);

        internal static bool TryFormatInt128(Int128 value, ReadOnlySpan<char> format, object? provider, Span<char> destination, out int charsWritten) =>
            TryCopy(FormatInt128(value, SpanToString(format), provider), destination, out charsWritten);

        internal static bool TryFormatInt128(Int128 value, ReadOnlySpan<char> format, object? provider, Span<byte> destination, out int bytesWritten) =>
            TryCopyUtf8(FormatInt128(value, SpanToString(format), provider), destination, out bytesWritten);

        internal static bool TryFormatUInt128(UInt128 value, ReadOnlySpan<char> format, object? provider, Span<char> destination, out int charsWritten) =>
            TryCopy(FormatUInt128(value, SpanToString(format), provider), destination, out charsWritten);

        internal static bool TryFormatUInt128(UInt128 value, ReadOnlySpan<char> format, object? provider, Span<byte> destination, out int bytesWritten) =>
            TryCopyUtf8(FormatUInt128(value, SpanToString(format), provider), destination, out bytesWritten);

        private static string FormatInteger(bool negative, UInt128 magnitude, string? format, bool signed)
        {
            var symbol = format is null || format.Length == 0 ? 'G' : format[0];
            var hexadecimal = symbol is 'x' or 'X';
            var binary = symbol is 'b' or 'B';
            if (!hexadecimal && !binary && symbol is not ('g' or 'G' or 'd' or 'D'))
            {
                throw new FormatException();
            }

            var precision = ParsePrecision(format);
            var radix = hexadecimal ? 16U : binary ? 2U : 10U;
            var digits = new char[hexadecimal ? 32 : binary ? 128 : 40];
            var index = digits.Length;
            var divisor = new UInt128(0, radix);
            do
            {
                var division = UInt128.DivRem(magnitude, divisor);
                var remainder = division.Remainder.Lower;
                var digit = (int)remainder;
                digits[--index] = digit < 10
                    ? (char)('0' + digit)
                    : (char)((symbol == 'X' ? 'A' : 'a') + digit - 10);
                magnitude = division.Quotient;
            }
            while (magnitude != UInt128.Zero);

            while (digits.Length - index < precision)
            {
                digits[--index] = '0';
            }

            var prefix = negative && signed && !hexadecimal && !binary ? 1 : 0;
            var result = new char[digits.Length - index + prefix];
            var resultIndex = 0;
            if (prefix != 0)
            {
                result[resultIndex++] = '-';
            }
            for (var i = index; i < digits.Length; i++)
            {
                result[resultIndex++] = digits[i];
            }
            return string.Create(result);
        }

        private static int ParsePrecision(string? format)
        {
            if (format is null || format.Length <= 1)
            {
                return 0;
            }
            var precision = 0;
            for (var i = 1; i < format.Length; i++)
            {
                if (format[i] is < '0' or > '9')
                {
                    throw new FormatException();
                }
                precision = precision * 10 + format[i] - '0';
                if (precision > 99)
                {
                    throw new FormatException();
                }
            }
            return precision;
        }

        internal static TNumber ParseBinaryInteger<TChar, TNumber>(ReadOnlySpan<TChar> text, NumberStyles style, object? provider)
            where TChar : unmanaged
            where TNumber : unmanaged, IBinaryIntegerParseAndFormatInfo<TNumber>
        {
            var status = TryParseBinaryInteger(text, style, provider, out TNumber result, out _);
            return status switch
            {
                ParsingStatus.OK => result,
                ParsingStatus.Overflow => throw new OverflowException(),
                _ => throw new FormatException(),
            };
        }

        internal static ParsingStatus TryParseBinaryInteger<TChar, TNumber>(ReadOnlySpan<TChar> text, NumberStyles style, object? provider, out TNumber result, out int charsConsumed)
            where TChar : unmanaged
            where TNumber : unmanaged, IBinaryIntegerParseAndFormatInfo<TNumber>
        {
            var source = SpanToString(text);
            var status = TryParseIntegerText(source, style, TNumber.IsSigned, out var signed, out var unsigned, out charsConsumed);
            if (status == ParsingStatus.OK)
            {
                result = TNumber.IsSigned
                    ? (TNumber)(object)signed
                    : (TNumber)(object)unsigned;
                return status;
            }
            result = default!;
            return status;
        }

        private static ParsingStatus TryParseIntegerText(string source, NumberStyles style, bool signedType, out Int128 signed, out UInt128 unsigned, out int consumed)
        {
            signed = default;
            unsigned = default;
            consumed = 0;
            var start = 0;
            while (start < source.Length && IsWhiteSpace(source[start])) start++;
            var end = source.Length;
            while (end > start && IsWhiteSpace(source[end - 1])) end--;
            if (start == end)
            {
                return ParsingStatus.Failed;
            }

            var negative = false;
            if (source[start] is '+' or '-')
            {
                negative = source[start] == '-';
                start++;
            }
            var radix = (style & NumberStyles.AllowHexSpecifier) != 0 ? 16 :
                (style & NumberStyles.AllowBinarySpecifier) != 0 ? 2 : 10;
            if (start + 2 <= end && source[start] == '0' && ((radix == 16 && (source[start + 1] is 'x' or 'X')) || (radix == 2 && (source[start + 1] is 'b' or 'B'))))
            {
                start += 2;
            }

            var value = UInt128.Zero;
            var sawDigit = false;
            var index = start;
            for (; index < end; index++)
            {
                var digit = Digit(source[index], radix);
                if (digit < 0)
                {
                    break;
                }
                sawDigit = true;
                var next = value * new UInt128(0, (uint)radix) + new UInt128(0, (uint)digit);
                if (next < value)
                {
                    consumed = index;
                    return ParsingStatus.Overflow;
                }
                value = next;
            }
            if (!sawDigit || index != end && (style & AllowTrailingInvalidCharacters) == 0)
            {
                return ParsingStatus.Failed;
            }
            consumed = index;
            if (negative && !signedType)
            {
                return ParsingStatus.Failed;
            }
            if (signedType)
            {
                if ((style & NumberStyles.AllowHexSpecifier) != 0 || (style & NumberStyles.AllowBinarySpecifier) != 0)
                {
                    signed = (Int128)value;
                    return ParsingStatus.OK;
                }
                var maximum = new UInt128(0x7FFF_FFFF_FFFF_FFFF, 0xFFFF_FFFF_FFFF_FFFF);
                var minimumMagnitude = new UInt128(0x8000_0000_0000_0000, 0);
                if ((!negative && value > maximum) || (negative && value > minimumMagnitude))
                {
                    return ParsingStatus.Overflow;
                }
                signed = negative
                    ? value == minimumMagnitude ? Int128.MinValue : -(Int128)value
                    : (Int128)value;
            }
            else
            {
                unsigned = value;
            }
            return ParsingStatus.OK;
        }

        private static int Digit(char value, int radix)
        {
            var digit = value is >= '0' and <= '9' ? value - '0' :
                value is >= 'a' and <= 'f' ? value - 'a' + 10 :
                value is >= 'A' and <= 'F' ? value - 'A' + 10 : -1;
            return digit < radix ? digit : -1;
        }

        private static bool IsWhiteSpace(char value) => value is
            '\u0009' or '\u000A' or '\u000B' or '\u000C' or '\u000D' or
            '\u0020' or '\u0085' or '\u00A0' or '\u1680' or '\u2000' or
            '\u2001' or '\u2002' or '\u2003' or '\u2004' or '\u2005' or
            '\u2006' or '\u2007' or '\u2008' or '\u2009' or '\u200A' or
            '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000';

        internal static void ThrowDecimalOverflowException() => throw new OverflowException();

        internal static string FormatFloat<TNumber>(TNumber value, string? format, object? provider)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            // Number's general formatter operates on binary64.  Decode the
            // source contract first; this keeps Half/BFloat16 formatting on
            // the same path as Single/Double without runtime type checks.
            return FormatDouble(BinaryBitsToDouble<TNumber>(TNumber.FloatToBits(value)), format);
        }

        internal static bool TryFormatFloat<TNumber>(TNumber value, ReadOnlySpan<char> format, object? provider, Span<char> destination, out int charsWritten)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber> =>
            TryCopy(FormatFloat(value, SpanToString(format), provider), destination, out charsWritten);

        internal static bool TryFormatFloat<TNumber>(TNumber value, ReadOnlySpan<char> format, object? provider, Span<byte> destination, out int bytesWritten)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber> =>
            TryCopyUtf8(FormatFloat(value, SpanToString(format), provider), destination, out bytesWritten);

        internal static TNumber ParseFloat<TChar, TNumber>(ReadOnlySpan<TChar> text, NumberStyles style, object? provider)
            where TChar : unmanaged
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            if (!TryParseFloat(text, style, provider, out TNumber result, out _))
            {
                throw new FormatException();
            }
            return result;
        }

        internal static bool TryParseFloat<TChar, TNumber>(ReadOnlySpan<TChar> text, NumberStyles style, object? provider, out TNumber result, out int charsConsumed)
            where TChar : unmanaged
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var source = SpanToString(text);
            if (!TryParseDouble(source, out var parsed))
            {
                result = default!;
                charsConsumed = 0;
                return false;
            }
            result = TNumber.CreateChecked(parsed);
            charsConsumed = source.Length;
            return true;
        }

        private static string SpanToString<TChar>(ReadOnlySpan<TChar> text)
            where TChar : unmanaged
        {
            if (text.IsEmpty)
            {
                return string.Create([]);
            }

            var bytes = MemoryMarshal.Cast<TChar, byte>(text);
            var charWidth = bytes.Length / text.Length;
            var chars = new char[charWidth == 2 ? text.Length : bytes.Length];
            if (charWidth == 2)
            {
                for (var i = 0; i < chars.Length; i++) chars[i] = (char)(bytes[i * 2] | (bytes[(i * 2) + 1] << 8));
            }
            else
            {
                for (var i = 0; i < bytes.Length; i++) chars[i] = (char)bytes[i];
            }
            return string.Create(chars);
        }

        private static double BinaryBitsToDouble<TNumber>(ulong bits)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var exponentBits = TNumber.ExponentBits;
            var fractionBits = TNumber.NormalMantissaBits - 1;
            var exponentMask = (1UL << exponentBits) - 1;
            var fractionMask = (1UL << fractionBits) - 1;
            var exponent = (bits >> fractionBits) & exponentMask;
            var fraction = bits & fractionMask;
            var negative = ((bits >> (fractionBits + exponentBits)) & 1) != 0;

            if (exponent == exponentMask)
            {
                if (fraction != 0) return double.NaN;
                return negative ? double.NegativeInfinity : double.PositiveInfinity;
            }

            if (exponent == 0)
            {
                if (fraction == 0) return negative ? -0.0 : 0.0;
                var subnormal = Math.ScaleB((double)fraction, TNumber.MinBinaryExponent - fractionBits);
                return negative ? -subnormal : subnormal;
            }

            var significand = (double)(fraction | (1UL << fractionBits));
            var normal = Math.ScaleB(significand, (int)exponent - TNumber.ExponentBias - fractionBits);
            return negative ? -normal : normal;
        }

        private static bool TryCopy(string value, Span<char> destination, out int written)
        {
            if (destination.Length < value.Length)
            {
                written = 0;
                return false;
            }
            for (var i = 0; i < value.Length; i++) destination[i] = value[i];
            written = value.Length;
            return true;
        }

        private static bool TryCopyUtf8(string value, Span<byte> destination, out int written)
        {
            if (destination.Length < value.Length)
            {
                written = 0;
                return false;
            }
            for (var i = 0; i < value.Length; i++) destination[i] = (byte)value[i];
            written = value.Length;
            return true;
        }
    }
}
