// Invariant decimal IEEE-754 parsing and formatting support.
//
// The public decimal interchange types are ports of System.Private.CoreLib.
// Adapted from dotnet/runtime Number.Parsing.cs and Number.Formatting.cs;
// the upstream implementation is licensed under the MIT license.
// Copyright (c) .NET Foundation and Contributors.
// NetWasm intentionally has no ICU/culture dependency, so this file keeps the
// same conversion and rounding pipeline while supplying an invariant provider
// and the small NumberBuffer adapters that pipeline needs.

using System.Globalization;
using System.Numerics;

namespace System.Buffers.Text
{
    internal static partial class FormattingHelpers
    {
        internal static int CountDigits(uint value)
        {
            var digits = 1;
            while (value >= 10) { value /= 10; digits++; }
            return digits;
        }

        internal static int CountDigits(ulong value)
        {
            var digits = 1;
            while (value >= 10) { value /= 10; digits++; }
            return digits;
        }

        internal static int CountDigits(UInt128 value)
        {
            var digits = 1;
            while (value >= 10U) { value /= 10U; digits++; }
            return digits;
        }
    }
}

namespace System.Globalization
{
    public sealed class NumberFormatInfo : IFormatProvider
    {
        internal const string InvariantNaNSymbol = "NaN";
        internal const string InvariantPositiveInfinitySymbol = "Infinity";
        internal const string InvariantNegativeInfinitySymbol = "-Infinity";

        private static readonly NumberFormatInfo s_invariant = new();

        public static NumberFormatInfo CurrentInfo => s_invariant;

        public static NumberFormatInfo InvariantInfo => s_invariant;

        public NumberFormatInfo()
        {
        }

        // NetWasm is intentionally invariant-only, but these standard properties are
        // still exposed so the provider remains useful to formatting clients.
        public string NaNSymbol => InvariantNaNSymbol;
        public string PositiveInfinitySymbol => InvariantPositiveInfinitySymbol;
        public string NegativeInfinitySymbol => InvariantNegativeInfinitySymbol;
        public string PositiveSign => "+";
        public string NegativeSign => "-";
        public string NumberDecimalSeparator => ".";
        public string NumberGroupSeparator => ",";
        public int NumberDecimalDigits => 2;
        public string CurrencySymbol => "$";
        public int CurrencyDecimalDigits => 2;
        public string PercentSymbol => "%";
        public int PercentDecimalDigits => 2;

        internal static NumberFormatInfo GetInstance(IFormatProvider? provider) =>
            provider as NumberFormatInfo ?? s_invariant;

        public object? GetFormat(Type? formatType) => formatType == typeof(NumberFormatInfo) ? this : null;

        internal static void ValidateParseStyleDecimal(NumberStyles style) => Validate(style, allowExponent: true, allowDecimalPoint: true);

        internal static void ValidateParseStyleFloatingPoint(NumberStyles style) => Validate(style, allowExponent: true, allowDecimalPoint: true);

        internal static void ValidateParseStyleInteger(NumberStyles style) => Validate(style, allowExponent: false, allowDecimalPoint: false);

        private static void Validate(NumberStyles style, bool allowExponent, bool allowDecimalPoint)
        {
            var allowed = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
                NumberStyles.AllowLeadingSign | NumberStyles.AllowTrailingSign |
                NumberStyles.AllowParentheses | NumberStyles.AllowThousands |
                NumberStyles.AllowCurrencySymbol | NumberStyles.AllowHexSpecifier |
                NumberStyles.AllowBinarySpecifier;

            if (allowExponent) allowed |= NumberStyles.AllowExponent;
            if (allowDecimalPoint) allowed |= NumberStyles.AllowDecimalPoint;
            if ((style & ~allowed) != 0 || (style & (NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier)) != 0 &&
                (allowExponent || allowDecimalPoint))
            {
                throw new ArgumentException("The number style is not supported.", nameof(style));
            }

            if ((style & (NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier)) != 0 &&
                (style & (NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier)) ==
                (NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier))
            {
                throw new ArgumentException("The number style is not supported.", nameof(style));
            }
        }
    }
}

namespace System
{
    internal static class SR
    {
        internal const string Arithmetic_NaN = "NaN is not a number.";
        internal const string Overflow_Decimal = "Value was either too large or too small for a Decimal.";
        internal const string Arg_MustBeDecimal32 = "Object must be of type Decimal32.";
        internal const string Arg_MustBeDecimal64 = "Object must be of type Decimal64.";
        internal const string Arg_MustBeDecimal128 = "Object must be of type Decimal128.";
        internal const string Arg_MustBeBFloat16 = "Object must be of type BFloat16.";
        internal const string Arg_MustBeHalf = "Object must be of type Half.";
        internal const string Arg_MustBeInt128 = "Object must be of type Int128.";
        internal const string Arg_MustBeUInt128 = "Object must be of type UInt128.";
        internal const string Overflow_Int128 = "Value was either too large or too small for an Int128.";
        internal const string Overflow_UInt128 = "Value was either too large or too small for a UInt128.";
    }

    public static partial class Math
    {
        internal static void ThrowMinMaxException<T>(T min, T max) => throw new ArgumentException();
        internal static void ThrowNegateTwosCompOverflow() => throw new OverflowException();
    }

    internal static partial class Number
    {
        // P significant digits, one digit retained for rounding, and the NUL.
        internal const int Decimal32NumberBufferLength = 9;
        internal const int Decimal64NumberBufferLength = 18;
        internal const int Decimal128NumberBufferLength = 36;
        internal const int UInt128NumberBufferLength = 40;

        internal static string UInt32ToDecStr(uint value) => FormatUnsigned(value, 32, null);

        internal static string UInt64ToDecStr(ulong value) => FormatUnsigned(value, 64, null);

        internal static uint DigitsToUInt32(ReadOnlySpan<byte> digits, int count)
        {
            var value = 0U;
            for (var i = 0; i < count; i++) value = value * 10U + (uint)(digits[i] - (byte)'0');
            return value;
        }

        internal static ulong DigitsToUInt64(ReadOnlySpan<byte> digits, int count)
        {
            var value = 0UL;
            for (var i = 0; i < count; i++) value = value * 10UL + (uint)(digits[i] - (byte)'0');
            return value;
        }

        internal static UInt128 DigitsToUInt128(ReadOnlySpan<byte> digits, int count)
        {
            var value = UInt128.Zero;
            for (var i = 0; i < count; i++) value = value * 10U + (uint)(digits[i] - (byte)'0');
            return value;
        }

        internal static void DecimalToNumber(ref decimal value, ref NumberBuffer number)
        {
            var bits = decimal.GetBits(value);
            var magnitude = new UInt128(
                (ulong)(uint)bits[2],
                ((ulong)(uint)bits[1] << 32) | (uint)bits[0]);
            var digits = UInt128ToDecStr(magnitude);
            var count = digits.Length;
            for (var i = 0; i < count; i++) number.Digits[i] = (byte)digits[i];
            number.Digits[count] = 0;
            number.DigitsCount = count;
            number.Scale = count - ((int)(bits[3] >> 16) & 0xff);
            number.IsNegative = ((uint)bits[3] & 0x8000_0000U) != 0;
        }

        private static TValue NumberToDecimalIeee754<TDecimal, TValue>(ref NumberBuffer number)
            where TDecimal : unmanaged, IDecimalIeee754ParseAndFormatInfo<TDecimal, TValue>
            where TValue : unmanaged, IBinaryInteger<TValue> => NumberToDecimalIeee754Bits<TDecimal, TValue>(ref number);

        internal static bool TryFloatingPointBitsFromMantissa<TFloat>(ulong mantissa, int exponent, out ulong bits)
            where TFloat : unmanaged, IBinaryFloatParseAndFormatInfo<TFloat>
        {
            var digits = UInt64ToDecStr(mantissa).ToCharArray();
            bits = ConvertDecimalToFloatingBits(
                digits,
                digits.Length,
                exponent,
                negative: false,
                single: typeof(TFloat) == typeof(float));
            return true;
        }

        // A compact Dragon4-compatible front end. The exact rational is produced by
        // the existing binary decomposition, then rounded directly to the requested
        // decimal precision by the same integer long-division routine used by the
        // invariant floating formatter.
        internal static void Dragon4<TFloat>(
            TFloat value,
            int cutoffNumber,
            bool isSignificantDigits,
            ref NumberBuffer number,
            out bool isExact)
            where TFloat : unmanaged, IBinaryFloatParseAndFormatInfo<TFloat>
        {
            var bits = TFloat.FloatToBits(value);
            var single = typeof(TFloat) == typeof(float);
            GetFloatingRational(bits, single, out var numerator, out var denominator);
            var binaryExponent = FloorBinaryExponent(numerator, denominator);
            var decimalExponent = (int)(binaryExponent * 30_103L / 100_000L);
            while (CompareToPower10(numerator, denominator, decimalExponent) < 0) decimalExponent--;
            while (CompareToPower10(numerator, denominator, decimalExponent + 1) >= 0) decimalExponent++;

            var requested = cutoffNumber == int.MaxValue
                ? Math.Min(number.Digits.Length - 1, 80)
                : Math.Min(cutoffNumber, number.Digits.Length - 1);
            requested = Math.Max(requested, 1);
            var digits = GenerateRoundedDigitsExact(
                numerator,
                denominator,
                decimalExponent,
                requested,
                out var resultExponent,
                out var resultLength,
                out isExact);
            for (var i = 0; i < resultLength; i++) number.Digits[i] = (byte)digits[i];
            number.Digits[resultLength] = 0;
            number.DigitsCount = resultLength;
            // Dragon4's exponent is the adjusted exponent of the first digit (10^e), whereas
            // NumberBuffer.Scale is the decimal point position after the coefficient digits.
            // The latter is therefore e + 1, independent of how many trailing zeros we retained.
            number.Scale = resultExponent + 1;
            number.HasNonZeroTail = cutoffNumber == int.MaxValue && !isExact;
        }

        internal static void Dragon4<TFloat>(
            TFloat value,
            int cutoffNumber,
            bool isSignificantDigits,
            ref NumberBuffer number)
            where TFloat : unmanaged, IBinaryFloatParseAndFormatInfo<TFloat>
        {
            Dragon4(value, cutoffNumber, isSignificantDigits, ref number, out _);
        }

        private static char[] GenerateRoundedDigitsExact(
            FloatingBigInteger numerator,
            FloatingBigInteger denominator,
            int decimalExponent,
            int precision,
            out int resultExponent,
            out int resultLength,
            out bool isExact)
        {
            var scaledNumerator = numerator.Clone();
            var scaledDenominator = denominator.Clone();
            if (decimalExponent >= 0) scaledDenominator.MultiplyPower10(decimalExponent);
            else scaledNumerator.MultiplyPower10(-decimalExponent);

            var digits = new char[precision];
            var current = scaledNumerator;
            for (var index = 0; index < precision; index++)
            {
                var digit = FloatingBigInteger.DivideToUInt64(current, scaledDenominator, out var remainder);
                digits[index] = (char)('0' + digit);
                current = remainder;
                if (index + 1 < precision) current.Multiply(10);
            }

            isExact = current.IsZero;
            var twice = current.Clone();
            twice.Multiply(2);
            var comparison = FloatingBigInteger.Compare(twice, scaledDenominator);
            if (comparison > 0 || comparison == 0 && ((digits[precision - 1] - '0') & 1) != 0)
            {
                var index = precision - 1;
                while (index >= 0 && digits[index] == '9') digits[index--] = '0';
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

        internal static string FormatDecimalIeee754<TDecimal, TValue>(TValue value, string? format, NumberFormatInfo info)
            where TDecimal : unmanaged, IDecimalIeee754ParseAndFormatInfo<TDecimal, TValue>
            where TValue : unmanaged, IBinaryInteger<TValue>
        {
            if (TDecimal.IsNaN(value)) return NumberFormatInfo.InvariantNaNSymbol;
            if (TDecimal.IsInfinity(value)) return TDecimal.IsNegative(value)
                ? NumberFormatInfo.InvariantNegativeInfinitySymbol
                : NumberFormatInfo.InvariantPositiveInfinitySymbol;

            var decoded = UnpackDecimalIeee754<TDecimal, TValue>(value);
            if (TValue.IsZero(decoded.Significand))
            {
                // Zero has a sign but no meaningful cohort exponent for ordinary numeric formatting.
                decoded = new DecodedDecimalIeee754<TValue>(decoded.Signed, 0, TValue.Zero);
            }
            var digits = TDecimal.ToDecStr(decoded.Significand);
            var negative = decoded.Signed;
            var specifier = 'G';
            var precision = -1;

            if (!string.IsNullOrEmpty(format))
            {
                specifier = format![0];
                if (format.Length > 1)
                {
                    precision = ParseDecimalFormatPrecision(format);
                }

                if (specifier is 'R' or 'r')
                {
                    specifier = specifier is 'r' ? 'g' : 'G';
                    precision = -1;
                }
                else if (specifier is not ('G' or 'g' or 'R' or 'r' or 'E' or 'e' or 'F' or 'f' or 'N' or 'n' or 'C' or 'c' or 'P' or 'p'))
                {
                    throw new FormatException();
                }
            }

            if (specifier is 'G' or 'g' && precision == 0)
            {
                // As in the runtime formatter, G0 means the default shortest/general form.
                precision = -1;
            }

            if (specifier is 'E' or 'e')
            {
                var places = precision < 0 ? TDecimal.Precision - 1 : precision;
                RoundCoefficient(ref digits, ref decoded, places + 1);
                return FormatScientific(digits, decoded.UnbiasedExponent, negative, places, specifier);
            }

            if (specifier is 'C' or 'c')
            {
                var places = precision < 0 ? info.CurrencyDecimalDigits : precision;
                RoundToFixed(ref digits, ref decoded, places);
                var currencyText = AddGrouping(FormatFixed(digits, decoded.UnbiasedExponent, negative, places));
                return negative
                    ? "(" + info.CurrencySymbol + currencyText.Substring(1) + ")"
                    : info.CurrencySymbol + currencyText;
            }

            if (specifier is 'P' or 'p')
            {
                var places = precision < 0 ? info.PercentDecimalDigits : precision;
                decoded = new DecodedDecimalIeee754<TValue>(decoded.Signed, decoded.UnbiasedExponent + 2, decoded.Significand);
                RoundToFixed(ref digits, ref decoded, places);
                return AddGrouping(FormatFixed(digits, decoded.UnbiasedExponent, negative, places)) + " %";
            }

            if (specifier is 'F' or 'f' or 'N' or 'n')
            {
                var places = precision < 0 ? 2 : precision;
                RoundToFixed(ref digits, ref decoded, places);
                var fixedText = FormatFixed(digits, decoded.UnbiasedExponent, negative, places);
                return specifier is 'N' or 'n' ? AddGrouping(fixedText) : fixedText;
            }

            if (precision >= 0) RoundCoefficient(ref digits, ref decoded, precision);
            return FormatGeneral(digits, decoded.UnbiasedExponent, negative, TDecimal.Precision, specifier);
        }

        internal static bool TryFormatDecimalIeee754<TDecimal, TValue, TChar>(
            TValue value,
            ReadOnlySpan<char> format,
            NumberFormatInfo info,
            Span<TChar> destination,
            out int charsWritten)
            where TDecimal : unmanaged, IDecimalIeee754ParseAndFormatInfo<TDecimal, TValue>
            where TValue : unmanaged, IBinaryInteger<TValue>
            where TChar : unmanaged
        {
            var text = FormatDecimalIeee754<TDecimal, TValue>(value, SpanToString(format), info);
            if (typeof(TChar) == typeof(char)) return TryCopy(text, System.Runtime.InteropServices.MemoryMarshal.Cast<TChar, char>(destination), out charsWritten);
            return TryCopyUtf8(text, System.Runtime.InteropServices.MemoryMarshal.Cast<TChar, byte>(destination), out charsWritten);
        }

        internal static TDecimal ParseDecimalIeee754<TChar, TDecimal, TValue>(
            ReadOnlySpan<TChar> value,
            NumberStyles styles,
            NumberFormatInfo info)
            where TChar : unmanaged
            where TDecimal : unmanaged, IDecimalIeee754ParseAndFormatInfo<TDecimal, TValue>
            where TValue : unmanaged, IBinaryInteger<TValue>
        {
            var status = TryParseDecimalIeee754<TChar, TDecimal, TValue>(value, styles, info, out TDecimal result, out _);
            if (status == ParsingStatus.Failed) throw new FormatException();
            return result;
        }

        internal static ParsingStatus TryParseDecimalIeee754<TChar, TDecimal, TValue>(
            ReadOnlySpan<TChar> value,
            NumberStyles styles,
            NumberFormatInfo info,
            out TDecimal result,
            out int elementsConsumed)
            where TChar : unmanaged
            where TDecimal : unmanaged, IDecimalIeee754ParseAndFormatInfo<TDecimal, TValue>
            where TValue : unmanaged, IBinaryInteger<TValue>
        {
            result = default;
            elementsConsumed = 0;
            var source = SpanToString(value);
            var index = 0;
            var allowTrailing = (styles & AllowTrailingInvalidCharacters) != 0;
            var allowLeadingWhite = (styles & NumberStyles.AllowLeadingWhite) != 0;
            var allowTrailingWhite = (styles & NumberStyles.AllowTrailingWhite) != 0;

            if (allowLeadingWhite) while (index < source.Length && IsAsciiWhiteSpace(source[index])) index++;
            else if (index < source.Length && IsAsciiWhiteSpace(source[index])) return ParsingStatus.Failed;

            var start = index;
            var negative = false;
            var hadLeadingSign = false;
            var parenthesized = false;
            if ((styles & NumberStyles.AllowParentheses) != 0 && index < source.Length && source[index] == '(')
            {
                parenthesized = true;
                index++;
            }
            if ((styles & NumberStyles.AllowCurrencySymbol) != 0 && index < source.Length && source[index] == '$') index++;
            if (index < source.Length && (source[index] == '+' || source[index] == '-'))
            {
                if ((styles & NumberStyles.AllowLeadingSign) == 0) return ParsingStatus.Failed;
                hadLeadingSign = true;
                negative = source[index++] == '-';
            }

            var specialStart = index;
            if (MatchWord(source, ref index, "Infinity") || MatchWord(source, ref index, "NaN"))
            {
                var trailing = ConsumeTrailing(source, index, allowTrailingWhite);
                if (trailing < 0 || (!allowTrailing && trailing != source.Length)) return ParsingStatus.Failed;
                elementsConsumed = allowTrailing ? trailing : source.Length;
                var wasNaN = source[specialStart] is 'N' or 'n';
                var bits = wasNaN ? TDecimal.NaN : negative ? TDecimal.NegativeInfinity : TDecimal.PositiveInfinity;
                if (wasNaN && negative) bits |= TDecimal.SignMask;
                result = TDecimal.Construct(bits);
                return ParsingStatus.OK;
            }

            index = start;
            parenthesized = false;
            if ((styles & NumberStyles.AllowParentheses) != 0 && index < source.Length && source[index] == '(')
            {
                parenthesized = true;
                index++;
            }
            if ((styles & NumberStyles.AllowCurrencySymbol) != 0 && index < source.Length && source[index] == '$') index++;
            if (index < source.Length && (source[index] == '+' || source[index] == '-'))
            {
                if ((styles & NumberStyles.AllowLeadingSign) == 0) return ParsingStatus.Failed;
                hadLeadingSign = true;
                negative = source[index++] == '-';
            }
            if ((styles & NumberStyles.AllowCurrencySymbol) != 0 && index < source.Length && source[index] == '$') index++;

            var raw = new char[source.Length];
            var rawCount = 0;
            var decimalIndex = -1;
            var sawDigit = false;
            var exponent = 0;
            var exponentNegative = false;
            while (index < source.Length)
            {
                var c = source[index];
                if (c is >= '0' and <= '9')
                {
                    raw[rawCount++] = c;
                    sawDigit = true;
                    index++;
                    continue;
                }
                if (c == '.' && decimalIndex < 0)
                {
                    if ((styles & NumberStyles.AllowDecimalPoint) == 0) return ParsingStatus.Failed;
                    decimalIndex = rawCount;
                    index++;
                    continue;
                }
                if (c == ',' && (styles & NumberStyles.AllowThousands) != 0)
                {
                    index++;
                    continue;
                }
                break;
            }

            if (!sawDigit) return ParsingStatus.Failed;
            if (decimalIndex < 0) decimalIndex = rawCount;

            if (index < source.Length && (source[index] is 'e' or 'E'))
            {
                if ((styles & NumberStyles.AllowExponent) == 0) return ParsingStatus.Failed;
                index++;
                if (index < source.Length && (source[index] is '+' or '-'))
                {
                    exponentNegative = source[index++] == '-';
                }
                var exponentDigits = 0;
                while (index < source.Length && source[index] is >= '0' and <= '9')
                {
                    if (exponentDigits < 6) exponent = exponent * 10 + source[index] - '0';
                    exponentDigits++;
                    index++;
                }
                if (exponentDigits == 0) return ParsingStatus.Failed;
                if (exponentNegative) exponent = -exponent;
            }

            if ((styles & NumberStyles.AllowTrailingSign) != 0 && index < source.Length && (source[index] == '+' || source[index] == '-'))
            {
                if (hadLeadingSign) return ParsingStatus.Failed;
                negative = source[index++] == '-';
            }
            if (parenthesized)
            {
                if (index >= source.Length || source[index++] != ')') return ParsingStatus.Failed;
                negative = true;
            }

            var trailingIndex = ConsumeTrailing(source, index, allowTrailingWhite);
            if (trailingIndex < 0 || (!allowTrailing && trailingIndex != source.Length)) return ParsingStatus.Failed;
            elementsConsumed = allowTrailing ? trailingIndex : source.Length;

            var first = 0;
            while (first < rawCount && raw[first] == '0') first++;
            Span<byte> digits = stackalloc byte[TDecimal.BufferLength];
            var number = new NumberBuffer(NumberBufferKind.Decimal, digits);
            number.IsNegative = negative;
            if (first == rawCount)
            {
                number.Scale = 0;
                result = TDecimal.Construct(NumberToDecimalIeee754<TDecimal, TValue>(ref number));
                return ParsingStatus.OK;
            }

            var significantCount = rawCount - first;
            var capacity = digits.Length - 1;
            var stored = Math.Min(significantCount, capacity);
            for (var i = 0; i < stored; i++) number.Digits[i] = (byte)raw[first + i];
            number.Digits[stored] = 0;
            number.DigitsCount = stored;
            number.HasNonZeroTail = significantCount > stored;
            if (number.HasNonZeroTail)
            {
                for (var i = first + stored; i < rawCount; i++)
                {
                    if (raw[i] != '0') { number.HasNonZeroTail = true; break; }
                    number.HasNonZeroTail = false;
                }
            }

            number.Scale = decimalIndex + exponent - first;
            result = TDecimal.Construct(NumberToDecimalIeee754<TDecimal, TValue>(ref number));
            return ParsingStatus.OK;
        }

        private static int ConsumeTrailing(string source, int index, bool allowTrailingWhite)
        {
            var end = index;
            if (allowTrailingWhite) while (end < source.Length && IsAsciiWhiteSpace(source[end])) end++;
            return end;
        }

        private static bool MatchWord(string source, ref int index, string word)
        {
            if (index + word.Length > source.Length) return false;
            for (var i = 0; i < word.Length; i++)
            {
                var c = source[index + i];
                var expected = word[i];
                if (c >= 'a' && c <= 'z') c = (char)(c - ('a' - 'A'));
                if (expected >= 'a' && expected <= 'z') expected = (char)(expected - ('a' - 'A'));
                if (c != expected) return false;
            }
            index += word.Length;
            return true;
        }

        private static int ParseDecimalFormatPrecision(string format)
        {
            var result = 0;
            for (var i = 1; i < format.Length; i++)
            {
                if (format[i] is < '0' or > '9' || result > 99) throw new FormatException();
                result = result * 10 + format[i] - '0';
            }
            return result;
        }

        private static void RoundCoefficient<TValue>(ref string digits, ref DecodedDecimalIeee754<TValue> decoded, int keep)
            where TValue : unmanaged, IBinaryInteger<TValue>
        {
            if (keep < 1 || keep >= digits.Length) return;
            var discarded = digits.Length - keep;
            var round = digits[keep] - '0';
            var nonZero = false;
            for (var i = keep + 1; i < digits.Length; i++) nonZero |= digits[i] != '0';
            var up = round > 5 || round == 5 && (nonZero || ((digits[keep - 1] - '0') & 1) != 0);
            var prefix = digits.Substring(0, keep).ToCharArray();
            if (up)
            {
                var i = prefix.Length - 1;
                while (i >= 0 && prefix[i] == '9') prefix[i--] = '0';
                if (i < 0)
                {
                    digits = string.Concat("1", new string('0', Math.Max(0, keep - 1)));
                    decoded = new DecodedDecimalIeee754<TValue>(decoded.Signed, decoded.UnbiasedExponent + discarded + 1, TValue.One);
                    return;
                }
                prefix[i]++;
            }
            digits = string.Create(prefix);
            decoded = new DecodedDecimalIeee754<TValue>(decoded.Signed, decoded.UnbiasedExponent + discarded, decoded.Significand);
        }

        private static void RoundToFixed<TValue>(ref string digits, ref DecodedDecimalIeee754<TValue> decoded, int places)
            where TValue : unmanaged, IBinaryInteger<TValue>
        {
            var drop = -places - decoded.UnbiasedExponent;
            if (drop <= 0) return;
            if (drop >= digits.Length)
            {
                digits = "0";
                decoded = new DecodedDecimalIeee754<TValue>(decoded.Signed, -places, TValue.Zero);
                return;
            }
            var keep = digits.Length - drop;
            RoundCoefficient(ref digits, ref decoded, keep);
        }

        private static string FormatGeneral(string digits, int exponent, bool negative, int precision, char specifier)
        {
            if (digits == "0") return negative ? "-0" : "0";

            // General formatting suppresses insignificant trailing zeros after rounding. Moving a
            // zero from the coefficient into the exponent preserves the represented value.
            while (digits.Length > 1 && digits[digits.Length - 1] == '0')
            {
                digits = digits.Substring(0, digits.Length - 1);
                exponent++;
            }

            var decimalExponent = digits.Length + exponent - 1;
            return FormatGeneralDigits(
                digits.ToCharArray(),
                digits.Length,
                decimalExponent,
                negative,
                precision < 0 ? int.MaxValue : precision,
                specifier is 'g');
        }

        private static string FormatFixed(string digits, int exponent, bool negative, int places)
        {
            var position = digits.Length + exponent;
            var leadingZeros = position <= 0 ? -position : 0;
            var fractionLength = position < digits.Length ? digits.Length - Math.Max(position, 0) : 0;
            fractionLength = Math.Max(fractionLength, places);
            if (position <= 0)
            {
                fractionLength = Math.Max(fractionLength, leadingZeros + digits.Length);
            }

            var length = (negative ? 1 : 0) + Math.Max(position, 1) + (fractionLength == 0 ? 0 : 1 + fractionLength);
            var chars = new char[length];
            var at = 0;
            if (negative) chars[at++] = '-';
            if (position <= 0)
            {
                chars[at++] = '0';
                chars[at++] = '.';
                for (var i = 0; i < leadingZeros; i++) chars[at++] = '0';
                for (var i = 0; i < digits.Length; i++) chars[at++] = digits[i];
                while (at < chars.Length) chars[at++] = '0';
            }
            else
            {
                for (var i = 0; i < position; i++) chars[at++] = i < digits.Length ? digits[i] : '0';
                if (fractionLength != 0)
                {
                    chars[at++] = '.';
                    for (var i = 0; i < fractionLength; i++) chars[at++] = position + i < digits.Length ? digits[position + i] : '0';
                }
            }
            return string.Create(chars);
        }

        private static string FormatScientific(string digits, int exponent, bool negative, int places, char specifier)
        {
            var scientificExponent = exponent + digits.Length - 1;
            var exponentMagnitude = scientificExponent < 0 ? -scientificExponent : scientificExponent;
            const int exponentDigits = 3;
            var length = (negative ? 1 : 0) + 1 + (places == 0 ? 0 : 1 + places) + 2 + exponentDigits;
            var chars = new char[length];
            var at = 0;
            if (negative) chars[at++] = '-';
            chars[at++] = digits[0];
            if (places != 0)
            {
                chars[at++] = '.';
                for (var i = 0; i < places; i++) chars[at++] = i + 1 < digits.Length ? digits[i + 1] : '0';
            }
            chars[at++] = specifier;
            chars[at++] = scientificExponent < 0 ? '-' : '+';
            for (var i = exponentDigits - 1; i >= 0; i--)
            {
                chars[at + i] = (char)('0' + exponentMagnitude % 10);
                exponentMagnitude /= 10;
            }
            return string.Create(chars);
        }

        private static string AddGrouping(string value)
        {
            var sign = value.Length != 0 && value[0] == '-' ? 1 : 0;
            var point = value.IndexOf('.');
            if (point < 0) point = value.Length;
            var integerLength = point - sign;
            if (integerLength <= 3) return value;

            var groups = (integerLength - 1) / 3;
            var chars = new char[value.Length + groups];
            var source = 0;
            var target = 0;
            if (sign != 0) chars[target++] = value[source++];
            var firstGroup = integerLength % 3;
            if (firstGroup == 0) firstGroup = 3;
            for (var i = 0; i < integerLength; i++)
            {
                if (i != 0 && i % firstGroup == 0 && (i - firstGroup) % 3 == 0) chars[target++] = ',';
                chars[target++] = value[source++];
            }
            while (source < value.Length) chars[target++] = value[source++];
            return string.Create(chars);
        }
    }
}
