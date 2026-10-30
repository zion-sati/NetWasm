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
using System.Text;

namespace System.Globalization
{
    public sealed partial class NumberFormatInfo : IFormatProvider
    {
        internal const string InvariantNaNSymbol = "NaN";
        internal const string InvariantPositiveInfinitySymbol = "Infinity";
        internal const string InvariantNegativeInfinitySymbol = "-Infinity";
        internal const string InvariantNegativeSign = "-";

        private static readonly NumberFormatInfo s_invariant = new();

        public static NumberFormatInfo CurrentInfo => s_invariant;

        public static NumberFormatInfo InvariantInfo => s_invariant;

        // The current culture is invariant, so its negative sign needs no
        // instance: integer formatting reads it without initializing this type.
        internal static string CurrentNegativeSign => InvariantNegativeSign;

        public NumberFormatInfo()
        {
        }

        // NetWasm is intentionally invariant-only, but these standard properties are
        // still exposed so the provider remains useful to formatting clients.
        public string NaNSymbol => InvariantNaNSymbol;
        public string PositiveInfinitySymbol => InvariantPositiveInfinitySymbol;
        public string NegativeInfinitySymbol => InvariantNegativeInfinitySymbol;
        public string PositiveSign => "+";
        public string NegativeSign => InvariantNegativeSign;
        public string NumberDecimalSeparator => ".";
        public string NumberGroupSeparator => ",";
        public int NumberDecimalDigits => 2;
        public string CurrencySymbol => _currencySymbol;
        public int CurrencyDecimalDigits => 2;
        public string PercentSymbol => "%";
        public int PercentDecimalDigits => 2;

        internal static NumberFormatInfo GetInstance(IFormatProvider? provider)
        {
            if (provider is NumberFormatInfo numberFormat) return numberFormat;
            if (provider is CultureInfo culture) return culture.NumberFormat;
            return provider?.GetFormat(typeof(NumberFormatInfo)) as NumberFormatInfo ?? s_invariant;
        }

        public object? GetFormat(Type? formatType) => formatType == typeof(NumberFormatInfo) ? this : null;

        internal static void ValidateParseStyleDecimal(NumberStyles style) => Validate(style, allowExponent: true, allowDecimalPoint: true);

        internal static void ValidateParseStyleFloatingPoint(NumberStyles style) => Validate(style, allowExponent: true, allowDecimalPoint: true);

        internal static void ValidateParseStyleInteger(NumberStyles style)
        {
            const NumberStyles allowed = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
                NumberStyles.AllowLeadingSign | NumberStyles.AllowTrailingSign | NumberStyles.AllowParentheses |
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowExponent |
                NumberStyles.AllowCurrencySymbol | NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier;

            if ((style & ~allowed) != 0 ||
                ((style & (NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier)) != 0 &&
                 (style & ~NumberStyles.HexNumber) != 0 &&
                 (style & ~NumberStyles.BinaryNumber) != 0))
            {
                throw new ArgumentException("The number style is not supported.", nameof(style));
            }
        }

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
    internal static partial class SR
    {
        internal const string Arg_InvalidBase = "Invalid Base.";
        internal const string Arg_MustBeNullTerminatedString = "The string must be null-terminated.";
        internal const string Arg_TypeNotSupported = "Specified type is not supported";
        internal const string Argument_DestinationTooShort = "Destination is too short.";
        internal const string ArgumentOutOfRange_IndexMustBeLess = "Index was out of range. Must be non-negative and less than the size of the collection.";
        internal const string NotSupported_Type = "Type is not supported.";
        internal const string Argument_BadFormatSpecifier = "Format specifier was invalid.";
        internal const string Argument_CannotParsePrecision = "Characters following the format symbol must be a number of {0} or less.";
        internal const string Argument_GWithPrecisionNotSupported = "The 'G' format combined with a precision is not supported.";
        internal const string Argument_PrecisionTooLarge = "Precision cannot be larger than {0}.";
        internal const string Arithmetic_NaN = "Function does not accept floating point Not-a-Number values.";
        internal const string Overflow_Decimal = "Value was either too large or too small for a Decimal.";
        internal const string Arg_DecBitCtor = "Decimal constructor requires an array or span of four valid decimal bytes.";
        internal const string Arg_MustBeDecimal = "Object must be of type Decimal.";
        internal const string ArgumentOutOfRange_DecimalRound = "Decimal can only round to between 0 and 28 digits of precision.";
        internal const string Overflow_Char = "Value was either too large or too small for a character.";
        internal const string Overflow_Currency = "Value was either too large or too small for a Currency.";
        internal const string Overflow_Int32 = "Value was either too large or too small for an Int32.";
        internal const string Overflow_Int64 = "Value was either too large or too small for an Int64.";
        internal const string Overflow_UInt32 = "Value was either too large or too small for a UInt32.";
        internal const string Overflow_UInt64 = "Value was either too large or too small for a UInt64.";
        internal const string Overflow_Byte = "Value was either too large or too small for an unsigned byte.";
        internal const string Overflow_SByte = "Value was either too large or too small for a signed byte.";
        internal const string Overflow_Int16 = "Value was either too large or too small for an Int16.";
        internal const string Overflow_UInt16 = "Value was either too large or too small for a UInt16.";
        internal const string Arg_MustBeDecimal32 = "Object must be of type Decimal32.";
        internal const string Arg_MustBeDecimal64 = "Object must be of type Decimal64.";
        internal const string Arg_MustBeDecimal128 = "Object must be of type Decimal128.";
        internal const string Arg_MustBeBFloat16 = "Object must be of type BFloat16.";
        internal const string Arg_MustBeHalf = "Object must be of type Half.";
        internal const string Arg_MustBeInt128 = "Object must be of type Int128.";
        internal const string Arg_MustBeUInt128 = "Object must be of type UInt128.";
        internal const string Overflow_Int128 = "Value was either too large or too small for an Int128.";
        internal const string Overflow_UInt128 = "Value was either too large or too small for a UInt128.";
        internal const string ArgumentException_TupleIncorrectType = "Argument must be of type {0}.";
        internal const string ArgumentException_TupleLastArgumentNotATuple =
            "The last element of an eight element tuple must be a Tuple.";
        internal const string ArgumentException_ValueTupleIncorrectType =
            "Argument must be of type {0}.";
        internal const string ArgumentException_ValueTupleLastArgumentNotAValueTuple =
            "The last element of an eight element ValueTuple must be a ValueTuple.";
        internal const string ArgumentOutOfRange_IndexCountBuffer = "Index and count must refer to a location within the buffer.";
        internal const string ArgumentOutOfRange_IndexMustBeLessOrEqual = "Index was out of range. Must be non-negative and less than or equal to the size of the collection.";
        internal const string Argument_ExtraNotValid = "Extra portion of URI not valid.";
        internal const string Argument_IdnBadBidi = "Left to right characters may not be mixed with right to left characters in IDN labels.";
        internal const string Argument_IdnBadLabelSize = "IDN labels must be between 1 and 63 characters long.";
        internal const string Argument_IdnBadNameSize = "IDN names must be between 1 and {0} characters long.";
        internal const string Argument_IdnBadPunycode = "Invalid IDN encoded string.";
        internal const string Argument_IdnBadStd3 = "Label contains character '{0}' not allowed with UseStd3AsciiRules";
        internal const string Argument_IdnIllegalName = "Decoded string is not a valid IDN name.";
        internal const string Argument_InvalidCharSequence = "Invalid Unicode code point found at index {0}.";
        internal const string Argument_InvalidUriSubcomponent = "The subcomponent, {0}, of this uri is not valid.";
        internal const string net_uri_AlreadyRegistered = "A URI scheme name '{0}' already has a registered custom parser.";
        internal const string net_uri_BadAuthority = "Invalid URI: The Authority/Host could not be parsed.";
        internal const string net_uri_BadAuthorityTerminator = "Invalid URI: The Authority/Host cannot end with a backslash character ('\\').";
        internal const string net_uri_BadFormat = "Invalid URI: The format of the URI could not be determined.";
        internal const string net_uri_BadHostName = "Invalid URI: The hostname could not be parsed.";
        internal const string net_uri_BadPort = "Invalid URI: Invalid port specified.";
        internal const string net_uri_BadScheme = "Invalid URI: The URI scheme is not valid.";
        internal const string net_uri_BadUnicodeHostForIdn = "An invalid Unicode character by IDN standards was specified in the host.";
        internal const string net_uri_BadUserPassword = "Invalid URI: The username:password construct is badly formed.";
        internal const string net_uri_CannotCreateRelative = "A relative URI cannot be created because the 'uriString' parameter represents an absolute URI.";
        internal const string net_uri_EmptyUri = "Invalid URI: The URI is empty.";
        internal const string net_uri_GetComponentsCalledWhenCanonicalizationDisabled = "GetComponents() may not be used for Path/Query on a Uri instance created with UriCreationOptions.DangerousDisablePathAndQueryCanonicalization.";
        internal const string net_uri_InitializeCalledAlreadyOrTooLate = "UriParser's base InitializeAndValidate may only be called once on a single Uri instance and only from an override of InitializeAndValidate.";
        internal const string net_uri_InvalidUriKind = "The value '{0}' passed for the UriKind parameter is invalid.";
        internal const string net_uri_MustRootedPath = "Invalid URI: A Dos path must be rooted, for example, 'c:\\'.";
        internal const string net_uri_NeedFreshParser = "The URI parser instance passed into 'uriParser' parameter is already registered with the scheme name '{0}'.";
        internal const string net_uri_NotAbsolute = "This operation is not supported for a relative URI.";
        internal const string net_uri_NotJustSerialization = "UriComponents.SerializationInfoString must not be combined with other UriComponents.";
        internal const string net_uri_PortOutOfRange = "A derived type '{0}' has reported an invalid value for the Uri port '{1}'.";
        internal const string net_uri_SchemeLimit = "Invalid URI: The Uri scheme is too long.";
        internal const string net_uri_UserDrivenParsing = "A derived type '{0}' is responsible for parsing this Uri instance. The base implementation must not be used.";

        internal static string Format(string format, object? arg) =>
            string.Format(format, arg);

        internal static string Format(string format, object? first, object? second) =>
            string.Format(format, first, second);
    }

    public static partial class Math
    {
        internal static void ThrowMinMaxException<T>(T min, T max) => throw new ArgumentException();
        internal static void ThrowNegateTwosCompOverflow() => throw new OverflowException();
    }

    internal static partial class Number
    {

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
            bits = ConvertDecimalToFloatingBits<TFloat>(
                digits,
                digits.Length,
                exponent,
                negative: false);
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
            GetFloatingRational(
                bits,
                out var numerator,
                out var denominator,
                TFloat.ExponentBits,
                TFloat.ExponentBias,
                TFloat.NormalMantissaBits);
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
            var builder = new ValueStringBuilder(64);
            AppendDecimalIeee754<TDecimal, TValue>(ref builder, value, format.AsSpan(), info);
            return builder.ToString();
        }

        private static void AppendDecimalIeee754<TDecimal, TValue>(
            scoped ref ValueStringBuilder builder,
            TValue value,
            scoped ReadOnlySpan<char> format,
            NumberFormatInfo info)
            where TDecimal : unmanaged, IDecimalIeee754ParseAndFormatInfo<TDecimal, TValue>
            where TValue : unmanaged, IBinaryInteger<TValue>
        {
            if (TDecimal.IsNaN(value)) { builder.Append(NumberFormatInfo.InvariantNaNSymbol); return; }
            if (TDecimal.IsInfinity(value))
            {
                builder.Append(TDecimal.IsNegative(value)
                    ? NumberFormatInfo.InvariantNegativeInfinitySymbol
                    : NumberFormatInfo.InvariantPositiveInfinitySymbol);
                return;
            }

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

            if (!format.IsEmpty)
            {
                specifier = format[0];
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
                AppendScientific(ref builder, digits, decoded.UnbiasedExponent, negative, places, specifier);
                return;
            }

            if (specifier is 'C' or 'c')
            {
                var places = precision < 0 ? info.CurrencyDecimalDigits : precision;
                RoundToFixed(ref digits, ref decoded, places);
                if (negative) builder.Append('(');
                builder.Append(info.CurrencySymbol);
                AppendFixed(ref builder, digits, decoded.UnbiasedExponent, negative: false, places, group: true);
                if (negative) builder.Append(')');
                return;
            }

            if (specifier is 'P' or 'p')
            {
                var places = precision < 0 ? info.PercentDecimalDigits : precision;
                decoded = new DecodedDecimalIeee754<TValue>(decoded.Signed, decoded.UnbiasedExponent + 2, decoded.Significand);
                RoundToFixed(ref digits, ref decoded, places);
                AppendFixed(ref builder, digits, decoded.UnbiasedExponent, negative, places, group: true);
                builder.Append(" %");
                return;
            }

            if (specifier is 'F' or 'f' or 'N' or 'n')
            {
                var places = precision < 0 ? 2 : precision;
                RoundToFixed(ref digits, ref decoded, places);
                AppendFixed(ref builder, digits, decoded.UnbiasedExponent, negative, places, specifier is 'N' or 'n');
                return;
            }

            if (precision >= 0) RoundCoefficient(ref digits, ref decoded, precision);
            AppendGeneral(ref builder, digits, decoded.UnbiasedExponent, negative, TDecimal.Precision, specifier);
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
            if (typeof(TChar) == typeof(char))
            {
                var chars = System.Runtime.InteropServices.MemoryMarshal.Cast<TChar, char>(destination);
                var builder = new ValueStringBuilder(chars);
                AppendDecimalIeee754<TDecimal, TValue>(ref builder, value, format, info);
                return builder.TryCopyTo(chars, out charsWritten);
            }
            Span<char> buffer = stackalloc char[128];
            var utf16 = new ValueStringBuilder(buffer);
            AppendDecimalIeee754<TDecimal, TValue>(ref utf16, value, format, info);
            var success = TryCopyUtf8(utf16.AsSpan(), System.Runtime.InteropServices.MemoryMarshal.Cast<TChar, byte>(destination), out charsWritten);
            utf16.Dispose();
            return success;
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
            var index = 0;
            var allowTrailing = (styles & AllowTrailingInvalidCharacters) != 0;
            var allowLeadingWhite = (styles & NumberStyles.AllowLeadingWhite) != 0;
            var allowTrailingWhite = (styles & NumberStyles.AllowTrailingWhite) != 0;

            if (allowLeadingWhite) while (index < value.Length && IsAsciiWhiteSpace(ReadSourceChar(value, index))) index++;
            else if (index < value.Length && IsAsciiWhiteSpace(ReadSourceChar(value, index))) return ParsingStatus.Failed;

            var start = index;
            var negative = false;
            var hadLeadingSign = false;
            var parenthesized = false;
            if ((styles & NumberStyles.AllowParentheses) != 0 && index < value.Length && ReadSourceChar(value, index) == '(')
            {
                parenthesized = true;
                index++;
            }
            if ((styles & NumberStyles.AllowCurrencySymbol) != 0 && index < value.Length && ReadSourceChar(value, index) == '$') index++;
            if (index < value.Length && ReadSourceChar(value, index) is '+' or '-')
            {
                if ((styles & NumberStyles.AllowLeadingSign) == 0) return ParsingStatus.Failed;
                hadLeadingSign = true;
                negative = ReadSourceChar(value, index++) == '-';
            }

            var specialStart = index;
            if (MatchWord(value, ref index, "Infinity") || MatchWord(value, ref index, "NaN"))
            {
                var trailing = ConsumeTrailing(value, index, allowTrailingWhite);
                if (trailing < 0 || (!allowTrailing && trailing != value.Length)) return ParsingStatus.Failed;
                elementsConsumed = allowTrailing ? trailing : value.Length;
                var wasNaN = ReadSourceChar(value, specialStart) is 'N' or 'n';
                var bits = wasNaN ? TDecimal.NaN : negative ? TDecimal.NegativeInfinity : TDecimal.PositiveInfinity;
                if (wasNaN && negative) bits |= TDecimal.SignMask;
                result = TDecimal.Construct(bits);
                return ParsingStatus.OK;
            }

            index = start;
            parenthesized = false;
            if ((styles & NumberStyles.AllowParentheses) != 0 && index < value.Length && ReadSourceChar(value, index) == '(')
            {
                parenthesized = true;
                index++;
            }
            if ((styles & NumberStyles.AllowCurrencySymbol) != 0 && index < value.Length && ReadSourceChar(value, index) == '$') index++;
            if (index < value.Length && ReadSourceChar(value, index) is '+' or '-')
            {
                if ((styles & NumberStyles.AllowLeadingSign) == 0) return ParsingStatus.Failed;
                hadLeadingSign = true;
                negative = ReadSourceChar(value, index++) == '-';
            }
            if ((styles & NumberStyles.AllowCurrencySymbol) != 0 && index < value.Length && ReadSourceChar(value, index) == '$') index++;

            var rawCount = 0;
            var decimalIndex = -1;
            var sawDigit = false;
            var exponent = 0;
            var exponentNegative = false;
            var firstNonZero = -1;
            var storedCount = 0;
            var hasNonZeroTail = false;
            Span<byte> digits = stackalloc byte[TDecimal.BufferLength];
            var number = new NumberBuffer(NumberBufferKind.Decimal, digits);
            var capacity = digits.Length - 1;
            while (index < value.Length)
            {
                var c = ReadSourceChar(value, index);
                if (c is >= '0' and <= '9')
                {
                    if (firstNonZero < 0 && c != '0')
                    {
                        firstNonZero = rawCount;
                    }
                    if (firstNonZero >= 0)
                    {
                        if (storedCount < capacity)
                        {
                            number.Digits[storedCount++] = (byte)c;
                        }
                        else if (c != '0')
                        {
                            hasNonZeroTail = true;
                        }
                    }
                    rawCount++;
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

            if (index < value.Length && ReadSourceChar(value, index) is 'e' or 'E')
            {
                if ((styles & NumberStyles.AllowExponent) == 0) return ParsingStatus.Failed;
                index++;
                if (index < value.Length && ReadSourceChar(value, index) is '+' or '-')
                {
                    exponentNegative = ReadSourceChar(value, index++) == '-';
                }
                var exponentDigits = 0;
                while (index < value.Length && ReadSourceChar(value, index) is >= '0' and <= '9')
                {
                    if (exponentDigits < 6) exponent = exponent * 10 + ReadSourceChar(value, index) - '0';
                    exponentDigits++;
                    index++;
                }
                if (exponentDigits == 0) return ParsingStatus.Failed;
                if (exponentNegative) exponent = -exponent;
            }

            if ((styles & NumberStyles.AllowTrailingSign) != 0 && index < value.Length && ReadSourceChar(value, index) is '+' or '-')
            {
                if (hadLeadingSign) return ParsingStatus.Failed;
                negative = ReadSourceChar(value, index++) == '-';
            }
            if (parenthesized)
            {
                if (index >= value.Length || ReadSourceChar(value, index++) != ')') return ParsingStatus.Failed;
                negative = true;
            }

            var trailingIndex = ConsumeTrailing(value, index, allowTrailingWhite);
            if (trailingIndex < 0 || (!allowTrailing && trailingIndex != value.Length)) return ParsingStatus.Failed;
            elementsConsumed = allowTrailing ? trailingIndex : value.Length;

            number.IsNegative = negative;
            if (firstNonZero < 0)
            {
                number.Scale = 0;
                result = TDecimal.Construct(NumberToDecimalIeee754<TDecimal, TValue>(ref number));
                return ParsingStatus.OK;
            }

            number.Digits[storedCount] = 0;
            number.DigitsCount = storedCount;
            number.HasNonZeroTail = hasNonZeroTail;
            number.Scale = decimalIndex + exponent - firstNonZero;
            result = TDecimal.Construct(NumberToDecimalIeee754<TDecimal, TValue>(ref number));
            return ParsingStatus.OK;
        }

        private static int ConsumeTrailing<TChar>(ReadOnlySpan<TChar> source, int index, bool allowTrailingWhite)
            where TChar : unmanaged
        {
            var end = index;
            if (allowTrailingWhite) while (end < source.Length && IsAsciiWhiteSpace(ReadSourceChar(source, end))) end++;
            return end;
        }

        private static int ParseDecimalFormatPrecision(ReadOnlySpan<char> format)
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

        private static void AppendGeneral(
            scoped ref ValueStringBuilder builder,
            string digits,
            int exponent,
            bool negative,
            int precision,
            char specifier)
        {
            if (digits == "0") { builder.Append(negative ? "-0" : "0"); return; }

            // General formatting suppresses insignificant trailing zeros after rounding. Moving a
            // zero from the coefficient into the exponent preserves the represented value.
            var digitCount = digits.Length;
            while (digitCount > 1 && digits[digitCount - 1] == '0')
            {
                digitCount--;
                exponent++;
            }

            var decimalExponent = digitCount + exponent - 1;
            var digitBuffer = new char[digitCount];
            for (var index = 0; index < digitCount; index++) digitBuffer[index] = digits[index];
            AppendGeneralDigits(
                ref builder,
                digitBuffer,
                digitCount,
                decimalExponent,
                negative,
                precision < 0 ? int.MaxValue : precision,
                specifier is 'g');
        }

        private static void AppendFixed(
            scoped ref ValueStringBuilder builder,
            string digits,
            int exponent,
            bool negative,
            int places,
            bool group)
        {
            var position = digits.Length + exponent;
            var leadingZeros = position <= 0 ? -position : 0;
            var fractionLength = position < digits.Length ? digits.Length - Math.Max(position, 0) : 0;
            fractionLength = Math.Max(fractionLength, places);
            if (position <= 0)
            {
                fractionLength = Math.Max(fractionLength, leadingZeros + digits.Length);
            }

            if (negative) builder.Append('-');
            if (position <= 0)
            {
                builder.Append('0');
                builder.Append('.');
                builder.Append('0', leadingZeros);
                builder.Append(digits);
                var writtenFraction = leadingZeros + digits.Length;
                if (fractionLength > writtenFraction) builder.Append('0', fractionLength - writtenFraction);
            }
            else
            {
                for (var i = 0; i < position; i++)
                {
                    if (group && i != 0 && (position - i) % 3 == 0) builder.Append(',');
                    builder.Append(i < digits.Length ? digits[i] : '0');
                }
                if (fractionLength != 0)
                {
                    builder.Append('.');
                    for (var i = 0; i < fractionLength; i++) builder.Append(position + i < digits.Length ? digits[position + i] : '0');
                }
            }
        }

        private static void AppendScientific(
            scoped ref ValueStringBuilder builder,
            string digits,
            int exponent,
            bool negative,
            int places,
            char specifier)
        {
            var scientificExponent = exponent + digits.Length - 1;
            var exponentMagnitude = scientificExponent < 0 ? -scientificExponent : scientificExponent;
            var exponentDigits = 3;
            for (var remaining = exponentMagnitude; remaining >= 1000; remaining /= 10)
            {
                exponentDigits++;
            }
            if (negative) builder.Append('-');
            builder.Append(digits[0]);
            if (places != 0)
            {
                builder.Append('.');
                for (var i = 0; i < places; i++) builder.Append(i + 1 < digits.Length ? digits[i + 1] : '0');
            }
            builder.Append(specifier);
            builder.Append(scientificExponent < 0 ? '-' : '+');
            Span<char> exponentBuffer = stackalloc char[10];
            var at = exponentBuffer.Length;
            for (var i = exponentDigits - 1; i >= 0; i--)
            {
                exponentBuffer[--at] = (char)('0' + exponentMagnitude % 10);
                exponentMagnitude /= 10;
            }
            builder.Append(exponentBuffer[at..]);
        }

    }
}
