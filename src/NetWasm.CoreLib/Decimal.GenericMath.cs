// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 474ed963699fc29809015392738a15231aaae2de.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace System
{
    public readonly partial struct Decimal :
        IFormattable,
        ISpanFormattable,
        IParsable<decimal>,
        ISpanParsable<decimal>,
        IUtf8SpanFormattable,
        IUtf8SpanParsable<decimal>
    {
        private const NumberStyles DecimalParseStyles =
            NumberStyles.AllowLeadingWhite |
            NumberStyles.AllowTrailingWhite |
            NumberStyles.AllowLeadingSign |
            NumberStyles.AllowTrailingSign |
            NumberStyles.AllowParentheses |
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowThousands |
            NumberStyles.AllowExponent |
            NumberStyles.AllowCurrencySymbol;

        static decimal IAdditiveIdentity<decimal, decimal>.AdditiveIdentity => Zero;

        static decimal IMultiplicativeIdentity<decimal, decimal>.MultiplicativeIdentity => One;

        static decimal ISignedNumber<decimal>.NegativeOne => MinusOne;

        static decimal IMinMaxValue<decimal>.MinValue => MinValue;

        static decimal IMinMaxValue<decimal>.MaxValue => MaxValue;

        static decimal INumberBase<decimal>.One => One;

        static int INumberBase<decimal>.Radix => 10;

        static decimal INumberBase<decimal>.Zero => Zero;

        static decimal IFloatingPointConstants<decimal>.E => 2.7182818284590452353602874714m;

        static decimal IFloatingPointConstants<decimal>.Pi => 3.1415926535897932384626433833m;

        static decimal IFloatingPointConstants<decimal>.Tau => 6.2831853071795864769252867666m;

        public static decimal E => 2.7182818284590452353602874714m;

        public static decimal Pi => 3.1415926535897932384626433833m;

        public static decimal Tau => 6.2831853071795864769252867666m;

        public static decimal MaxNumber(decimal x, decimal y) => Max(x, y);

        public static decimal MinNumber(decimal x, decimal y) => Min(x, y);

        public static decimal MaxNative(decimal x, decimal y) => x > y ? x : y;

        public static decimal MinNative(decimal x, decimal y) => x < y ? x : y;

        public static decimal ClampNative(decimal value, decimal min, decimal max)
        {
            if (min > max)
            {
                throw new ArgumentException();
            }
            return MinNative(MaxNative(value, min), max);
        }

        public static decimal MultiplyAddEstimate(decimal left, decimal right, decimal addend) =>
            (left * right) + addend;

        public static TInteger ConvertToInteger<TInteger>(decimal value)
            where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

        public static TInteger ConvertToIntegerNative<TInteger>(decimal value)
            where TInteger : IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

        int IFloatingPoint<decimal>.GetExponentByteCount() => sizeof(sbyte);

        int IFloatingPoint<decimal>.GetExponentShortestBitLength()
        {
            var exponent = Exponent;
            var magnitude = exponent >= 0 ? exponent : ~exponent;
            var bits = 0;
            while (magnitude != 0)
            {
                bits++;
                magnitude >>= 1;
            }
            return exponent < 0 ? bits + 1 : bits;
        }

        int IFloatingPoint<decimal>.GetSignificandBitLength() => 96;

        int IFloatingPoint<decimal>.GetSignificandByteCount() => sizeof(ulong) + sizeof(uint);

        bool IFloatingPoint<decimal>.TryWriteExponentBigEndian(Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < sizeof(sbyte))
            {
                bytesWritten = 0;
                return false;
            }

            destination[0] = (byte)Exponent;
            bytesWritten = sizeof(sbyte);
            return true;
        }

        bool IFloatingPoint<decimal>.TryWriteExponentLittleEndian(Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < sizeof(sbyte))
            {
                bytesWritten = 0;
                return false;
            }

            destination[0] = (byte)Exponent;
            bytesWritten = sizeof(sbyte);
            return true;
        }

        bool IFloatingPoint<decimal>.TryWriteSignificandBigEndian(Span<byte> destination, out int bytesWritten)
        {
            const int byteCount = sizeof(uint) + sizeof(ulong);
            if (destination.Length < byteCount)
            {
                bytesWritten = 0;
                return false;
            }

            WriteUInt32BigEndian(destination, High);
            WriteUInt32BigEndian(destination.Slice(4), Mid);
            WriteUInt32BigEndian(destination.Slice(8), Low);
            bytesWritten = byteCount;
            return true;
        }

        bool IFloatingPoint<decimal>.TryWriteSignificandLittleEndian(Span<byte> destination, out int bytesWritten)
        {
            const int byteCount = sizeof(ulong) + sizeof(uint);
            if (destination.Length < byteCount)
            {
                bytesWritten = 0;
                return false;
            }

            WriteUInt32LittleEndian(destination, Low);
            WriteUInt32LittleEndian(destination.Slice(4), Mid);
            WriteUInt32LittleEndian(destination.Slice(8), High);
            bytesWritten = byteCount;
            return true;
        }

        public static bool IsCanonical(decimal value)
        {
            if (value.Scale == 0)
            {
                return true;
            }

            DecimalMath.DivideWord(DecimalMath.Coefficient(value), 10, out var remainder);
            return remainder != 0;
        }

        public static bool IsComplexNumber(decimal value) => false;

        static bool INumberBase<decimal>.IsComplexNumber(decimal value) => IsComplexNumber(value);

        public static bool IsEvenInteger(decimal value)
        {
            var truncated = Truncate(value);
            return value == truncated && (truncated.Low & 1U) == 0;
        }

        public static bool IsFinite(decimal value) => true;

        static bool INumberBase<decimal>.IsFinite(decimal value) => IsFinite(value);

        public static bool IsImaginaryNumber(decimal value) => false;

        static bool INumberBase<decimal>.IsImaginaryNumber(decimal value) => IsImaginaryNumber(value);

        public static bool IsInfinity(decimal value) => false;

        static bool INumberBase<decimal>.IsInfinity(decimal value) => IsInfinity(value);

        public static bool IsInteger(decimal value) => value == Truncate(value);

        public static bool IsNaN(decimal value) => false;

        static bool INumberBase<decimal>.IsNaN(decimal value) => IsNaN(value);

        static bool INumberBase<decimal>.IsNegative(decimal value) => value.IsNegativeValue;

        public static bool IsNegativeInfinity(decimal value) => false;

        static bool INumberBase<decimal>.IsNegativeInfinity(decimal value) => IsNegativeInfinity(value);

        public static bool IsNormal(decimal value) => !value.IsZero;

        static bool INumberBase<decimal>.IsNormal(decimal value) => IsNormal(value);

        public static bool IsOddInteger(decimal value)
        {
            var truncated = Truncate(value);
            return value == truncated && (truncated.Low & 1U) != 0;
        }

        public static bool IsPositive(decimal value) => !value.IsNegativeValue;

        static bool INumberBase<decimal>.IsPositive(decimal value) => IsPositive(value);

        public static bool IsPositiveInfinity(decimal value) => false;

        static bool INumberBase<decimal>.IsPositiveInfinity(decimal value) => IsPositiveInfinity(value);

        public static bool IsRealNumber(decimal value) => true;

        static bool INumberBase<decimal>.IsRealNumber(decimal value) => IsRealNumber(value);

        public static bool IsSubnormal(decimal value) => false;

        static bool INumberBase<decimal>.IsSubnormal(decimal value) => IsSubnormal(value);

        static bool INumberBase<decimal>.IsZero(decimal value) => value.IsZero;

        public static decimal CopySign(decimal value, decimal sign) => new(
            value.Low,
            value.Mid,
            value.High,
            sign.IsNegativeValue,
            value.Scale);

        static decimal INumber<decimal>.MaxNumber(decimal x, decimal y) => MaxNumber(x, y);

        static decimal INumber<decimal>.MinNumber(decimal x, decimal y) => MinNumber(x, y);

        public static decimal MaxMagnitude(decimal x, decimal y)
        {
            var comparison = Compare(Abs(x), Abs(y));
            if (comparison > 0)
            {
                return x;
            }
            if (comparison == 0)
            {
                return x.IsNegativeValue ? y : x;
            }
            return y;
        }

        public static decimal MaxMagnitudeNumber(decimal x, decimal y) => MaxMagnitude(x, y);

        static decimal INumberBase<decimal>.MaxMagnitudeNumber(decimal x, decimal y) => MaxMagnitudeNumber(x, y);

        public static decimal MinMagnitude(decimal x, decimal y)
        {
            var comparison = Compare(Abs(x), Abs(y));
            if (comparison < 0)
            {
                return x;
            }
            if (comparison == 0)
            {
                return x.IsNegativeValue ? x : y;
            }
            return y;
        }

        public static decimal MinMagnitudeNumber(decimal x, decimal y) => MinMagnitude(x, y);

        static decimal INumberBase<decimal>.MinMagnitudeNumber(decimal x, decimal y) => MinMagnitudeNumber(x, y);

        public static decimal Parse(string s, NumberStyles style, IFormatProvider? provider)
        {
            if (s == null)
            {
                throw new ArgumentNullException();
            }
            return ParseCore(s, style);
        }

        public static decimal Parse(ReadOnlySpan<char> s, System.Globalization.NumberStyles style = System.Globalization.NumberStyles.Number, IFormatProvider? provider = null) =>
            ParseCore(s, style);

        public static decimal Parse(string s, NumberStyles style) => Parse(s, style, provider: null);

        public static decimal Parse(string s, IFormatProvider? provider) => Parse(s, NumberStyles.Number, provider);

        public static decimal Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
            Parse(s, NumberStyles.Number, provider);

        public static bool TryParse(
            string? s,
            NumberStyles style,
            IFormatProvider? provider,
            out decimal result) => TryParseCore(s, style, out result);

        public static bool TryParse(
            ReadOnlySpan<char> s,
            NumberStyles style,
            IFormatProvider? provider,
            out decimal result) => TryParseCore(s, style, out result);

        public static bool TryParse(ReadOnlySpan<char> s, out decimal result) =>
            TryParse(s, NumberStyles.Number, provider: null, out result);

        public static bool TryParse(string? s, IFormatProvider? provider, out decimal result) =>
            TryParse(s, NumberStyles.Number, provider, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out decimal result) =>
            TryParse(s, NumberStyles.Number, provider, out result);

        public static decimal Parse(ReadOnlySpan<byte> utf8Text, System.Globalization.NumberStyles style = System.Globalization.NumberStyles.Number, IFormatProvider? provider = null)
            => ParseUtf8Core(utf8Text, style);

        public static decimal Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) =>
            Parse(utf8Text, NumberStyles.Number, provider);

        public static bool TryParse(ReadOnlySpan<byte> utf8Text, out decimal result) =>
            TryParse(utf8Text, NumberStyles.Number, provider: null, out result);

        public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out decimal result) =>
            TryParse(utf8Text, NumberStyles.Number, provider, out result);

        public static bool TryParse(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider? provider, out decimal result)
            => TryParseUtf8Core(utf8Text, style, out result);

        public static bool TryParsePartial(
            [NotNullWhen(true)] string? s,
            NumberStyles style,
            IFormatProvider? provider,
            out decimal result,
            out int charsConsumed) => TryParsePartialCore(s, style, out result, out charsConsumed);

        public static bool TryParsePartial(
            ReadOnlySpan<char> s,
            NumberStyles style,
            IFormatProvider? provider,
            out decimal result,
            out int charsConsumed) => TryParsePartialCore(s, style, out result, out charsConsumed);

        public static bool TryParsePartial(
            ReadOnlySpan<byte> utf8Text,
            NumberStyles style,
            IFormatProvider? provider,
            out decimal result,
            out int bytesConsumed)
            => TryParsePartialUtf8Core(utf8Text, style, out result, out bytesConsumed);

        // A format or a provider takes upstream's layout with the provider's NumberFormatInfo;
        // with neither, the general form is written without that machinery.
        public string ToString(string? format, IFormatProvider? provider) =>
            string.IsNullOrEmpty(format) && provider is null
                ? ToString()
                : Number.FormatDecimal(this, format, NumberFormatInfo.GetInstance(provider));

        public string ToString(string? format) => ToString(format, null);

        // Without a format a provider changes nothing: NetWasm's NumberFormatInfo is the invariant one.
        public string ToString(IFormatProvider? provider) => ToString();

        public bool TryFormat(
            Span<char> destination,
            out int charsWritten,
            ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
            IFormatProvider? provider = null)
        {
            if (!format.IsEmpty || provider is not null)
            {
                return Number.TryFormatDecimal(this, format, NumberFormatInfo.GetInstance(provider), destination, out charsWritten);
            }
            var builder = new ValueStringBuilder(destination);
            DecimalMath.AppendFormat(ref builder, this);
            return builder.TryCopyTo(destination, out charsWritten);
        }

        public bool TryFormat(
            Span<byte> utf8Destination,
            out int bytesWritten,
            ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
            IFormatProvider? provider = null)
        {
            if (!format.IsEmpty || provider is not null)
            {
                return Number.TryFormatDecimal(this, format, NumberFormatInfo.GetInstance(provider), utf8Destination, out bytesWritten);
            }
            Span<char> buffer = stackalloc char[64];
            var builder = new ValueStringBuilder(buffer);
            DecimalMath.AppendFormat(ref builder, this);
            var success = Number.TryCopyUtf8(builder.AsSpan(), utf8Destination, out bytesWritten);
            builder.Dispose();
            return success;
        }

        /// <inheritdoc cref="INumberBase{TSelf}.CreateChecked{TOther}(TOther)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal CreateChecked<TOther>(TOther value)
            where TOther : INumberBase<TOther>
        {
            decimal result;

            if (typeof(TOther) == typeof(decimal))
            {
                result = (decimal)(object)value;
            }
            else if (!TryConvertFromChecked(value, out result) && !TOther.TryConvertToChecked(value, out result))
            {
                ThrowHelper.ThrowNotSupportedException();
            }

            return result;
        }

        /// <inheritdoc cref="INumberBase{TSelf}.CreateSaturating{TOther}(TOther)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal CreateSaturating<TOther>(TOther value)
            where TOther : INumberBase<TOther>
        {
            decimal result;

            if (typeof(TOther) == typeof(decimal))
            {
                result = (decimal)(object)value;
            }
            else if (!TryConvertFrom(value, out result) && !TOther.TryConvertToSaturating(value, out result))
            {
                ThrowHelper.ThrowNotSupportedException();
            }

            return result;
        }

        /// <inheritdoc cref="INumberBase{TSelf}.CreateTruncating{TOther}(TOther)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static decimal CreateTruncating<TOther>(TOther value)
            where TOther : INumberBase<TOther>
        {
            decimal result;

            if (typeof(TOther) == typeof(decimal))
            {
                result = (decimal)(object)value;
            }
            else if (!TryConvertFrom(value, out result) && !TOther.TryConvertToTruncating(value, out result))
            {
                ThrowHelper.ThrowNotSupportedException();
            }

            return result;
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromChecked{TOther}(TOther, out TSelf)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool INumberBase<decimal>.TryConvertFromChecked<TOther>(TOther value, out decimal result) => TryConvertFromChecked(value, out result);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryConvertFromChecked<TOther>(TOther value, out decimal result)
            where TOther : INumberBase<TOther>
        {
            // In order to reduce overall code duplication and improve the inlinabilty of these
            // methods for the corelib types we have `ConvertFrom` handle the same sign and
            // `ConvertTo` handle the opposite sign. However, since there is an uneven split
            // between signed and unsigned types, the one that handles unsigned will also
            // handle `Decimal`.
            //
            // That is, `ConvertFrom` for `decimal` will handle the other unsigned types and
            // `ConvertTo` will handle the signed types

            if (typeof(TOther) == typeof(byte))
            {
                byte actualValue = (byte)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(char))
            {
                char actualValue = (char)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(ushort))
            {
                ushort actualValue = (ushort)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(uint))
            {
                uint actualValue = (uint)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(ulong))
            {
                ulong actualValue = (ulong)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(UInt128))
            {
                UInt128 actualValue = (UInt128)(object)value;
                result = checked((decimal)actualValue);
                return true;
            }
            else if (typeof(TOther) == typeof(nuint))
            {
                nuint actualValue = (nuint)(object)value;
                result = actualValue;
                return true;
            }
            else
            {
                result = default;
                return false;
            }
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromSaturating{TOther}(TOther, out TSelf)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool INumberBase<decimal>.TryConvertFromSaturating<TOther>(TOther value, out decimal result)
        {
            return TryConvertFrom(value, out result);
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryConvertFromTruncating{TOther}(TOther, out TSelf)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool INumberBase<decimal>.TryConvertFromTruncating<TOther>(TOther value, out decimal result)
        {
            return TryConvertFrom(value, out result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryConvertFrom<TOther>(TOther value, out decimal result)
            where TOther : INumberBase<TOther>
        {
            // In order to reduce overall code duplication and improve the inlinabilty of these
            // methods for the corelib types we have `ConvertFrom` handle the same sign and
            // `ConvertTo` handle the opposite sign. However, since there is an uneven split
            // between signed and unsigned types, the one that handles unsigned will also
            // handle `Decimal`.
            //
            // That is, `ConvertFrom` for `decimal` will handle the other unsigned types and
            // `ConvertTo` will handle the signed types

            if (typeof(TOther) == typeof(byte))
            {
                byte actualValue = (byte)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(char))
            {
                char actualValue = (char)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(ushort))
            {
                ushort actualValue = (ushort)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(uint))
            {
                uint actualValue = (uint)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(ulong))
            {
                ulong actualValue = (ulong)(object)value;
                result = actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(UInt128))
            {
                UInt128 actualValue = (UInt128)(object)value;
                result = actualValue.Upper > uint.MaxValue ? MaxValue : (decimal)actualValue;
                return true;
            }
            else if (typeof(TOther) == typeof(nuint))
            {
                nuint actualValue = (nuint)(object)value;
                result = actualValue;
                return true;
            }
            else
            {
                result = default;
                return false;
            }
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryConvertToChecked{TOther}(TSelf, out TOther)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool INumberBase<decimal>.TryConvertToChecked<TOther>(decimal value, [MaybeNullWhen(false)] out TOther result)
        {
            // In order to reduce overall code duplication and improve the inlinabilty of these
            // methods for the corelib types we have `ConvertFrom` handle the same sign and
            // `ConvertTo` handle the opposite sign. However, since there is an uneven split
            // between signed and unsigned types, the one that handles unsigned will also
            // handle `Decimal`.
            //
            // That is, `ConvertFrom` for `decimal` will handle the other unsigned types and
            // `ConvertTo` will handle the signed types

            if (typeof(TOther) == typeof(double))
            {
                double actualResult = checked((double)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(Half))
            {
                Half actualResult = checked((Half)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(short))
            {
                short actualResult = checked((short)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(int))
            {
                int actualResult = checked((int)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(long))
            {
                long actualResult = checked((long)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(Int128))
            {
                Int128 actualResult = checked((Int128)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(nint))
            {
                nint actualResult = checked((nint)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(sbyte))
            {
                sbyte actualResult = checked((sbyte)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(float))
            {
                float actualResult = checked((float)value);
                result = (TOther)(object)actualResult;
                return true;
            }
            else
            {
                result = default;
                return false;
            }
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryConvertToSaturating{TOther}(TSelf, out TOther)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool INumberBase<decimal>.TryConvertToSaturating<TOther>(decimal value, [MaybeNullWhen(false)] out TOther result)
        {
            return TryConvertTo(value, out result);
        }

        /// <inheritdoc cref="INumberBase{TSelf}.TryConvertToTruncating{TOther}(TSelf, out TOther)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool INumberBase<decimal>.TryConvertToTruncating<TOther>(decimal value, [MaybeNullWhen(false)] out TOther result)
        {
            return TryConvertTo(value, out result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryConvertTo<TOther>(decimal value, [MaybeNullWhen(false)] out TOther result)
            where TOther : INumberBase<TOther>
        {
            // In order to reduce overall code duplication and improve the inlinabilty of these
            // methods for the corelib types we have `ConvertFrom` handle the same sign and
            // `ConvertTo` handle the opposite sign. However, since there is an uneven split
            // between signed and unsigned types, the one that handles unsigned will also
            // handle `Decimal`.
            //
            // That is, `ConvertFrom` for `decimal` will handle the other unsigned types and
            // `ConvertTo` will handle the signed types

            if (typeof(TOther) == typeof(double))
            {
                double actualResult = (double)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(Half))
            {
                Half actualResult = (Half)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(short))
            {
                short actualResult = (value >= short.MaxValue) ? short.MaxValue :
                                     (value <= short.MinValue) ? short.MinValue : (short)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(int))
            {
                int actualResult = (value >= int.MaxValue) ? int.MaxValue :
                                   (value <= int.MinValue) ? int.MinValue : (int)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(long))
            {
                long actualResult = (value >= long.MaxValue) ? long.MaxValue :
                                    (value <= long.MinValue) ? long.MinValue : (long)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(Int128))
            {
                Int128 actualResult = (Int128)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(nint))
            {
                nint actualResult = (value >= nint.MaxValue) ? nint.MaxValue :
                                    (value <= nint.MinValue) ? nint.MinValue : (nint)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(sbyte))
            {
                sbyte actualResult = (value >= sbyte.MaxValue) ? sbyte.MaxValue :
                                     (value <= sbyte.MinValue) ? sbyte.MinValue : (sbyte)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else if (typeof(TOther) == typeof(float))
            {
                float actualResult = (float)value;
                result = (TOther)(object)actualResult;
                return true;
            }
            else
            {
                result = default;
                return false;
            }
        }

        static bool INumberBase<decimal>.TryParse(
            string? s,
            NumberStyles style,
            IFormatProvider? provider,
            [MaybeNullWhen(false)] out decimal result) => TryParseCore(s, style, out result);

        static bool INumberBase<decimal>.TryParse(
            ReadOnlySpan<char> s,
            NumberStyles style,
            IFormatProvider? provider,
            [MaybeNullWhen(false)] out decimal result) => TryParseCore(s, style, out result);

        private static decimal ParseCore(string text, NumberStyles style) =>
            ParseCore(text.AsSpan(), style);

        private static decimal ParseCore(ReadOnlySpan<char> text, NumberStyles style)
        {
            if (!TryParseCore(text, style, out var result, out var overflow))
            {
                if (overflow)
                {
                    throw new OverflowException();
                }
                throw new FormatException();
            }
            return result;
        }

        private static bool TryParseCore(string? text, NumberStyles style, out decimal result)
        {
            if (text == null)
            {
                result = Zero;
                ValidateDecimalParseStyle(style);
                return false;
            }
            return TryParseCore(text.AsSpan(), style, out result);
        }

        private static bool TryParseCore(ReadOnlySpan<char> text, NumberStyles style, out decimal result)
            => TryParseCore(text, style, out result, out _);

        private static bool TryParseCore(
            ReadOnlySpan<char> text,
            NumberStyles style,
            out decimal result,
            out bool overflow)
        {
            result = Zero;
            overflow = false;
            ValidateDecimalParseStyle(style);
            if (text.Length == 0)
            {
                return false;
            }

            var start = 0;
            var end = text.Length;
            var leadingWhite = (style & NumberStyles.AllowLeadingWhite) != 0;
            var trailingWhite = (style & NumberStyles.AllowTrailingWhite) != 0;
            while (end > start && text[end - 1] == '\0')
            {
                end--;
            }
            while (start < end && IsAsciiWhiteSpace(text[start]))
            {
                if (!leadingWhite)
                {
                    return false;
                }
                start++;
            }
            while (end > start && IsAsciiWhiteSpace(text[end - 1]))
            {
                if (!trailingWhite)
                {
                    return false;
                }
                end--;
            }
            if (start == end)
            {
                return false;
            }

            var negative = false;
            var parentheses = false;
            if (text[start] == '(')
            {
                if ((style & NumberStyles.AllowParentheses) == 0 || end <= start + 1 || text[end - 1] != ')')
                {
                    return false;
                }
                parentheses = true;
                negative = true;
                start++;
                end--;
            }

            var currency = false;
            if (start < end && text[start] == '$')
            {
                if ((style & NumberStyles.AllowCurrencySymbol) == 0) return false;
                currency = true;
                start++;
            }

            var leadingSign = false;
            if (start < end && (text[start] == '+' || text[start] == '-'))
            {
                if (parentheses || (style & NumberStyles.AllowLeadingSign) == 0)
                {
                    return false;
                }
                leadingSign = true;
                negative = text[start] == '-';
                start++;
            }
            if (start < end && text[start] == '$')
            {
                if (currency || (style & NumberStyles.AllowCurrencySymbol) == 0) return false;
                currency = true;
                start++;
            }

            if (start < end && text[end - 1] == '$')
            {
                if (currency || (style & NumberStyles.AllowCurrencySymbol) == 0) return false;
                currency = true;
                end--;
            }

            var trailingSign = false;
            if (start < end && (text[end - 1] == '+' || text[end - 1] == '-'))
            {
                if (parentheses || leadingSign || (style & NumberStyles.AllowTrailingSign) == 0)
                {
                    return false;
                }
                trailingSign = true;
                negative = text[--end] == '-';
                if (trailingWhite)
                {
                    while (end > start && IsAsciiWhiteSpace(text[end - 1])) end--;
                }
            }
            if (start < end && text[end - 1] == '$')
            {
                if (currency || (style & NumberStyles.AllowCurrencySymbol) == 0) return false;
                currency = true;
                end--;
            }
            if (start >= end || (trailingSign && start >= end))
            {
                return false;
            }

            var normalizedCapacity = end - start + 1;
            if (normalizedCapacity <= 128)
            {
                Span<char> normalized = stackalloc char[normalizedCapacity];
                return TryNormalizeAndParseDecimal(
                    text,
                    start,
                    end,
                    negative,
                    style,
                    normalized,
                    out result,
                    out overflow);
            }

            return TryNormalizeAndParseDecimal(
                text,
                start,
                end,
                negative,
                style,
                new char[normalizedCapacity],
                out result,
                out overflow);
        }

        private static bool TryNormalizeAndParseDecimal(
            ReadOnlySpan<char> text,
            int start,
            int end,
            bool negative,
            NumberStyles style,
            Span<char> normalized,
            out decimal result,
            out bool overflow)
        {
            result = Zero;
            overflow = false;
            var normalizedLength = 0;
            if (negative)
            {
                normalized[normalizedLength++] = '-';
            }
            var sawDigit = false;
            var sawDecimalPoint = false;
            var sawExponent = false;
            for (var index = start; index < end; index++)
            {
                var character = text[index];
                if (character >= '0' && character <= '9')
                {
                    sawDigit = true;
                    normalized[normalizedLength++] = character;
                    continue;
                }
                if (character == '.' && (style & NumberStyles.AllowDecimalPoint) != 0)
                {
                    if (sawDecimalPoint || sawExponent)
                    {
                        return false;
                    }
                    sawDecimalPoint = true;
                    normalized[normalizedLength++] = character;
                    continue;
                }
                if ((character == 'e' || character == 'E') && (style & NumberStyles.AllowExponent) != 0)
                {
                    if (!sawDigit || sawExponent)
                    {
                        return false;
                    }
                    sawExponent = true;
                    normalized[normalizedLength++] = character;
                    continue;
                }
                if ((character == '+' || character == '-') &&
                    normalizedLength > 0 &&
                    (normalized[normalizedLength - 1] == 'e' || normalized[normalizedLength - 1] == 'E'))
                {
                    normalized[normalizedLength++] = character;
                    continue;
                }
                if (character == ',' && sawDigit && !sawDecimalPoint && !sawExponent &&
                    (style & NumberStyles.AllowThousands) != 0)
                {
                    continue;
                }
                return false;
            }
            if (!sawDigit)
            {
                result = Zero;
                return false;
            }

            return DecimalMath.TryParse(
                normalized[..normalizedLength],
                out result,
                out overflow);
        }

        private static void ValidateDecimalParseStyle(NumberStyles style)
        {
            if (((int)style & ~(int)DecimalParseStyles) != 0)
            {
                throw new ArgumentException();
            }
        }

        private static decimal ParseUtf8Core(ReadOnlySpan<byte> value, NumberStyles style)
        {
            char[]? rentedCharacters = null;
            Span<char> characters = value.Length <= 128
                ? stackalloc char[value.Length]
                : rentedCharacters = ArrayPool<char>.Shared.Rent(value.Length);
            try
            {
                if (!TryCopyAscii(value, characters))
                {
                    throw new FormatException();
                }
                return ParseCore(characters[..value.Length], style);
            }
            finally
            {
                if (rentedCharacters is not null)
                {
                    ArrayPool<char>.Shared.Return(rentedCharacters);
                }
            }
        }

        private static bool TryParseUtf8Core(
            ReadOnlySpan<byte> value,
            NumberStyles style,
            out decimal result)
        {
            char[]? rentedCharacters = null;
            Span<char> characters = value.Length <= 128
                ? stackalloc char[value.Length]
                : rentedCharacters = ArrayPool<char>.Shared.Rent(value.Length);
            try
            {
                if (!TryCopyAscii(value, characters))
                {
                    result = Zero;
                    return false;
                }
                return TryParseCore(characters[..value.Length], style, out result);
            }
            finally
            {
                if (rentedCharacters is not null)
                {
                    ArrayPool<char>.Shared.Return(rentedCharacters);
                }
            }
        }

        private static bool TryParsePartialUtf8Core(
            ReadOnlySpan<byte> value,
            NumberStyles style,
            out decimal result,
            out int bytesConsumed)
        {
            char[]? rentedCharacters = null;
            Span<char> characters = value.Length <= 128
                ? stackalloc char[value.Length]
                : rentedCharacters = ArrayPool<char>.Shared.Rent(value.Length);
            try
            {
                if (!TryCopyAscii(value, characters))
                {
                    result = Zero;
                    bytesConsumed = 0;
                    return false;
                }
                return TryParsePartialCore(
                    characters[..value.Length],
                    style,
                    out result,
                    out bytesConsumed);
            }
            finally
            {
                if (rentedCharacters is not null)
                {
                    ArrayPool<char>.Shared.Return(rentedCharacters);
                }
            }
        }

        private static bool TryCopyAscii(ReadOnlySpan<byte> source, Span<char> destination)
        {
            for (var index = 0; index < source.Length; index++)
            {
                if (source[index] > 0x7f)
                {
                    return false;
                }
                destination[index] = (char)source[index];
            }
            return true;
        }

        private static bool TryParsePartialCore(
            string? text,
            NumberStyles style,
            out decimal result,
            out int charsConsumed)
        {
            if (text == null)
            {
                result = Zero;
                charsConsumed = 0;
                ValidateDecimalParseStyle(style);
                return false;
            }
            return TryParsePartialCore(text.AsSpan(), style, out result, out charsConsumed);
        }

        private static bool TryParsePartialCore(
            ReadOnlySpan<char> text,
            NumberStyles style,
            out decimal result,
            out int charsConsumed)
        {
            result = Zero;
            charsConsumed = 0;
            ValidateDecimalParseStyle(style);

            var length = FindPartialDecimalLength(text, style);
            if (length == 0)
            {
                return false;
            }

            if (!TryParseCore(text[..length], style, out result))
            {
                result = Zero;
                return false;
            }

            charsConsumed = length;
            return true;
        }

        private static int FindPartialDecimalLength(ReadOnlySpan<char> text, NumberStyles style)
        {
            var index = 0;
            if ((style & NumberStyles.AllowLeadingWhite) != 0)
            {
                while (index < text.Length && IsAsciiWhiteSpace(text[index]))
                {
                    index++;
                }
            }
            else if (index < text.Length && IsAsciiWhiteSpace(text[index]))
            {
                return 0;
            }

            var parenthesized = false;
            if ((style & NumberStyles.AllowParentheses) != 0 && index < text.Length && text[index] == '(')
            {
                parenthesized = true;
                index++;
            }

            var currency = false;
            if ((style & NumberStyles.AllowCurrencySymbol) != 0 && index < text.Length && text[index] == '$')
            {
                currency = true;
                index++;
            }

            var leadingSign = false;
            if (index < text.Length && text[index] is '+' or '-')
            {
                if (parenthesized || (style & NumberStyles.AllowLeadingSign) == 0)
                {
                    return 0;
                }
                leadingSign = true;
                index++;
            }

            if (!currency && (style & NumberStyles.AllowCurrencySymbol) != 0 &&
                index < text.Length && text[index] == '$')
            {
                currency = true;
                index++;
            }

            var sawDigit = false;
            var sawDecimal = false;
            var lastValid = 0;
            while (index < text.Length)
            {
                var character = text[index];
                if (character is >= '0' and <= '9')
                {
                    sawDigit = true;
                    index++;
                }
                else if (!sawDecimal && character == '.' &&
                    (style & NumberStyles.AllowDecimalPoint) != 0)
                {
                    sawDecimal = true;
                    index++;
                }
                else if (character == ',' && (style & NumberStyles.AllowThousands) != 0)
                {
                    index++;
                }
                else
                {
                    break;
                }

                if (sawDigit && !parenthesized)
                {
                    lastValid = index;
                }
            }

            if (!sawDigit)
            {
                return 0;
            }

            if (index < text.Length && text[index] is 'e' or 'E' &&
                (style & NumberStyles.AllowExponent) != 0)
            {
                var exponentStart = index++;
                if (index < text.Length && text[index] is '+' or '-')
                {
                    index++;
                }
                var exponentDigits = 0;
                while (index < text.Length && text[index] is >= '0' and <= '9')
                {
                    exponentDigits++;
                    index++;
                    if (!parenthesized)
                    {
                        lastValid = index;
                    }
                }
                if (exponentDigits == 0)
                {
                    index = exponentStart;
                }
            }

            var trailingSign = false;
            for (var token = 0; token < 2 && index < text.Length; token++)
            {
                if (!currency && (style & NumberStyles.AllowCurrencySymbol) != 0 && text[index] == '$')
                {
                    currency = true;
                    index++;
                }
                else if (!trailingSign && !leadingSign && !parenthesized &&
                    (style & NumberStyles.AllowTrailingSign) != 0 && text[index] is '+' or '-')
                {
                    trailingSign = true;
                    index++;
                }
                else
                {
                    break;
                }

                if (!parenthesized)
                {
                    lastValid = index;
                }
            }

            if (parenthesized)
            {
                if (index >= text.Length || text[index] != ')')
                {
                    return 0;
                }
                lastValid = ++index;
            }

            if ((style & NumberStyles.AllowTrailingWhite) != 0 && index == lastValid)
            {
                while (index < text.Length && IsAsciiWhiteSpace(text[index]))
                {
                    index++;
                }
                lastValid = index;
            }

            return lastValid;
        }

        private static bool IsAsciiWhiteSpace(char value) => value is ' ' or '\t' or '\r' or '\n' or '\f' or '\v';

        private static void WriteUInt32BigEndian(Span<byte> destination, uint value)
        {
            destination[0] = (byte)(value >> 24);
            destination[1] = (byte)(value >> 16);
            destination[2] = (byte)(value >> 8);
            destination[3] = (byte)value;
        }

        private static void WriteUInt32LittleEndian(Span<byte> destination, uint value)
        {
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
            destination[2] = (byte)(value >> 16);
            destination[3] = (byte)(value >> 24);
        }
    }
}
