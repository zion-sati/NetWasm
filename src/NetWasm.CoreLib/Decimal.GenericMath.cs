// Ported from dotnet/runtime System.Private.CoreLib; upstream commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
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
            if (s is null)
            {
                throw new ArgumentNullException(nameof(s));
            }
            return Parse(s.AsSpan(), style, provider);
        }

        public static decimal Parse(ReadOnlySpan<char> s, NumberStyles style = NumberStyles.Number, IFormatProvider? provider = null)
        {
            NumberFormatInfo.ValidateParseStyleDecimal(style);
            return Number.ParseDecimal(s, style, NumberFormatInfo.GetInstance(provider));
        }

        public static decimal Parse(string s, NumberStyles style) => Parse(s, style, provider: null);

        public static decimal Parse(string s, IFormatProvider? provider) => Parse(s, NumberStyles.Number, provider);

        public static decimal Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, NumberStyles.Number, provider);

        public static bool TryParse([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out decimal result)
        {
            NumberFormatInfo.ValidateParseStyleDecimal(style);
            return Number.TryParseDecimal(s.AsSpan(), style, NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
        }

        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out decimal result)
        {
            NumberFormatInfo.ValidateParseStyleDecimal(style);
            return Number.TryParseDecimal(s, style, NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
        }

        public static bool TryParse(ReadOnlySpan<char> s, out decimal result) => TryParse(s, NumberStyles.Number, provider: null, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out decimal result) => TryParse(s, NumberStyles.Number, provider, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out decimal result) => TryParse(s, NumberStyles.Number, provider, out result);

        public static decimal Parse(ReadOnlySpan<byte> utf8Text, NumberStyles style = NumberStyles.Number, IFormatProvider? provider = null)
        {
            NumberFormatInfo.ValidateParseStyleDecimal(style);
            return Number.ParseDecimal(utf8Text, style, NumberFormatInfo.GetInstance(provider));
        }

        public static decimal Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text, NumberStyles.Number, provider);

        public static bool TryParse(ReadOnlySpan<byte> utf8Text, out decimal result) => TryParse(utf8Text, NumberStyles.Number, provider: null, out result);

        public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out decimal result) => TryParse(utf8Text, NumberStyles.Number, provider, out result);

        public static bool TryParse(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider? provider, out decimal result)
        {
            NumberFormatInfo.ValidateParseStyleDecimal(style);
            return Number.TryParseDecimal(utf8Text, style, NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
        }

        public static bool TryParsePartial([NotNullWhen(true)] string? s, NumberStyles style, IFormatProvider? provider, out decimal result, out int charsConsumed)
        {
            NumberFormatInfo.ValidateParseStyleDecimal(style);
            return Number.TryParseDecimal(s.AsSpan(), style | Number.AllowTrailingInvalidCharacters, NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
        }

        public static bool TryParsePartial(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out decimal result, out int charsConsumed)
        {
            NumberFormatInfo.ValidateParseStyleDecimal(style);
            return Number.TryParseDecimal(s, style | Number.AllowTrailingInvalidCharacters, NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
        }

        public static bool TryParsePartial(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider? provider, out decimal result, out int bytesConsumed)
        {
            NumberFormatInfo.ValidateParseStyleDecimal(style);
            return Number.TryParseDecimal(utf8Text, style | Number.AllowTrailingInvalidCharacters, NumberFormatInfo.GetInstance(provider), out result, out bytesConsumed) == Number.ParsingStatus.OK;
        }

        // A format or a provider takes upstream's layout with the provider's NumberFormatInfo;
        // with neither, the general form is written without that machinery.
        public string ToString(string? format, IFormatProvider? provider) =>
            string.IsNullOrEmpty(format) && provider is null
                ? ToString()
                : Number.FormatDecimal(this, format, NumberFormatInfo.GetInstance(provider));

        public string ToString(string? format) => ToString(format, null);

        // Without a format, the general form with the provider's symbols, without the format string parser.
        public string ToString(IFormatProvider? provider) =>
            provider is null ? ToString() : DecimalMath.Format(this, NumberFormatInfo.GetInstance(provider));

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
            [MaybeNullWhen(false)] out decimal result) => TryParse(s, style, provider, out result);

        static bool INumberBase<decimal>.TryParse(
            ReadOnlySpan<char> s,
            NumberStyles style,
            IFormatProvider? provider,
            [MaybeNullWhen(false)] out decimal result) => TryParse(s, style, provider, out result);

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
