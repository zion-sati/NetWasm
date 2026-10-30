// Adapted from dotnet/runtime Number parsing and formatting contracts.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.

using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace System
{
    internal static partial class Number
    {
        // The flag is private in the desktop implementation.  The scalar ports
        // use it to distinguish strict parsing from TryParsePartial.
        internal const NumberStyles AllowTrailingInvalidCharacters = (NumberStyles)0x4000_0000;

        internal enum ParsingStatus
        {
            OK,
            Failed,
            Overflow,
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
            var status = TryParseIntegerText(text, style, TNumber.IsSigned, out var signed, out var unsigned, out charsConsumed);
            if (status == ParsingStatus.OK)
            {
                result = TNumber.IsSigned
                    ? (TNumber)(object)signed
                    : (TNumber)(object)unsigned;
                return status;
            }
            result = default!;
            charsConsumed = 0;
            return status;
        }

        internal static long ParseSignedInteger(string? text, int bits, NumberStyles style)
        {
            NumberFormatInfo.ValidateParseStyleInteger(style);
            if (text is null) throw new ArgumentNullException();
            return ParseSignedIntegerCore(text.AsSpan(), bits, style);
        }

        internal static long ParseSignedInteger<TChar>(ReadOnlySpan<TChar> text, int bits, NumberStyles style)
            where TChar : unmanaged
        {
            NumberFormatInfo.ValidateParseStyleInteger(style);
            return ParseSignedIntegerCore(text, bits, style);
        }

        internal static bool TryParseSignedInteger(string? text, int bits, NumberStyles style, out long result)
        {
            NumberFormatInfo.ValidateParseStyleInteger(style);
            if (text is null)
            {
                result = default;
                return false;
            }
            return TryParseSignedIntegerCore(text.AsSpan(), bits, style, out result);
        }

        internal static bool TryParseSignedInteger<TChar>(ReadOnlySpan<TChar> text, int bits, NumberStyles style, out long result)
            where TChar : unmanaged
        {
            NumberFormatInfo.ValidateParseStyleInteger(style);
            return TryParseSignedIntegerCore(text, bits, style, out result);
        }

        internal static ulong ParseUnsignedInteger(string? text, int bits, NumberStyles style)
        {
            NumberFormatInfo.ValidateParseStyleInteger(style);
            if (text is null) throw new ArgumentNullException();
            return ParseUnsignedIntegerCore(text.AsSpan(), bits, style);
        }

        internal static ulong ParseUnsignedInteger<TChar>(ReadOnlySpan<TChar> text, int bits, NumberStyles style)
            where TChar : unmanaged
        {
            NumberFormatInfo.ValidateParseStyleInteger(style);
            return ParseUnsignedIntegerCore(text, bits, style);
        }

        internal static bool TryParseUnsignedInteger(string? text, int bits, NumberStyles style, out ulong result)
        {
            NumberFormatInfo.ValidateParseStyleInteger(style);
            if (text is null)
            {
                result = default;
                return false;
            }
            return TryParseUnsignedIntegerCore(text.AsSpan(), bits, style, out result);
        }

        internal static bool TryParseUnsignedInteger<TChar>(ReadOnlySpan<TChar> text, int bits, NumberStyles style, out ulong result)
            where TChar : unmanaged
        {
            NumberFormatInfo.ValidateParseStyleInteger(style);
            return TryParseUnsignedIntegerCore(text, bits, style, out result);
        }

        private static long ParseSignedIntegerCore<TChar>(ReadOnlySpan<TChar> text, int bits, NumberStyles style)
            where TChar : unmanaged
        {
            var status = TryParseSignedIntegerCore(text, bits, style, out var result, out _);
            return status switch
            {
                ParsingStatus.OK => result,
                ParsingStatus.Overflow => throw new OverflowException(),
                _ => throw new FormatException(),
            };
        }

        private static bool TryParseSignedIntegerCore<TChar>(ReadOnlySpan<TChar> text, int bits, NumberStyles style, out long result)
            where TChar : unmanaged =>
            TryParseSignedIntegerCore(text, bits, style, out result, out _) == ParsingStatus.OK;

        private static ParsingStatus TryParseSignedIntegerCore<TChar>(
            ReadOnlySpan<TChar> text,
            int bits,
            NumberStyles style,
            out long result,
            out int charsConsumed)
            where TChar : unmanaged
        {
            result = default;
            var status = TryParseIntegerText(text, style, signedType: true, out var signed, out var unsigned, out charsConsumed);
            if (status != ParsingStatus.OK) return status;

            var radixStyle = (style & (NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier)) != 0;
            if (radixStyle)
            {
                var maximum = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
                if (unsigned.Upper != 0 || unsigned.Lower > maximum) return ParsingStatus.Overflow;
                var shift = 64 - bits;
                result = shift == 0
                    ? unchecked((long)unsigned.Lower)
                    : unchecked((long)(unsigned.Lower << shift)) >> shift;
                return ParsingStatus.OK;
            }

            var minimum = bits == 64 ? long.MinValue : -(1L << (bits - 1));
            var maximumSigned = bits == 64 ? long.MaxValue : (1L << (bits - 1)) - 1;
            if (signed < (Int128)minimum || signed > (Int128)maximumSigned) return ParsingStatus.Overflow;
            result = (long)signed;
            return ParsingStatus.OK;
        }

        private static ulong ParseUnsignedIntegerCore<TChar>(ReadOnlySpan<TChar> text, int bits, NumberStyles style)
            where TChar : unmanaged
        {
            var status = TryParseUnsignedIntegerCore(text, bits, style, out var result, out _);
            return status switch
            {
                ParsingStatus.OK => result,
                ParsingStatus.Overflow => throw new OverflowException(),
                _ => throw new FormatException(),
            };
        }

        private static bool TryParseUnsignedIntegerCore<TChar>(ReadOnlySpan<TChar> text, int bits, NumberStyles style, out ulong result)
            where TChar : unmanaged =>
            TryParseUnsignedIntegerCore(text, bits, style, out result, out _) == ParsingStatus.OK;

        private static ParsingStatus TryParseUnsignedIntegerCore<TChar>(
            ReadOnlySpan<TChar> text,
            int bits,
            NumberStyles style,
            out ulong result,
            out int charsConsumed)
            where TChar : unmanaged
        {
            result = default;
            var status = TryParseIntegerText(text, style, signedType: false, out _, out var unsigned, out charsConsumed);
            if (status != ParsingStatus.OK) return status;
            var maximum = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
            if (unsigned.Upper != 0 || unsigned.Lower > maximum) return ParsingStatus.Overflow;
            result = unsigned.Lower;
            return ParsingStatus.OK;
        }

        private static ParsingStatus TryParseIntegerText<TChar>(ReadOnlySpan<TChar> source, NumberStyles style, bool signedType, out Int128 signed, out UInt128 unsigned, out int consumed)
            where TChar : unmanaged
        {
            signed = default;
            unsigned = default;
            consumed = 0;

            var start = 0;
            var end = source.Length;
            var allowTrailingInvalid = (style & AllowTrailingInvalidCharacters) != 0;
            if ((style & NumberStyles.AllowLeadingWhite) != 0)
            {
                while (start < end && IsWhiteSpace(ReadSourceChar(source, start))) start++;
            }
            if (start == end)
            {
                return ParsingStatus.Failed;
            }

            var radixFlags = style & (NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier);
            var radix = radixFlags == NumberStyles.AllowHexSpecifier ? 16 :
                radixFlags == NumberStyles.AllowBinarySpecifier ? 2 : 10;
            if (radix != 10)
            {
                return TryParseRadixInteger(source, start, end, radix, signedType, style, out signed, out unsigned, out consumed);
            }

            var negative = false;
            var parenthesized = false;
            var sawSign = false;
            var sawCurrency = false;
            if ((style & NumberStyles.AllowParentheses) != 0 && ReadSourceChar(source, start) == '(')
            {
                negative = true;
                parenthesized = true;
                sawSign = true;
                start++;
            }

            if ((style & NumberStyles.AllowCurrencySymbol) != 0 && start < end && ReadSourceChar(source, start) == '$')
            {
                sawCurrency = true;
                start++;
            }
            if ((style & NumberStyles.AllowLeadingSign) != 0 && start < end && ReadSourceChar(source, start) is '+' or '-')
            {
                if (parenthesized) return ParsingStatus.Failed;
                sawSign = true;
                negative = ReadSourceChar(source, start) == '-';
                start++;
            }
            if ((style & NumberStyles.AllowCurrencySymbol) != 0 && start < end && ReadSourceChar(source, start) == '$')
            {
                if (sawCurrency) return ParsingStatus.Failed;
                sawCurrency = true;
                start++;
            }

            var sawDigit = false;
            var sawDecimalPoint = false;
            var digitsBeforePoint = 0;
            var leadingZeroCount = 0;
            var sawSignificantDigit = false;
            var significantDigitCount = 0;
            var storedDigitCount = 0;
            var nonZeroTail = false;
            Span<byte> digits = stackalloc byte[39];
            var index = start;
            for (; index < end; index++)
            {
                var current = ReadSourceChar(source, index);
                if ((style & NumberStyles.AllowThousands) != 0 && current == ',')
                {
                    if (sawDecimalPoint)
                    {
                        break;
                    }
                    if (!sawDigit)
                    {
                        return ParsingStatus.Failed;
                    }
                    continue;
                }
                if ((style & NumberStyles.AllowDecimalPoint) != 0 && current == '.' && !sawDecimalPoint)
                {
                    sawDecimalPoint = true;
                    continue;
                }
                if ((style & NumberStyles.AllowExponent) != 0 && (current == 'e' || current == 'E')) break;

                var digit = Digit(current, 10);
                if (digit < 0)
                {
                    break;
                }
                sawDigit = true;
                if (!sawDecimalPoint) digitsBeforePoint++;
                if (!sawSignificantDigit && digit == 0)
                {
                    leadingZeroCount++;
                    continue;
                }
                sawSignificantDigit = true;
                significantDigitCount++;
                if (storedDigitCount < digits.Length) digits[storedDigitCount++] = (byte)digit;
                else if (digit != 0) nonZeroTail = true;
            }

            var exponent = 0;
            if (index < end && (ReadSourceChar(source, index) is 'e' or 'E') && (style & NumberStyles.AllowExponent) != 0)
            {
                var exponentStart = index;
                index++;
                var exponentNegative = false;
                if (index < end && ReadSourceChar(source, index) is '+' or '-')
                {
                    exponentNegative = ReadSourceChar(source, index) == '-';
                    index++;
                }
                var exponentDigits = 0;
                while (index < end)
                {
                    var digit = Digit(ReadSourceChar(source, index), 10);
                    if (digit < 0) break;
                    exponent = exponent > 1_000_000 ? 1_000_001 : exponent * 10 + digit;
                    exponentDigits++;
                    index++;
                }
                if (exponentDigits == 0)
                {
                    if (!allowTrailingInvalid) return ParsingStatus.Failed;
                    index = exponentStart;
                    exponent = 0;
                }
                else if (exponentNegative)
                {
                    exponent = -exponent;
                }
            }

            if (!sawDigit) return ParsingStatus.Failed;
            var closedParenthesis = !parenthesized;
            while (index < end)
            {
                var current = ReadSourceChar(source, index);
                if ((style & NumberStyles.AllowTrailingWhite) != 0 && IsWhiteSpace(current))
                {
                    index++;
                    continue;
                }
                if ((style & NumberStyles.AllowTrailingSign) != 0 && !sawSign && current is '+' or '-')
                {
                    sawSign = true;
                    negative = current == '-';
                    index++;
                    continue;
                }
                if (parenthesized && !closedParenthesis && current == ')')
                {
                    closedParenthesis = true;
                    index++;
                    continue;
                }
                if ((style & NumberStyles.AllowCurrencySymbol) != 0 && !sawCurrency && current == '$')
                {
                    sawCurrency = true;
                    index++;
                    continue;
                }
                break;
            }
            if (!closedParenthesis) return ParsingStatus.Failed;
            while (index < end && ReadSourceChar(source, index) == '\0') index++;
            if (index != end && !allowTrailingInvalid) return ParsingStatus.Failed;
            consumed = index;
            if (negative && !signedType)
            {
                return sawSignificantDigit || sawDecimalPoint ? ParsingStatus.Overflow : ParsingStatus.OK;
            }

            if (!sawSignificantDigit)
            {
                return ParsingStatus.OK;
            }

            var scale = (long)digitsBeforePoint + exponent - leadingZeroCount;
            if (scale <= 0 || scale > 39) return ParsingStatus.Overflow;
            if (significantDigitCount > scale)
            {
                var firstFractional = (int)scale;
                for (var digitIndex = firstFractional; digitIndex < storedDigitCount; digitIndex++)
                {
                    if (digits[digitIndex] != 0) return ParsingStatus.Overflow;
                }
                if (nonZeroTail) return ParsingStatus.Overflow;
            }

            var integerDigitCount = (int)scale;
            var value = UInt128.Zero;
            var maximum = UInt128.MaxValue;
            var maximumDiv10 = maximum / new UInt128(0, 10);
            var maximumMod10 = maximum % new UInt128(0, 10);
            for (var digitIndex = 0; digitIndex < integerDigitCount; digitIndex++)
            {
                var digit = digitIndex < storedDigitCount ? digits[digitIndex] : (byte)0;
                if (value > maximumDiv10 || (value == maximumDiv10 && digit > maximumMod10.Lower)) return ParsingStatus.Overflow;
                value = value * new UInt128(0, 10) + new UInt128(0, digit);
            }

            if (signedType)
            {
                var signedMaximum = new UInt128(0x7FFF_FFFF_FFFF_FFFF, 0xFFFF_FFFF_FFFF_FFFF);
                var minimumMagnitude = new UInt128(0x8000_0000_0000_0000, 0);
                if ((!negative && value > signedMaximum) || (negative && value > minimumMagnitude))
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

        private static ParsingStatus TryParseRadixInteger<TChar>(ReadOnlySpan<TChar> source, int start, int end, int radix, bool signedType, NumberStyles style, out Int128 signed, out UInt128 unsigned, out int consumed)
            where TChar : unmanaged
        {
            signed = default;
            unsigned = default;
            consumed = 0;
            var value = UInt128.Zero;
            var divisor = new UInt128(0, (uint)radix);
            var maximumDivisor = UInt128.MaxValue / divisor;
            var maximumRemainder = UInt128.MaxValue % divisor;
            var sawDigit = false;
            var overflowed = false;
            var index = start;
            for (; index < end; index++)
            {
                var digit = Digit(ReadSourceChar(source, index), radix);
                if (digit < 0) break;
                sawDigit = true;
                if (overflowed) continue;
                if (value > maximumDivisor || (value == maximumDivisor && (uint)digit > maximumRemainder.Lower))
                {
                    overflowed = true;
                    continue;
                }
                value = value * divisor + new UInt128(0, (uint)digit);
            }
            if (!sawDigit) return ParsingStatus.Failed;
            if ((style & NumberStyles.AllowTrailingWhite) != 0)
            {
                while (index < end && IsWhiteSpace(ReadSourceChar(source, index))) index++;
            }
            while (index < end && ReadSourceChar(source, index) == '\0') index++;
            if (index != end && (style & AllowTrailingInvalidCharacters) == 0) return ParsingStatus.Failed;
            if (overflowed) return ParsingStatus.Overflow;
            consumed = index;
            unsigned = value;
            if (signedType) signed = (Int128)value;
            return ParsingStatus.OK;
        }

        private static int Digit(char value, int radix)
        {
            var digit = value is >= '0' and <= '9' ? value - '0' :
                value is >= 'a' and <= 'f' ? value - 'a' + 10 :
                value is >= 'A' and <= 'F' ? value - 'A' + 10 : -1;
            return digit < radix ? digit : -1;
        }

        private static bool IsWhiteSpace(char value) => IsAsciiWhiteSpace(value);

        internal static void ThrowDecimalOverflowException() => throw new OverflowException(SR.Overflow_Decimal);

        // ToString() with no format: the shortest round-trip form, without the
        // format string machinery.
        internal static string FormatFloat<TNumber>(TNumber value)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
            => FormatShortestFloating<TNumber>(TNumber.FloatToBits(value));

        internal static string FormatFloat<TNumber>(TNumber value, string? format, object? provider)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
            => FormatFloating<TNumber>(TNumber.FloatToBits(value), format);

        // TryFormat with no format, as FormatFloat(value) writes it.
        internal static bool TryFormatShortestFloat<TNumber>(TNumber value, Span<char> destination, out int charsWritten)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var builder = new ValueStringBuilder(destination);
            AppendShortestFloating<TNumber>(ref builder, TNumber.FloatToBits(value));
            return builder.TryCopyTo(destination, out charsWritten);
        }

        internal static bool TryFormatFloat<TNumber>(TNumber value, ReadOnlySpan<char> format, object? provider, Span<char> destination, out int charsWritten)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var builder = new ValueStringBuilder(destination);
            AppendFloating<TNumber>(ref builder, TNumber.FloatToBits(value), format);
            return builder.TryCopyTo(destination, out charsWritten);
        }

        internal static bool TryFormatFloat<TNumber>(TNumber value, ReadOnlySpan<char> format, object? provider, Span<byte> destination, out int bytesWritten)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            Span<char> buffer = stackalloc char[64];
            var builder = new ValueStringBuilder(buffer);
            AppendFloating<TNumber>(ref builder, TNumber.FloatToBits(value), format);
            var success = TryCopyUtf8(builder.AsSpan(), destination, out bytesWritten);
            builder.Dispose();
            return success;
        }

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
            var allowTrailing = (style & AllowTrailingInvalidCharacters) != 0;
            if (TryParseSpecialFloat(text, allowTrailing, out result, out charsConsumed))
            {
                return true;
            }

            result = default!;
            charsConsumed = 0;
            var index = 0;
            if ((style & NumberStyles.AllowLeadingWhite) != 0)
            {
                while (index < text.Length && IsAsciiWhiteSpace(ReadSourceChar(text, index))) index++;
            }
            else if (index < text.Length && IsAsciiWhiteSpace(ReadSourceChar(text, index)))
            {
                return false;
            }

            var negative = false;
            var hadLeadingSign = false;
            var parenthesized = false;
            if ((style & NumberStyles.AllowParentheses) != 0 && index < text.Length && ReadSourceChar(text, index) == '(')
            {
                parenthesized = true;
                negative = true;
                index++;
            }
            if ((style & NumberStyles.AllowCurrencySymbol) != 0 && index < text.Length && ReadSourceChar(text, index) == '$') index++;
            if (index < text.Length && ReadSourceChar(text, index) is '+' or '-')
            {
                if ((style & NumberStyles.AllowLeadingSign) == 0 || parenthesized) return false;
                hadLeadingSign = true;
                negative = ReadSourceChar(text, index++) == '-';
            }
            if ((style & NumberStyles.AllowCurrencySymbol) != 0 && index < text.Length && ReadSourceChar(text, index) == '$') index++;

            char[]? rentedDigits = null;
            Span<char> digits = text.Length <= TNumber.NumberBufferLength
                ? stackalloc char[text.Length]
                : rentedDigits = ArrayPool<char>.Shared.Rent(text.Length);
            try
            {
                var digitCount = 0;
                var decimalIndex = -1;
                var sawDigit = false;
                while (index < text.Length)
                {
                    var character = ReadSourceChar(text, index);
                    if (character is >= '0' and <= '9')
                    {
                        digits[digitCount++] = character;
                        sawDigit = true;
                        index++;
                        continue;
                    }
                    if (character == '.' && decimalIndex < 0 && (style & NumberStyles.AllowDecimalPoint) != 0)
                    {
                        decimalIndex = digitCount;
                        index++;
                        continue;
                    }
                    if (character == ',' && decimalIndex < 0 && sawDigit &&
                        (style & NumberStyles.AllowThousands) != 0)
                    {
                        index++;
                        continue;
                    }
                    break;
                }
                if (!sawDigit) return false;
                if (decimalIndex < 0) decimalIndex = digitCount;

                var explicitExponent = 0L;
                if (index < text.Length && ReadSourceChar(text, index) is 'e' or 'E')
                {
                    if ((style & NumberStyles.AllowExponent) == 0) return false;
                    index++;
                    var exponentNegative = false;
                    if (index < text.Length && ReadSourceChar(text, index) is '+' or '-')
                    {
                        exponentNegative = ReadSourceChar(text, index++) == '-';
                    }
                    var exponentDigits = 0;
                    while (index < text.Length && ReadSourceChar(text, index) is >= '0' and <= '9')
                    {
                        if (explicitExponent < 1_000_000)
                        {
                            explicitExponent = explicitExponent * 10 + ReadSourceChar(text, index) - '0';
                        }
                        exponentDigits++;
                        index++;
                    }
                    if (exponentDigits == 0) return false;
                    if (exponentNegative) explicitExponent = -explicitExponent;
                }

                if ((style & NumberStyles.AllowTrailingSign) != 0 && index < text.Length && ReadSourceChar(text, index) is '+' or '-')
                {
                    if (hadLeadingSign || parenthesized) return false;
                    negative = ReadSourceChar(text, index++) == '-';
                }
                if (parenthesized)
                {
                    if (index >= text.Length || ReadSourceChar(text, index++) != ')') return false;
                }

                if ((style & NumberStyles.AllowTrailingWhite) != 0)
                {
                    while (index < text.Length && IsAsciiWhiteSpace(ReadSourceChar(text, index))) index++;
                }
                while (index < text.Length && ReadSourceChar(text, index) == '\0') index++;
                if (!allowTrailing && index != text.Length) return false;
                charsConsumed = index;

                var first = 0;
                while (first < digitCount && digits[first] == '0') first++;
                var last = digitCount;
                while (last > first && digits[last - 1] == '0') last--;
                var exponent10 = explicitExponent - (digitCount - decimalIndex) + digitCount - last;
                var bits = ConvertDecimalToFloatingBits<TNumber>(
                    digits[first..last],
                    last - first,
                    exponent10,
                    negative);
                result = TNumber.BitsToFloat(bits);
                return true;
            }
            finally
            {
                if (rentedDigits is not null)
                {
                    ArrayPool<char>.Shared.Return(rentedDigits);
                }
            }
        }

        private static bool TryParseSpecialFloat<TChar, TNumber>(
            ReadOnlySpan<TChar> source,
            bool allowTrailing,
            out TNumber result,
            out int charsConsumed)
            where TChar : unmanaged
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            result = default!;
            charsConsumed = 0;
            var index = 0;
            ConsumeSpecialWhiteSpace(source, ref index);
            var negative = false;
            if (index < source.Length && ReadSourceChar(source, index) is '+' or '-')
            {
                negative = ReadSourceChar(source, index++) == '-';
            }

            var specialIndex = index;
            var isNaN = MatchWord(source, ref index, "NaN");
            if (!isNaN)
            {
                index = specialIndex;
                if (!MatchWord(source, ref index, "Infinity")) return false;
            }

            ConsumeSpecialWhiteSpace(source, ref index);
            if (!allowTrailing && index != source.Length) return false;
            charsConsumed = index;
            result = isNaN
                ? TNumber.NaN
                : negative ? TNumber.NegativeInfinity : TNumber.PositiveInfinity;
            return true;
        }

        private static void ConsumeSpecialWhiteSpace<TChar>(
            ReadOnlySpan<TChar> source,
            ref int index)
            where TChar : unmanaged
        {
            if (typeof(TChar) == typeof(char))
            {
                var characters = MemoryMarshal.Cast<TChar, char>(source);
                while (index < characters.Length && char.IsWhiteSpace(characters[index])) index++;
                return;
            }

            var bytes = MemoryMarshal.Cast<TChar, byte>(source);
            while (index < bytes.Length &&
                   Text.Rune.DecodeFromUtf8(bytes[index..], out var rune, out var consumed) ==
                       Buffers.OperationStatus.Done &&
                   Text.Rune.IsWhiteSpace(rune))
            {
                index += consumed;
            }
        }

        private static bool MatchWord<TChar>(ReadOnlySpan<TChar> source, ref int index, string word)
            where TChar : unmanaged
        {
            if (index + word.Length > source.Length) return false;
            for (var wordIndex = 0; wordIndex < word.Length; wordIndex++)
            {
                var character = ReadSourceChar(source, index + wordIndex);
                var expected = word[wordIndex];
                if (character is >= 'a' and <= 'z') character = (char)(character - ('a' - 'A'));
                if (expected is >= 'a' and <= 'z') expected = (char)(expected - ('a' - 'A'));
                if (character != expected) return false;
            }
            index += word.Length;
            return true;
        }

        private static char ReadSourceChar<TChar>(ReadOnlySpan<TChar> source, int index)
            where TChar : unmanaged =>
            typeof(TChar) == typeof(char)
                ? MemoryMarshal.Cast<TChar, char>(source)[index]
                : (char)MemoryMarshal.Cast<TChar, byte>(source)[index];

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
            => TryCopyUtf8(value.AsSpan(), destination, out written);

        internal static bool TryCopyUtf8(ReadOnlySpan<char> value, Span<byte> destination, out int written)
        {
            var required = Encoding.UTF8.GetByteCount(value);
            if (destination.Length < required)
            {
                written = 0;
                return false;
            }
            written = Encoding.UTF8.GetBytes(value, destination);
            return true;
        }
    }
}
