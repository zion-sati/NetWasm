// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 474ed963699fc29809015392738a15231aaae2de.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
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

        public string ToString(string? format, IFormatProvider? provider) => FormatCore(this, format);

        public string ToString(string? format) => FormatCore(this, format);

        public string ToString(IFormatProvider? provider) => FormatCore(this, format: null);

        public bool TryFormat(
            Span<char> destination,
            out int charsWritten,
            ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
            IFormatProvider? provider = null)
        {
            var builder = new ValueStringBuilder(destination);
            AppendFormatCore(ref builder, this, format);
            return builder.TryCopyTo(destination, out charsWritten);
        }

        public bool TryFormat(
            Span<byte> utf8Destination,
            out int bytesWritten,
            ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
            IFormatProvider? provider = null)
        {
            Span<char> buffer = stackalloc char[64];
            var builder = new ValueStringBuilder(buffer);
            AppendFormatCore(ref builder, this, format);
            var success = Number.TryCopyUtf8(builder.AsSpan(), utf8Destination, out bytesWritten);
            builder.Dispose();
            return success;
        }

        static bool INumberBase<decimal>.TryConvertFromChecked<TOther>(
            TOther value,
            [MaybeNullWhen(false)] out decimal result)
        {
            return TryConvertFromChecked(value, out result);
        }

        static bool INumberBase<decimal>.TryConvertFromSaturating<TOther>(
            TOther value,
            [MaybeNullWhen(false)] out decimal result)
        {
            return TryConvertFrom(value, out result);
        }

        static bool INumberBase<decimal>.TryConvertFromTruncating<TOther>(
            TOther value,
            [MaybeNullWhen(false)] out decimal result)
        {
            return TryConvertFrom(value, out result);
        }

        static bool INumberBase<decimal>.TryConvertToChecked<TOther>(
            decimal value,
            [MaybeNullWhen(false)] out TOther result)
        {
            return TryConvertToChecked(value, out result);
        }

        static bool INumberBase<decimal>.TryConvertToSaturating<TOther>(
            decimal value,
            [MaybeNullWhen(false)] out TOther result)
        {
            return TryConvertTo(value, out result);
        }

        static bool INumberBase<decimal>.TryConvertToTruncating<TOther>(
            decimal value,
            [MaybeNullWhen(false)] out TOther result)
        {
            return TryConvertTo(value, out result);
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

        public static decimal CreateChecked<TOther>(TOther value)
            where TOther : INumberBase<TOther>
        {
            if (typeof(TOther) == typeof(decimal))
            {
                return (decimal)(object)value;
            }
            if (TryConvertFromChecked(value, out var result) || TOther.TryConvertToChecked(value, out result))
            {
                return result;
            }
            throw new NotSupportedException();
        }

        public static decimal CreateSaturating<TOther>(TOther value)
            where TOther : INumberBase<TOther>
        {
            if (typeof(TOther) == typeof(decimal))
            {
                return (decimal)(object)value;
            }
            if (TryConvertFrom(value, out var result) || TOther.TryConvertToSaturating(value, out result))
            {
                return result;
            }
            throw new NotSupportedException();
        }

        public static decimal CreateTruncating<TOther>(TOther value)
            where TOther : INumberBase<TOther>
        {
            if (typeof(TOther) == typeof(decimal))
            {
                return (decimal)(object)value;
            }
            if (TryConvertFrom(value, out var result) || TOther.TryConvertToTruncating(value, out result))
            {
                return result;
            }
            throw new NotSupportedException();
        }

        private static bool TryConvertFromChecked<TOther>(
            TOther value,
            out decimal result)
            where TOther : INumberBase<TOther>
        {
            if (typeof(TOther) == typeof(byte))
            {
                result = new decimal((uint)(byte)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(sbyte))
            {
                result = new decimal((int)(sbyte)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(char))
            {
                result = new decimal((uint)(char)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(short))
            {
                result = new decimal((int)(short)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(ushort))
            {
                result = new decimal((uint)(ushort)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(int))
            {
                result = new decimal((int)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(uint))
            {
                result = new decimal((uint)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(long))
            {
                result = new decimal((long)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(ulong))
            {
                result = new decimal((ulong)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(Int128))
            {
                result = (decimal)(Int128)(object)value;
                return true;
            }
            if (typeof(TOther) == typeof(UInt128))
            {
                result = (decimal)(UInt128)(object)value;
                return true;
            }
            if (typeof(TOther) == typeof(nint))
            {
                result = new decimal((long)(nint)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(nuint))
            {
                result = new decimal((ulong)(nuint)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(Half))
            {
                result = (decimal)(Half)(object)value;
                return true;
            }
            if (typeof(TOther) == typeof(float))
            {
                result = (decimal)(float)(object)value;
                return true;
            }
            if (typeof(TOther) == typeof(double))
            {
                result = (decimal)(double)(object)value;
                return true;
            }
            if (typeof(TOther) == typeof(Decimal32))
            {
                result = (decimal)(Decimal32)(object)value;
                return true;
            }
            if (typeof(TOther) == typeof(Decimal64))
            {
                result = (decimal)(Decimal64)(object)value;
                return true;
            }
            if (typeof(TOther) == typeof(Decimal128))
            {
                var actual = (Decimal128)(object)value;
                result = actual > (Decimal128)MaxValue ? MaxValue : actual < (Decimal128)MinValue ? MinValue : (decimal)actual;
                return true;
            }

            result = default;
            return false;
        }

        private static bool TryConvertFrom<TOther>(TOther value, out decimal result)
            where TOther : INumberBase<TOther>
        {
            if (typeof(TOther) == typeof(byte))
            {
                result = new decimal((uint)(byte)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(sbyte))
            {
                result = new decimal((int)(sbyte)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(char))
            {
                result = new decimal((uint)(char)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(short))
            {
                result = new decimal((int)(short)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(ushort))
            {
                result = new decimal((uint)(ushort)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(int))
            {
                result = new decimal((int)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(uint))
            {
                result = new decimal((uint)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(long))
            {
                result = new decimal((long)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(ulong))
            {
                result = new decimal((ulong)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(Int128))
            {
                var actual = (Int128)(object)value;
                result = actual > (Int128)MaxValue ? MaxValue : actual < (Int128)MinValue ? MinValue : (decimal)actual;
                return true;
            }
            if (typeof(TOther) == typeof(UInt128))
            {
                var actual = (UInt128)(object)value;
                result = actual.Upper > uint.MaxValue ? MaxValue : (decimal)actual;
                return true;
            }
            if (typeof(TOther) == typeof(nint))
            {
                result = new decimal((long)(nint)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(nuint))
            {
                result = new decimal((ulong)(nuint)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(Half))
            {
                result = FromFloatingSaturating((double)(float)(Half)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(float))
            {
                result = FromFloatingSaturating((float)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(double))
            {
                result = FromFloatingSaturating((double)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(Decimal32))
            {
                result = FromFloatingSaturating((double)(Decimal32)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(Decimal64))
            {
                result = FromFloatingSaturating((double)(Decimal64)(object)value);
                return true;
            }
            if (typeof(TOther) == typeof(Decimal128))
            {
                result = (decimal)(Decimal128)(object)value;
                return true;
            }

            result = default;
            return false;
        }

        private static decimal FromFloatingSaturating(double value)
        {
            if (double.IsNaN(value))
            {
                return Zero;
            }
            try
            {
                return (decimal)value;
            }
            catch (OverflowException)
            {
                return value < 0 ? MinValue : MaxValue;
            }
        }

        private static bool TryConvertToChecked<TOther>(
            decimal value,
            [MaybeNullWhen(false)] out TOther result)
            where TOther : INumberBase<TOther>
        {
            if (typeof(TOther) == typeof(byte))
            {
                result = (TOther)(object)checked((byte)value);
                return true;
            }
            if (typeof(TOther) == typeof(double))
            {
                result = (TOther)(object)checked((double)value);
                return true;
            }
            if (typeof(TOther) == typeof(Half))
            {
                result = (TOther)(object)checked((Half)value);
                return true;
            }
            if (typeof(TOther) == typeof(char))
            {
                result = (TOther)(object)checked((char)value);
                return true;
            }
            if (typeof(TOther) == typeof(short))
            {
                result = (TOther)(object)checked((short)value);
                return true;
            }
            if (typeof(TOther) == typeof(ushort))
            {
                result = (TOther)(object)checked((ushort)value);
                return true;
            }
            if (typeof(TOther) == typeof(int))
            {
                result = (TOther)(object)checked((int)value);
                return true;
            }
            if (typeof(TOther) == typeof(long))
            {
                result = (TOther)(object)checked((long)value);
                return true;
            }
            if (typeof(TOther) == typeof(uint))
            {
                result = (TOther)(object)checked((uint)value);
                return true;
            }
            if (typeof(TOther) == typeof(ulong))
            {
                result = (TOther)(object)checked((ulong)value);
                return true;
            }
            if (typeof(TOther) == typeof(Int128))
            {
                result = (TOther)(object)checked((Int128)value);
                return true;
            }
            if (typeof(TOther) == typeof(nint))
            {
                result = (TOther)(object)checked((nint)value);
                return true;
            }
            if (typeof(TOther) == typeof(nuint))
            {
                result = (TOther)(object)checked((nuint)value);
                return true;
            }
            if (typeof(TOther) == typeof(sbyte))
            {
                result = (TOther)(object)checked((sbyte)value);
                return true;
            }
            if (typeof(TOther) == typeof(float))
            {
                result = (TOther)(object)checked((float)value);
                return true;
            }
            if (typeof(TOther) == typeof(Decimal32))
            {
                result = (TOther)(object)(Decimal32)value;
                return true;
            }
            if (typeof(TOther) == typeof(Decimal64))
            {
                result = (TOther)(object)(Decimal64)value;
                return true;
            }
            if (typeof(TOther) == typeof(Decimal128))
            {
                result = (TOther)(object)(Decimal128)value;
                return true;
            }
            if (typeof(TOther) == typeof(decimal))
            {
                result = (TOther)(object)value;
                return true;
            }

            result = default;
            return false;
        }

        private static bool TryConvertTo<TOther>(
            decimal value,
            [MaybeNullWhen(false)] out TOther result)
            where TOther : INumberBase<TOther>
        {
            if (typeof(TOther) == typeof(double))
            {
                result = (TOther)(object)(double)value;
                return true;
            }
            if (typeof(TOther) == typeof(byte))
            {
                var actual = value >= byte.MaxValue ? byte.MaxValue : value <= byte.MinValue ? byte.MinValue : (byte)value;
                result = (TOther)(object)actual;
                return true;
            }
            if (typeof(TOther) == typeof(char))
            {
                var actual = value >= char.MaxValue ? char.MaxValue : value <= char.MinValue ? char.MinValue : (char)value;
                result = (TOther)(object)actual;
                return true;
            }
            if (typeof(TOther) == typeof(Half))
            {
                result = (TOther)(object)(Half)value;
                return true;
            }
            if (typeof(TOther) == typeof(short))
            {
                var actual = value >= short.MaxValue ? short.MaxValue : value <= short.MinValue ? short.MinValue : (short)value;
                result = (TOther)(object)actual;
                return true;
            }
            if (typeof(TOther) == typeof(ushort))
            {
                var actual = value >= ushort.MaxValue ? ushort.MaxValue : value <= ushort.MinValue ? ushort.MinValue : (ushort)value;
                result = (TOther)(object)actual;
                return true;
            }
            if (typeof(TOther) == typeof(uint))
            {
                var actual = value >= uint.MaxValue ? uint.MaxValue : value <= uint.MinValue ? uint.MinValue : (uint)value;
                result = (TOther)(object)actual;
                return true;
            }
            if (typeof(TOther) == typeof(ulong))
            {
                var actual = value >= ulong.MaxValue ? ulong.MaxValue : value <= ulong.MinValue ? ulong.MinValue : (ulong)value;
                result = (TOther)(object)actual;
                return true;
            }
            if (typeof(TOther) == typeof(int))
            {
                var actual = value >= int.MaxValue ? int.MaxValue : value <= int.MinValue ? int.MinValue : (int)value;
                result = (TOther)(object)actual;
                return true;
            }
            if (typeof(TOther) == typeof(long))
            {
                var actual = value >= long.MaxValue ? long.MaxValue : value <= long.MinValue ? long.MinValue : (long)value;
                result = (TOther)(object)actual;
                return true;
            }
            if (typeof(TOther) == typeof(Int128))
            {
                result = (TOther)(object)(Int128)value;
                return true;
            }
            if (typeof(TOther) == typeof(nint))
            {
                var actual = value >= nint.MaxValue ? nint.MaxValue : value <= nint.MinValue ? nint.MinValue : (nint)value;
                result = (TOther)(object)actual;
                return true;
            }
            if (typeof(TOther) == typeof(nuint))
            {
                var actual = value >= (decimal)(ulong)nuint.MaxValue ? nuint.MaxValue : value <= 0 ? nuint.MinValue : (nuint)value;
                result = (TOther)(object)actual;
                return true;
            }
            if (typeof(TOther) == typeof(sbyte))
            {
                var actual = value >= sbyte.MaxValue ? sbyte.MaxValue : value <= sbyte.MinValue ? sbyte.MinValue : (sbyte)value;
                result = (TOther)(object)actual;
                return true;
            }
            if (typeof(TOther) == typeof(float))
            {
                result = (TOther)(object)(float)value;
                return true;
            }
            if (typeof(TOther) == typeof(decimal))
            {
                result = (TOther)(object)value;
                return true;
            }
            if (typeof(TOther) == typeof(UInt128))
            {
                result = (TOther)(object)(value <= 0 ? UInt128.MinValue : (UInt128)value);
                return true;
            }
            if (typeof(TOther) == typeof(Decimal32))
            {
                result = (TOther)(object)(Decimal32)value;
                return true;
            }
            if (typeof(TOther) == typeof(Decimal64))
            {
                result = (TOther)(object)(Decimal64)value;
                return true;
            }
            if (typeof(TOther) == typeof(Decimal128))
            {
                result = (TOther)(object)(Decimal128)value;
                return true;
            }

            result = default;
            return false;
        }

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

        private static string FormatCore(decimal value, string? format)
        {
            var builder = new ValueStringBuilder(32);
            AppendFormatCore(ref builder, value, format.AsSpan());
            return builder.ToString();
        }

        private static void AppendFormatCore(
            scoped ref ValueStringBuilder builder,
            decimal value,
            scoped ReadOnlySpan<char> format)
        {
            if (format.Length == 0 || format[0] is 'G' or 'g')
            {
                DecimalMath.AppendFormat(ref builder, value);
                return;
            }

            var symbol = Number.ParseInvariantFormatSpecifier(format, out var parsedPrecision);
            if (symbol == '\0')
            {
                var digits = DecimalMath.ToDigits(DecimalMath.Coefficient(value));
                Number.AppendCustomInvariant(
                    ref builder,
                    digits,
                    digits.Length - value.Scale,
                    value.IsNegativeValue,
                    Number.NumberBufferKind.Decimal,
                    format);
                return;
            }
            var hasPrecision = parsedPrecision >= 0;
            var precision = hasPrecision ? parsedPrecision : 0;
            switch (symbol)
            {
                case 'F' or 'f': AppendFixed(ref builder, value, hasPrecision ? precision : 2, group: false); break;
                case 'N' or 'n': AppendFixed(ref builder, value, hasPrecision ? precision : 2, group: true); break;
                case 'C' or 'c': AppendCurrency(ref builder, value, hasPrecision ? precision : 2); break;
                case 'P' or 'p': AppendPercent(ref builder, value, hasPrecision ? precision : 2); break;
                case 'E' or 'e': AppendScientific(ref builder, value, hasPrecision ? precision : 6, symbol == 'e'); break;
                default: throw new FormatException();
            }
        }

        private static decimal MultiplyByHundred(decimal value) => Multiply(value, new decimal(100));

        private static string FormatCurrency(decimal value, int precision)
        {
            var builder = new ValueStringBuilder(32);
            AppendCurrency(ref builder, value, precision);
            return builder.ToString();
        }

        private static void AppendCurrency(scoped ref ValueStringBuilder builder, decimal value, int precision)
        {
            if (value.IsNegativeValue)
            {
                builder.Append('-');
                builder.Append('$');
                AppendFixed(ref builder, Abs(value), precision, group: true);
            }
            else
            {
                builder.Append('$');
                AppendFixed(ref builder, value, precision, group: true);
            }
        }

        private static string FormatPercent(decimal value, int precision)
        {
            var builder = new ValueStringBuilder(32);
            AppendPercent(ref builder, value, precision);
            return builder.ToString();
        }

        private static void AppendPercent(scoped ref ValueStringBuilder builder, decimal value, int precision)
        {
            AppendFixed(ref builder, MultiplyByHundred(value), precision, group: true);
            builder.Append('%');
        }

        private static string FormatFixed(decimal value, int precision, bool group)
        {
            var builder = new ValueStringBuilder(32);
            AppendFixed(ref builder, value, precision, group);
            return builder.ToString();
        }

        private static void AppendFixed(scoped ref ValueStringBuilder builder, decimal value, int precision, bool group)
        {
            var rounded = DecimalMath.Round(value, precision, MidpointRounding.ToEven);
            var digits = DecimalMath.ToDigits(DecimalMath.Coefficient(rounded));
            var sourceIntegerDigits = digits.Length - rounded.Scale;
            var integerDigits = sourceIntegerDigits > 0 ? sourceIntegerDigits : 1;
            if (rounded.IsNegativeValue)
            {
                builder.Append('-');
            }
            for (var index = 0; index < integerDigits; index++)
            {
                if (group && index != 0 && (integerDigits - index) % 3 == 0)
                {
                    builder.Append(',');
                }
                builder.Append(sourceIntegerDigits > 0 && index < sourceIntegerDigits
                    ? digits[index]
                    : '0');
            }
            if (precision != 0)
            {
                builder.Append('.');
                for (var index = 0; index < precision; index++)
                {
                    var sourceIndex = sourceIntegerDigits + index;
                    builder.Append(sourceIndex >= 0 && sourceIndex < digits.Length
                        ? digits[sourceIndex]
                        : '0');
                }
            }
        }

        private static string FormatScientific(decimal value, int precision, bool lowerExponent)
        {
            var builder = new ValueStringBuilder(32);
            AppendScientific(ref builder, value, precision, lowerExponent);
            return builder.ToString();
        }

        private static void AppendScientific(
            scoped ref ValueStringBuilder builder,
            decimal value,
            int precision,
            bool lowerExponent)
        {
            var digits = DecimalMath.ToDigits(DecimalMath.Coefficient(value));
            var negative = value.IsNegativeValue;
            var exponent = value.IsZero ? 0 : digits.Length - value.Scale - 1;
            var significantCount = precision + 1;
            var significant = new char[significantCount];
            for (var index = 0; index < significantCount; index++)
            {
                significant[index] = index < digits.Length ? digits[index] : '0';
            }
            if (digits.Length > significantCount && digits[significantCount] >= '5')
            {
                var index = significantCount - 1;
                while (index >= 0 && significant[index] == '9')
                {
                    significant[index--] = '0';
                }
                if (index < 0)
                {
                    significant[0] = '1';
                    exponent++;
                }
                else
                {
                    significant[index]++;
                }
            }

            var exponentMagnitude = exponent < 0 ? -exponent : exponent;
            const int exponentDigits = 3;
            if (negative)
            {
                builder.Append('-');
            }
            builder.Append(significant[0]);
            if (precision != 0)
            {
                builder.Append('.');
                for (var index = 1; index < significant.Length; index++)
                {
                    builder.Append(significant[index]);
                }
            }
            builder.Append(lowerExponent ? 'e' : 'E');
            builder.Append(exponent < 0 ? '-' : '+');
            Span<char> exponentBuffer = stackalloc char[exponentDigits];
            for (var index = exponentDigits - 1; index >= 0; index--)
            {
                exponentBuffer[index] = (char)('0' + exponentMagnitude % 10);
                exponentMagnitude /= 10;
            }
            builder.Append(exponentBuffer);
        }

        private static string SpanToString(ReadOnlySpan<char> value)
        {
            var result = new char[value.Length];
            for (var index = 0; index < value.Length; index++)
            {
                result[index] = value[index];
            }
            return string.Create(result);
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
