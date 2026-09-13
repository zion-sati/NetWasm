// Primitive contracts and algorithms in this file are adapted from the
// corresponding dotnet/runtime System.Private.CoreLib primitive sources.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
//
// NetWasm retains the supported observable contracts while omitting generic
// math, globalization/provider, UTF-8/span formatting and runtime-only hooks.
using System.Runtime.CompilerServices;

namespace System
{
    public abstract class ValueType
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public override extern bool Equals(object? value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        public override extern int GetHashCode();
    }

    public abstract class Enum : ValueType, IComparable, IConvertible, IFormattable, ISpanFormattable
    {
        // Enum storage is not an object graph in NetWasm. These narrow calls
        // are the managed/runtime boundary for the operations that need the
        // enum descriptor (underlying type, constants and names). The public
        // methods retain the normal BCL validation and overload surface while
        // the compiler supplies the storage and metadata operation.
        public override bool Equals(object? value) => InternalEquals(this, value);
        public override int GetHashCode() => InternalGetHashCode(this);
        public int CompareTo(object? value) => InternalCompareTo(this, value);

        public TypeCode GetTypeCode() => InternalGetTypeCode(this);

        public bool HasFlag(Enum flag)
        {
            if (flag == null)
            {
                throw new ArgumentNullException();
            }
            return InternalHasFlag(this, flag);
        }

        public override string ToString() => InternalToString(this, null, null);

        public string ToString(string? format) => InternalToString(this, format, null);

        public string ToString(IFormatProvider? provider) => InternalToString(this, null, provider);

        public string ToString(string? format, IFormatProvider? provider) =>
            InternalToString(this, format, provider);

        bool ISpanFormattable.TryFormat(
            Span<char> destination,
            out int charsWritten,
            ReadOnlySpan<char> format,
            IFormatProvider? provider)
        {
            var text = format.Length switch
            {
                0 => InternalToString(this, null, null),
                1 when format[0] is 'D' or 'd' => InternalToString(this, "D", null),
                1 when format[0] is 'F' or 'f' => InternalToString(this, "F", null),
                1 when format[0] is 'G' or 'g' => InternalToString(this, "G", null),
                1 when format[0] is 'X' or 'x' => InternalToString(this, "X", null),
                _ => throw new FormatException(),
            };
            if (text.Length > destination.Length)
            {
                charsWritten = 0;
                return false;
            }

            for (var index = 0; index < text.Length; index++)
            {
                destination[index] = text[index];
            }
            charsWritten = text.Length;
            return true;
        }

        public static string Format(Type enumType, object value, string format) =>
            InternalFormat(enumType, value, format);

        public static string? GetName(Type enumType, object value) =>
            InternalGetName(enumType, value);

        public static string? GetName<TEnum>(TEnum value) where TEnum : struct, Enum =>
            InternalGetName(value);

        public static string[] GetNames(Type enumType) => InternalGetNames(enumType);

        public static string[] GetNames<TEnum>() where TEnum : struct, Enum =>
            InternalGetNames<TEnum>();

        public static Type GetUnderlyingType(Type enumType) => InternalGetUnderlyingType(enumType);

        public static Array GetValues(Type enumType) => InternalGetValues(enumType, false);

        public static Array GetValuesAsUnderlyingType(Type enumType) =>
            InternalGetValues(enumType, true);

        public static Array GetValuesAsUnderlyingType<TEnum>() where TEnum : struct, Enum =>
            InternalGetValuesAsUnderlyingType<TEnum>();

        public static TEnum[] GetValues<TEnum>() where TEnum : struct, Enum =>
            InternalGetValues<TEnum>();

        public static bool IsDefined(Type enumType, object value) =>
            InternalIsDefined(enumType, value);

        public static bool IsDefined<TEnum>(TEnum value) where TEnum : struct, Enum =>
            InternalIsDefined(value);

        public static object Parse(Type enumType, ReadOnlySpan<char> value) =>
            InternalParse(enumType, value.ToArray(), false);

        public static object Parse(Type enumType, ReadOnlySpan<char> value, bool ignoreCase) =>
            InternalParse(enumType, value.ToArray(), ignoreCase);

        public static object Parse(Type enumType, string value) =>
            InternalParse(enumType, value, false);

        public static object Parse(Type enumType, string value, bool ignoreCase) =>
            InternalParse(enumType, value, ignoreCase);

        public static TEnum Parse<TEnum>(ReadOnlySpan<char> value) where TEnum : struct =>
            InternalParse<TEnum>(value.ToArray(), false);

        public static TEnum Parse<TEnum>(ReadOnlySpan<char> value, bool ignoreCase)
            where TEnum : struct => InternalParse<TEnum>(value.ToArray(), ignoreCase);

        public static TEnum Parse<TEnum>(string value) where TEnum : struct =>
            InternalParse<TEnum>(value, false);

        public static TEnum Parse<TEnum>(string value, bool ignoreCase) where TEnum : struct =>
            InternalParse<TEnum>(value, ignoreCase);

        public static object ToObject(Type enumType, byte value) =>
            InternalToObject(enumType, value);

        public static object ToObject(Type enumType, short value) =>
            InternalToObject(enumType, value);

        public static object ToObject(Type enumType, int value) =>
            InternalToObject(enumType, value);

        public static object ToObject(Type enumType, long value) =>
            InternalToObject(enumType, value);

        public static object ToObject(Type enumType, object value) =>
            InternalToObject(enumType, value);

        public static object ToObject(Type enumType, sbyte value) =>
            InternalToObject(enumType, value);

        public static object ToObject(Type enumType, ushort value) =>
            InternalToObject(enumType, value);

        public static object ToObject(Type enumType, uint value) =>
            InternalToObject(enumType, value);

        public static object ToObject(Type enumType, ulong value) =>
            InternalToObject(enumType, value);

        public static bool TryFormat<TEnum>(
            TEnum value,
            Span<char> destination,
            out int charsWritten,
            ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>)) where TEnum : struct =>
            InternalTryFormat(value, destination, out charsWritten, format);

        public static bool TryParse(
            Type enumType,
            ReadOnlySpan<char> value,
            bool ignoreCase,
            out object? result) =>
            InternalTryParse(enumType, value.ToArray(), ignoreCase, out result);

        public static bool TryParse(
            Type enumType,
            ReadOnlySpan<char> value,
            out object? result) =>
            InternalTryParse(enumType, value.ToArray(), false, out result);

        public static bool TryParse(
            Type enumType,
            string? value,
            bool ignoreCase,
            out object? result) =>
            InternalTryParse(enumType, value, ignoreCase, out result);

        public static bool TryParse(Type enumType, string? value, out object? result) =>
            InternalTryParse(enumType, value, false, out result);

        public static bool TryParse<TEnum>(
            ReadOnlySpan<char> value,
            bool ignoreCase,
            out TEnum result) where TEnum : struct =>
            InternalTryParse(value.ToArray(), ignoreCase, out result);

        public static bool TryParse<TEnum>(
            ReadOnlySpan<char> value,
            out TEnum result) where TEnum : struct =>
            InternalTryParse(value.ToArray(), false, out result);

        public static bool TryParse<TEnum>(
            string? value,
            bool ignoreCase,
            out TEnum result) where TEnum : struct =>
            InternalTryParse(value, ignoreCase, out result);

        public static bool TryParse<TEnum>(string? value, out TEnum result) where TEnum : struct =>
            InternalTryParse(value, false, out result);

        bool IConvertible.ToBoolean(IFormatProvider? provider) => InternalToBoolean(this);

        char IConvertible.ToChar(IFormatProvider? provider) => InternalToChar(this);

        sbyte IConvertible.ToSByte(IFormatProvider? provider) => InternalToSByte(this);

        byte IConvertible.ToByte(IFormatProvider? provider) => InternalToByte(this);

        short IConvertible.ToInt16(IFormatProvider? provider) => InternalToInt16(this);

        ushort IConvertible.ToUInt16(IFormatProvider? provider) => InternalToUInt16(this);

        int IConvertible.ToInt32(IFormatProvider? provider) => InternalToInt32(this);

        uint IConvertible.ToUInt32(IFormatProvider? provider) => InternalToUInt32(this);

        long IConvertible.ToInt64(IFormatProvider? provider) => InternalToInt64(this);

        ulong IConvertible.ToUInt64(IFormatProvider? provider) => InternalToUInt64(this);

        float IConvertible.ToSingle(IFormatProvider? provider) => InternalToSingle(this);

        double IConvertible.ToDouble(IFormatProvider? provider) => InternalToDouble(this);

        decimal IConvertible.ToDecimal(IFormatProvider? provider) => InternalToDecimal(this);

        DateTime IConvertible.ToDateTime(IFormatProvider? provider) => InternalToDateTime(this);

        object IConvertible.ToType(Type conversionType, IFormatProvider? provider) =>
            InternalToType(this, conversionType);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool InternalEquals(Enum value, object? other);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern int InternalGetHashCode(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern int InternalCompareTo(Enum value, object? other);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern TypeCode InternalGetTypeCode(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool InternalHasFlag(Enum value, Enum flag);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string InternalToString(
            Enum value, string? format, IFormatProvider? provider);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string InternalFormat(Type enumType, object value, string format);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string? InternalGetName(Type enumType, object value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string? InternalGetName<TEnum>(TEnum value)
            where TEnum : struct, Enum;

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string[] InternalGetNames(Type enumType);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string[] InternalGetNames<TEnum>() where TEnum : struct, Enum;

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern Type InternalGetUnderlyingType(Type enumType);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern Array InternalGetValues(Type enumType, bool underlyingType);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern Array InternalGetValuesAsUnderlyingType<TEnum>()
            where TEnum : struct, Enum;

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern TEnum[] InternalGetValues<TEnum>() where TEnum : struct, Enum;

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool InternalIsDefined(Type enumType, object value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool InternalIsDefined<TEnum>(TEnum value)
            where TEnum : struct, Enum;

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern object InternalParse(
            Type enumType, char[] value, bool ignoreCase);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern TEnum InternalParse<TEnum>(char[] value, bool ignoreCase)
            where TEnum : struct;

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern object InternalParse(Type enumType, string value, bool ignoreCase);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern TEnum InternalParse<TEnum>(string value, bool ignoreCase)
            where TEnum : struct;

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern object InternalToObject(Type enumType, object value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool InternalTryFormat<TEnum>(
            TEnum value,
            Span<char> destination,
            out int charsWritten,
            ReadOnlySpan<char> format) where TEnum : struct;

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool InternalTryParse(
            Type enumType, char[]? value, bool ignoreCase, out object? result);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool InternalTryParse<TEnum>(
            char[] value, bool ignoreCase, out TEnum result) where TEnum : struct;

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool InternalTryParse(
            Type enumType, string? value, bool ignoreCase, out object? result);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool InternalTryParse<TEnum>(
            string? value, bool ignoreCase, out TEnum result) where TEnum : struct;

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern bool InternalToBoolean(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern char InternalToChar(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern sbyte InternalToSByte(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern byte InternalToByte(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern short InternalToInt16(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern ushort InternalToUInt16(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern int InternalToInt32(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern uint InternalToUInt32(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern long InternalToInt64(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern ulong InternalToUInt64(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern float InternalToSingle(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern double InternalToDouble(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern decimal InternalToDecimal(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern DateTime InternalToDateTime(Enum value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern object InternalToType(Enum value, Type conversionType);
    }

    public struct Void
    {
    }

    public partial struct Boolean : IComparable, IComparable<bool>, IConvertible, IEquatable<bool>,
        IParsable<bool>, ISpanParsable<bool>
    {
        public static readonly string TrueString = "True";
        public static readonly string FalseString = "False";

        public bool Equals(bool other) => this == other;
        public int CompareTo(bool other) => this == other ? 0 : this ? 1 : -1;
        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (value is bool other) return CompareTo(other);
            throw new ArgumentException();
        }
        public override bool Equals(object? value) => value is bool other && Equals(other);
        public override int GetHashCode() => this ? 1 : 0;
        public override string ToString() => this ? TrueString : FalseString;
        public string ToString(IFormatProvider? provider) => ToString();

        public bool TryFormat(Span<char> destination, out int charsWritten)
        {
            var value = this ? TrueString : FalseString;
            if (destination.Length < value.Length)
            {
                charsWritten = 0;
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                destination[index] = value[index];
            }

            charsWritten = value.Length;
            return true;
        }

        public TypeCode GetTypeCode() => TypeCode.Boolean;
        public static bool Parse(string value)
        {
            if (value == null) throw new ArgumentNullException();
            if (TryParse(value, out var result)) return result;
            throw new FormatException();
        }
        public static bool TryParse(string? value, out bool result)
        {
            if (EqualsIgnoreCase(value, TrueString)) { result = true; return true; }
            if (EqualsIgnoreCase(value, FalseString)) { result = false; return true; }
            result = false;
            return false;
        }

        public static bool Parse(ReadOnlySpan<char> value)
        {
            if (TryParse(value, out var result))
            {
                return result;
            }

            throw new FormatException();
        }

        public static bool TryParse(ReadOnlySpan<char> value, out bool result)
        {
            var start = 0;
            var end = value.Length;
            while (start < end && IsTrimCharacter(value[start]))
            {
                start++;
            }

            while (end > start && IsTrimCharacter(value[end - 1]))
            {
                end--;
            }

            var length = end - start;
            if (length is 4 or 5)
            {
                var expected = length == 4 ? TrueString : FalseString;
                var matches = true;
                for (var index = 0; index < length; index++)
                {
                    var actual = value[start + index];
                    var canonical = expected[index];
                    if (actual >= 'A' && actual <= 'Z')
                    {
                        actual = (char)(actual + ('a' - 'A'));
                    }

                    if (canonical >= 'A' && canonical <= 'Z')
                    {
                        canonical = (char)(canonical + ('a' - 'A'));
                    }

                    if (actual != canonical)
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    result = length == 4;
                    return true;
                }
            }

            result = false;
            return false;
        }

        static bool IParsable<bool>.TryParse(string? value, IFormatProvider? provider, out bool result) =>
            TryParse(value, out result);

        static bool IParsable<bool>.Parse(string value, IFormatProvider? provider) => Parse(value);

        static bool ISpanParsable<bool>.TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out bool result) =>
            TryParse(value, out result);

        static bool ISpanParsable<bool>.Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => Parse(value);

        bool IConvertible.ToBoolean(IFormatProvider? provider) => this;
        char IConvertible.ToChar(IFormatProvider? provider) => throw new InvalidCastException();
        sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte(this);
        byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte(this);
        short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16(this);
        ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16(this);
        int IConvertible.ToInt32(IFormatProvider? provider) => Convert.ToInt32(this);
        uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32(this);
        long IConvertible.ToInt64(IFormatProvider? provider) => Convert.ToInt64(this);
        ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64(this);
        float IConvertible.ToSingle(IFormatProvider? provider) => Convert.ToSingle(this);
        double IConvertible.ToDouble(IFormatProvider? provider) => Convert.ToDouble(this);
        decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal(this);
        DateTime IConvertible.ToDateTime(IFormatProvider? provider) => throw new InvalidCastException();
        string IConvertible.ToString(IFormatProvider? provider) => ToString(provider);
        object IConvertible.ToType(Type conversionType, IFormatProvider? provider) =>
            conversionType == typeof(bool) ? this :
            conversionType == typeof(string) ? ToString() :
            conversionType == typeof(sbyte) ? Convert.ToSByte(this) :
            conversionType == typeof(byte) ? Convert.ToByte(this) :
            conversionType == typeof(short) ? Convert.ToInt16(this) :
            conversionType == typeof(ushort) ? Convert.ToUInt16(this) :
            conversionType == typeof(int) ? Convert.ToInt32(this) :
            conversionType == typeof(uint) ? Convert.ToUInt32(this) :
            conversionType == typeof(long) ? Convert.ToInt64(this) :
            conversionType == typeof(ulong) ? Convert.ToUInt64(this) :
            conversionType == typeof(float) ? Convert.ToSingle(this) :
            conversionType == typeof(double) ? Convert.ToDouble(this) :
            conversionType == typeof(decimal) ? Convert.ToDecimal(this) :
            throw new InvalidCastException();

        private static bool EqualsIgnoreCase(string? value, string expected)
        {
            if (value == null) return false;
            var start = 0;
            var end = value.Length;
            while (start < end && IsTrimCharacter(value[start])) start++;
            while (end > start && IsTrimCharacter(value[end - 1])) end--;
            if (end - start != expected.Length) return false;
            for (var index = 0; index < expected.Length; index++)
            {
                var actual = value[start + index];
                var canonical = expected[index];
                if (actual == canonical) continue;
                if (actual >= 'A' && actual <= 'Z') actual = (char)(actual + ('a' - 'A'));
                if (canonical >= 'A' && canonical <= 'Z') canonical = (char)(canonical + ('a' - 'A'));
                if (actual != canonical) return false;
            }
            return true;
        }

        private static bool IsTrimCharacter(char value) =>
            value is ' ' or '\t' or '\r' or '\n' or '\f' or '\v' or '\0';
    }

    public partial struct Byte : IComparable, IComparable<byte>, IConvertible, IEquatable<byte>,
        IFormattable, IParsable<byte>, ISpanFormattable, ISpanParsable<byte>,
        IUtf8SpanFormattable, IUtf8SpanParsable<byte>,
        Numerics.IAdditionOperators<byte, byte, byte>, Numerics.IAdditiveIdentity<byte, byte>,
        Numerics.IBinaryInteger<byte>, Numerics.IBinaryNumber<byte>, Numerics.IBitwiseOperators<byte, byte, byte>,
        Numerics.IComparisonOperators<byte, byte, bool>, Numerics.IDecrementOperators<byte>,
        Numerics.IDivisionOperators<byte, byte, byte>, Numerics.IEqualityOperators<byte, byte, bool>,
        Numerics.IIncrementOperators<byte>, Numerics.IMinMaxValue<byte>, Numerics.IModulusOperators<byte, byte, byte>,
        Numerics.IMultiplicativeIdentity<byte, byte>, Numerics.IMultiplyOperators<byte, byte, byte>,
        Numerics.INumber<byte>, Numerics.INumberBase<byte>, Numerics.IShiftOperators<byte, int, byte>,
        Numerics.ISubtractionOperators<byte, byte, byte>, Numerics.IUnaryNegationOperators<byte, byte>,
        Numerics.IUnaryPlusOperators<byte, byte>, Numerics.IUnsignedNumber<byte>
    {
        public const byte MinValue = (byte)0;
        public const byte MaxValue = (byte)255;
        public bool Equals(byte other) => this == other;
        public int CompareTo(byte other) => Number.Compare((ulong)this, other);
        public int CompareTo(object? value) => CompareObject(value);
        public override bool Equals(object? value) => value is byte other && Equals(other);
        public override int GetHashCode() => this;
        public override string ToString() => Number.FormatUnsigned(this, 8, null);
        public string ToString(string? format) => Number.FormatUnsigned(this, 8, format);
        public static byte Parse(string value) => ParseUnsigned(value, false);
        public static byte Parse(string value, Globalization.NumberStyles style) =>
            ParseByteWithStyle(value, style);
        private static byte ParseByteWithStyle(string value, Globalization.NumberStyles style)
        {
            PrimitiveGenericMathSmall.ValidateIntegerStyle(style);
            if (value is null) throw new ArgumentNullException();
            return PrimitiveGenericMathSmall.ParseByte(new ReadOnlySpan<char>(value.ToCharArray()), style);
        }
        public static bool TryParse(string? value, out byte result)
        {
            if (value is null)
            {
                result = 0;
                return false;
            }

            var valid = PrimitiveGenericMathSmall.TryParseUnsigned(new ReadOnlySpan<char>(value.ToCharArray()), Globalization.NumberStyles.Integer, MaxValue, out var parsed);
            result = (byte)parsed;
            return valid;
        }
        public static bool TryParse(string? value, Globalization.NumberStyles style, out byte result)
        {
            if (value is null)
            {
                PrimitiveGenericMathSmall.ValidateIntegerStyle(style);
                result = 0;
                return false;
            }

            var valid = PrimitiveGenericMathSmall.TryParseUnsigned(new ReadOnlySpan<char>(value.ToCharArray()), style, MaxValue, out var parsed);
            result = (byte)parsed;
            return valid;
        }
        private static byte ParseUnsigned(string value, bool hexadecimal)
        {
            if (value == null) throw new ArgumentNullException();
            return PrimitiveGenericMathSmall.ParseByte(new ReadOnlySpan<char>(value.ToCharArray()), hexadecimal ? Globalization.NumberStyles.HexNumber : Globalization.NumberStyles.Integer);
        }
        private int CompareObject(object? value)
        {
            if (value == null) return 1;
            if (value is byte other) return CompareTo(other);
            throw new ArgumentException();
        }

        public static byte One => 1;
        public static byte Zero => 0;
        public static int Radix => 2;
        public static byte Abs(byte value) => value;
        public static byte Clamp(byte value, byte minimum, byte maximum) =>
            minimum > maximum ? throw new ArgumentException() : value < minimum ? minimum : value > maximum ? maximum : value;
        public static byte Max(byte left, byte right) => left >= right ? left : right;
        public static byte Min(byte left, byte right) => left <= right ? left : right;
        public static int Sign(byte value) => value == 0 ? 0 : 1;
        public static bool IsCanonical(byte value) => true;
        public static bool IsComplexNumber(byte value) => false;
        public static bool IsEvenInteger(byte value) => (value & 1) == 0;
        public static bool IsFinite(byte value) => true;
        public static bool IsImaginaryNumber(byte value) => false;
        public static bool IsInfinity(byte value) => false;
        public static bool IsInteger(byte value) => true;
        public static bool IsNaN(byte value) => false;
        public static bool IsNegative(byte value) => false;
        public static bool IsNegativeInfinity(byte value) => false;
        public static bool IsNormal(byte value) => value != 0;
        public static bool IsOddInteger(byte value) => (value & 1) != 0;
        public static bool IsPositive(byte value) => true;
        public static bool IsPositiveInfinity(byte value) => false;
        public static bool IsRealNumber(byte value) => true;
        public static bool IsSubnormal(byte value) => false;
        public static bool IsZero(byte value) => value == 0;
        public static byte MaxMagnitude(byte left, byte right) => Max(left, right);
        public static byte MaxMagnitudeNumber(byte left, byte right) => Max(left, right);
        public static byte MinMagnitude(byte left, byte right) => Min(left, right);
        public static byte MinMagnitudeNumber(byte left, byte right) => Min(left, right);
        public static byte Log2(byte value)
        {
            var result = 0;
            while (value > 1) { value >>= 1; result++; }
            return (byte)result;
        }
        public static bool IsPow2(byte value) => value != 0 && (value & (value - 1)) == 0;
        public static byte LeadingZeroCount(byte value)
        {
            var result = 0;
            for (var bit = 7; bit >= 0 && (value & (1 << bit)) == 0; bit--) result++;
            return (byte)result;
        }
        public static byte PopCount(byte value)
        {
            var result = 0;
            while (value != 0) { value &= (byte)(value - 1); result++; }
            return (byte)result;
        }
        public static byte RotateLeft(byte value, int rotateAmount)
        {
            rotateAmount &= 7;
            return (byte)((value << rotateAmount) | (value >> ((8 - rotateAmount) & 7)));
        }
        public static byte RotateRight(byte value, int rotateAmount) => RotateLeft(value, -rotateAmount);
        public static byte TrailingZeroCount(byte value)
        {
            if (value == 0) return 8;
            var result = 0;
            while ((value & 1) == 0) { value >>= 1; result++; }
            return (byte)result;
        }
        public static (byte Quotient, byte Remainder) DivRem(byte left, byte right)
        {
            var quotient = (byte)(left / right);
            return (quotient, (byte)(left - quotient * right));
        }
        public int GetByteCount() => 1;
        public int GetShortestBitLength() => this == 0 ? 0 : 8 - LeadingZeroCount(this);
        public bool TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < 1) { bytesWritten = 0; return false; }
            destination[0] = this;
            bytesWritten = 1;
            return true;
        }
        public bool TryWriteLittleEndian(Span<byte> destination, out int bytesWritten) => TryWriteBigEndian(destination, out bytesWritten);
        public static bool TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out byte value)
        {
            if (source.IsEmpty) { value = 0; return true; }
            if (!isUnsigned && (source[0] & 0x80) != 0) { value = 0; return false; }
            for (var index = 0; index < source.Length - 1; index++)
            {
                if (source[index] != 0) { value = 0; return false; }
            }

            value = source[source.Length - 1];
            return true;
        }
        public static bool TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out byte value)
        {
            if (source.IsEmpty) { value = 0; return true; }
            if (!isUnsigned && (source[source.Length - 1] & 0x80) != 0) { value = 0; return false; }
            for (var index = 1; index < source.Length; index++)
            {
                if (source[index] != 0) { value = 0; return false; }
            }

            value = source[0];
            return true;
        }
        public static byte Parse(ReadOnlySpan<char> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => PrimitiveGenericMathSmall.ParseByte(value, style);
        public static bool TryParse(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out byte result)
        {
            var valid = PrimitiveGenericMathSmall.TryParseUnsigned(value, style, MaxValue, out var parsed);
            result = (byte)parsed;
            return valid;
        }
        public static byte Parse(ReadOnlySpan<byte> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => PrimitiveGenericMathSmall.ParseByteUtf8(value, style);
        public static bool TryParse(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out byte result)
        {
            var valid = PrimitiveGenericMathSmall.TryParseUtf8(value, style, out var parsed, MaxValue);
            result = (byte)parsed;
            return valid;
        }
        static byte Numerics.INumberBase<byte>.One => One;
        static byte Numerics.INumberBase<byte>.Zero => Zero;
        static int Numerics.INumberBase<byte>.Radix => Radix;
        static byte Numerics.INumberBase<byte>.Abs(byte value) => Abs(value);
        static byte Numerics.INumberBase<byte>.MaxMagnitude(byte left, byte right) => MaxMagnitude(left, right);
        static byte Numerics.INumberBase<byte>.MaxMagnitudeNumber(byte left, byte right) => MaxMagnitudeNumber(left, right);
        static byte Numerics.INumberBase<byte>.MinMagnitude(byte left, byte right) => MinMagnitude(left, right);
        static byte Numerics.INumberBase<byte>.MinMagnitudeNumber(byte left, byte right) => MinMagnitudeNumber(left, right);
        static bool Numerics.INumberBase<byte>.IsCanonical(byte value) => IsCanonical(value);
        static bool Numerics.INumberBase<byte>.IsComplexNumber(byte value) => IsComplexNumber(value);
        static bool Numerics.INumberBase<byte>.IsFinite(byte value) => IsFinite(value);
        static bool Numerics.INumberBase<byte>.IsImaginaryNumber(byte value) => IsImaginaryNumber(value);
        static bool Numerics.INumberBase<byte>.IsInfinity(byte value) => IsInfinity(value);
        static bool Numerics.INumberBase<byte>.IsInteger(byte value) => IsInteger(value);
        static bool Numerics.INumberBase<byte>.IsNaN(byte value) => IsNaN(value);
        static bool Numerics.INumberBase<byte>.IsNegative(byte value) => IsNegative(value);
        static bool Numerics.INumberBase<byte>.IsNegativeInfinity(byte value) => IsNegativeInfinity(value);
        static bool Numerics.INumberBase<byte>.IsNormal(byte value) => IsNormal(value);
        static bool Numerics.INumberBase<byte>.IsPositive(byte value) => IsPositive(value);
        static bool Numerics.INumberBase<byte>.IsPositiveInfinity(byte value) => IsPositiveInfinity(value);
        static bool Numerics.INumberBase<byte>.IsRealNumber(byte value) => IsRealNumber(value);
        static bool Numerics.INumberBase<byte>.IsSubnormal(byte value) => IsSubnormal(value);
        static bool Numerics.INumberBase<byte>.IsZero(byte value) => IsZero(value);
        static byte Numerics.IBinaryNumber<byte>.Log2(byte value) => Log2(value);
        static bool Numerics.IBinaryNumber<byte>.IsPow2(byte value) => IsPow2(value);
        static byte Numerics.INumber<byte>.CopySign(byte value, byte sign) => value;
        static byte Numerics.INumber<byte>.MaxNumber(byte left, byte right) => Max(left, right);
        static byte Numerics.INumber<byte>.MinNumber(byte left, byte right) => Min(left, right);
        static byte Numerics.INumberBase<byte>.MultiplyAddEstimate(byte left, byte right, byte addend) => (byte)(left * right + addend);
        static byte Numerics.IAdditiveIdentity<byte, byte>.AdditiveIdentity => Zero;
        static byte Numerics.IMultiplicativeIdentity<byte, byte>.MultiplicativeIdentity => One;
        static byte Numerics.IMinMaxValue<byte>.MinValue => MinValue;
        static byte Numerics.IMinMaxValue<byte>.MaxValue => MaxValue;

        private static bool TryConvert<TOther>(TOther value, out byte result)
            where TOther : Numerics.INumberBase<TOther>
        {
            try { result = Convert.ToByte((object?)value); return true; }
            catch { result = 0; return false; }
        }
        static bool Numerics.INumberBase<byte>.TryConvertFromChecked<TOther>(TOther value, out byte result) => TryConvert(value, out result);
        static bool Numerics.INumberBase<byte>.TryConvertFromSaturating<TOther>(TOther value, out byte result) => TryConvert(value, out result);
        static bool Numerics.INumberBase<byte>.TryConvertFromTruncating<TOther>(TOther value, out byte result) => TryConvert(value, out result);
        static bool Numerics.INumberBase<byte>.TryConvertToChecked<TOther>(byte value, out TOther result) => TryConvertTo(value, out result);
        static bool Numerics.INumberBase<byte>.TryConvertToSaturating<TOther>(byte value, out TOther result) => TryConvertTo(value, out result);
        static bool Numerics.INumberBase<byte>.TryConvertToTruncating<TOther>(byte value, out TOther result) => TryConvertTo(value, out result);
        private static bool TryConvertTo<TOther>(byte value, out TOther result)
            where TOther : Numerics.INumberBase<TOther>
        {
            try { result = TOther.CreateTruncating(value); return true; }
            catch { result = default!; return false; }
        }
    }

    public partial struct SByte : IComparable, IComparable<sbyte>, IConvertible, IEquatable<sbyte>,
        IFormattable, IParsable<sbyte>, ISpanFormattable, ISpanParsable<sbyte>,
        IUtf8SpanFormattable, IUtf8SpanParsable<sbyte>,
        Numerics.IAdditionOperators<sbyte, sbyte, sbyte>, Numerics.IAdditiveIdentity<sbyte, sbyte>,
        Numerics.IBinaryInteger<sbyte>, Numerics.IBinaryNumber<sbyte>, Numerics.IBitwiseOperators<sbyte, sbyte, sbyte>,
        Numerics.IComparisonOperators<sbyte, sbyte, bool>, Numerics.IDecrementOperators<sbyte>,
        Numerics.IDivisionOperators<sbyte, sbyte, sbyte>, Numerics.IEqualityOperators<sbyte, sbyte, bool>,
        Numerics.IIncrementOperators<sbyte>, Numerics.IMinMaxValue<sbyte>, Numerics.IModulusOperators<sbyte, sbyte, sbyte>,
        Numerics.IMultiplicativeIdentity<sbyte, sbyte>, Numerics.IMultiplyOperators<sbyte, sbyte, sbyte>,
        Numerics.INumber<sbyte>, Numerics.INumberBase<sbyte>, Numerics.IShiftOperators<sbyte, int, sbyte>,
        Numerics.ISignedNumber<sbyte>, Numerics.ISubtractionOperators<sbyte, sbyte, sbyte>,
        Numerics.IUnaryNegationOperators<sbyte, sbyte>, Numerics.IUnaryPlusOperators<sbyte, sbyte>
    {
        public const sbyte MinValue = (sbyte)-128;
        public const sbyte MaxValue = (sbyte)127;
        public bool Equals(sbyte other) => this == other;
        public int CompareTo(sbyte other) => Number.Compare((long)this, other);
        public int CompareTo(object? value) => CompareObject(value);
        public override bool Equals(object? value) => value is sbyte other && Equals(other);
        public override int GetHashCode() => this;
        public override string ToString() => Number.FormatSigned(this, 8, null);
        public string ToString(string? format) => Number.FormatSigned(this, 8, format);
        public static sbyte Parse(string value) => ParseSigned(value, false);
        public static sbyte Parse(string value, Globalization.NumberStyles style) =>
            ParseSByteWithStyle(value, style);
        private static sbyte ParseSByteWithStyle(string value, Globalization.NumberStyles style)
        {
            PrimitiveGenericMathSmall.ValidateIntegerStyle(style);
            if (value is null) throw new ArgumentNullException();
            return PrimitiveGenericMathSmall.ParseSByte(new ReadOnlySpan<char>(value.ToCharArray()), style);
        }
        public static bool TryParse(string? value, out sbyte result)
        {
            if (value is null)
            {
                result = 0;
                return false;
            }

            var valid = PrimitiveGenericMathSmall.TryParseSigned(new ReadOnlySpan<char>(value.ToCharArray()), Globalization.NumberStyles.Integer, 8, out var parsed);
            result = (sbyte)parsed;
            return valid;
        }
        public static bool TryParse(string? value, Globalization.NumberStyles style, out sbyte result)
        {
            if (value is null)
            {
                PrimitiveGenericMathSmall.ValidateIntegerStyle(style);
                result = 0;
                return false;
            }

            var valid = PrimitiveGenericMathSmall.TryParseSigned(new ReadOnlySpan<char>(value.ToCharArray()), style, 8, out var parsed);
            result = (sbyte)parsed;
            return valid;
        }
        private static sbyte ParseSigned(string value, bool hexadecimal)
        {
            if (value == null) throw new ArgumentNullException();
            return PrimitiveGenericMathSmall.ParseSByte(new ReadOnlySpan<char>(value.ToCharArray()), hexadecimal ? Globalization.NumberStyles.HexNumber : Globalization.NumberStyles.Integer);
        }
        private int CompareObject(object? value)
        {
            if (value == null) return 1;
            if (value is sbyte other) return CompareTo(other);
            throw new ArgumentException();
        }

        public static sbyte One => 1;
        public static sbyte Zero => 0;
        public static sbyte NegativeOne => -1;
        public static int Radix => 2;
        public static sbyte Abs(sbyte value) => value == MinValue ? throw new OverflowException() : value < 0 ? (sbyte)-value : value;
        public static sbyte Clamp(sbyte value, sbyte minimum, sbyte maximum) =>
            minimum > maximum ? throw new ArgumentException() : value < minimum ? minimum : value > maximum ? maximum : value;
        public static sbyte CopySign(sbyte value, sbyte sign)
        {
            if (value == MinValue)
            {
                return sign < 0 ? MinValue : throw new OverflowException();
            }

            var magnitude = value < 0 ? (sbyte)-value : value;
            return sign < 0 ? (sbyte)-magnitude : magnitude;
        }
        public static sbyte Max(sbyte left, sbyte right) => left >= right ? left : right;
        public static sbyte Min(sbyte left, sbyte right) => left <= right ? left : right;
        public static int Sign(sbyte value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        public static bool IsCanonical(sbyte value) => true;
        public static bool IsComplexNumber(sbyte value) => false;
        public static bool IsEvenInteger(sbyte value) => (value & 1) == 0;
        public static bool IsFinite(sbyte value) => true;
        public static bool IsImaginaryNumber(sbyte value) => false;
        public static bool IsInfinity(sbyte value) => false;
        public static bool IsInteger(sbyte value) => true;
        public static bool IsNaN(sbyte value) => false;
        public static bool IsNegative(sbyte value) => value < 0;
        public static bool IsNegativeInfinity(sbyte value) => false;
        public static bool IsNormal(sbyte value) => value != 0;
        public static bool IsOddInteger(sbyte value) => (value & 1) != 0;
        public static bool IsPositive(sbyte value) => value >= 0;
        public static bool IsPositiveInfinity(sbyte value) => false;
        public static bool IsRealNumber(sbyte value) => true;
        public static bool IsSubnormal(sbyte value) => false;
        public static bool IsZero(sbyte value) => value == 0;
        public static sbyte MaxMagnitude(sbyte left, sbyte right)
        {
            var leftMagnitude = Magnitude(left);
            var rightMagnitude = Magnitude(right);
            return leftMagnitude > rightMagnitude ? left : leftMagnitude < rightMagnitude ? right : IsNegative(left) ? right : left;
        }
        public static sbyte MaxMagnitudeNumber(sbyte left, sbyte right) => MaxMagnitude(left, right);
        public static sbyte MinMagnitude(sbyte left, sbyte right)
        {
            var leftMagnitude = Magnitude(left);
            var rightMagnitude = Magnitude(right);
            return leftMagnitude < rightMagnitude ? left : leftMagnitude > rightMagnitude ? right : IsNegative(left) ? left : right;
        }
        public static sbyte MinMagnitudeNumber(sbyte left, sbyte right) => MinMagnitude(left, right);
        private static int Magnitude(sbyte value) => value == MinValue ? 128 : value < 0 ? -value : value;
        public static sbyte Log2(sbyte value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException();
            var result = 0;
            while (value > 1) { value >>= 1; result++; }
            return (sbyte)result;
        }
        public static bool IsPow2(sbyte value) => value > 0 && (value & (value - 1)) == 0;
        public static sbyte LeadingZeroCount(sbyte value)
        {
            var bits = (byte)value;
            var result = 0;
            for (var bit = 7; bit >= 0 && (bits & (1 << bit)) == 0; bit--) result++;
            return (sbyte)result;
        }
        public static sbyte PopCount(sbyte value)
        {
            var bits = (byte)value;
            var result = 0;
            while (bits != 0) { bits &= (byte)(bits - 1); result++; }
            return (sbyte)result;
        }
        public static sbyte RotateLeft(sbyte value, int rotateAmount) => (sbyte)(((byte)value << (rotateAmount & 7)) | ((byte)value >> ((8 - rotateAmount) & 7)));
        public static sbyte RotateRight(sbyte value, int rotateAmount) => RotateLeft(value, -rotateAmount);
        public static sbyte TrailingZeroCount(sbyte value)
        {
            if (value == 0) return 8;
            var bits = (byte)value;
            var result = 0;
            while ((bits & 1) == 0) { bits >>= 1; result++; }
            return (sbyte)result;
        }
        public static (sbyte Quotient, sbyte Remainder) DivRem(sbyte left, sbyte right)
        {
            var quotient = (sbyte)(left / right);
            return (quotient, (sbyte)(left - quotient * right));
        }
        public int GetByteCount() => 1;
        public int GetShortestBitLength() => this switch { 0 => 0, > 0 => 8 - LeadingZeroCount(this), _ => 9 - LeadingZeroCount((sbyte)~this) };
        public bool TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < 1) { bytesWritten = 0; return false; }
            destination[0] = (byte)this;
            bytesWritten = 1;
            return true;
        }
        public bool TryWriteLittleEndian(Span<byte> destination, out int bytesWritten) => TryWriteBigEndian(destination, out bytesWritten);
        public static bool TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out sbyte value)
        {
            if (source.IsEmpty) { value = 0; return true; }

            var signExtension = (source[0] & 0x80) != 0;
            isUnsigned |= !signExtension;
            if (isUnsigned && signExtension)
            {
                value = 0;
                return false;
            }

            if (source.Length > 1)
            {
                var extension = signExtension ? (byte)0xFF : (byte)0x00;
                if (isUnsigned == ((source[^1] & 0x80) != 0))
                {
                    value = 0;
                    return false;
                }

                for (var index = 0; index < source.Length - 1; index++)
                {
                    if (source[index] != extension)
                    {
                        value = 0;
                        return false;
                    }
                }
            }

            value = (sbyte)source[source.Length - 1];
            return true;
        }
        public static bool TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out sbyte value)
        {
            if (source.IsEmpty) { value = 0; return true; }

            var signExtension = (source[source.Length - 1] & 0x80) != 0;
            isUnsigned |= !signExtension;
            if (isUnsigned && signExtension)
            {
                value = 0;
                return false;
            }

            if (source.Length > 1)
            {
                var extension = signExtension ? (byte)0xFF : (byte)0x00;
                if (isUnsigned == ((source[0] & 0x80) != 0))
                {
                    value = 0;
                    return false;
                }

                for (var index = 1; index < source.Length; index++)
                {
                    if (source[index] != extension)
                    {
                        value = 0;
                        return false;
                    }
                }
            }

            value = (sbyte)source[0];
            return true;
        }
        public static sbyte Parse(ReadOnlySpan<char> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => PrimitiveGenericMathSmall.ParseSByte(value, style);
        public static bool TryParse(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out sbyte result)
        {
            var valid = PrimitiveGenericMathSmall.TryParseSigned(value, style, 8, out var parsed);
            result = (sbyte)parsed;
            return valid;
        }
        public static sbyte Parse(ReadOnlySpan<byte> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => PrimitiveGenericMathSmall.ParseSByteUtf8(value, style);
        public static bool TryParse(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out sbyte result)
        {
            PrimitiveGenericMathSmall.ValidateIntegerStyle(style);
            var chars = new char[value.Length];
            var valid = true;
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] > 0x7F)
                {
                    valid = false;
                    break;
                }

                chars[index] = (char)value[index];
            }

            var parsed = 0L;
            valid = valid && PrimitiveGenericMathSmall.TryParseSigned(chars, style, 8, out parsed);
            result = (sbyte)parsed;
            return valid;
        }
        static sbyte Numerics.INumberBase<sbyte>.One => One;
        static sbyte Numerics.INumberBase<sbyte>.Zero => Zero;
        static int Numerics.INumberBase<sbyte>.Radix => Radix;
        static sbyte Numerics.INumberBase<sbyte>.Abs(sbyte value) => Abs(value);
        static sbyte Numerics.INumberBase<sbyte>.MaxMagnitude(sbyte left, sbyte right) => MaxMagnitude(left, right);
        static sbyte Numerics.INumberBase<sbyte>.MaxMagnitudeNumber(sbyte left, sbyte right) => MaxMagnitudeNumber(left, right);
        static sbyte Numerics.INumberBase<sbyte>.MinMagnitude(sbyte left, sbyte right) => MinMagnitude(left, right);
        static sbyte Numerics.INumberBase<sbyte>.MinMagnitudeNumber(sbyte left, sbyte right) => MinMagnitudeNumber(left, right);
        static bool Numerics.INumberBase<sbyte>.IsCanonical(sbyte value) => IsCanonical(value);
        static bool Numerics.INumberBase<sbyte>.IsComplexNumber(sbyte value) => IsComplexNumber(value);
        static bool Numerics.INumberBase<sbyte>.IsFinite(sbyte value) => IsFinite(value);
        static bool Numerics.INumberBase<sbyte>.IsImaginaryNumber(sbyte value) => IsImaginaryNumber(value);
        static bool Numerics.INumberBase<sbyte>.IsInfinity(sbyte value) => IsInfinity(value);
        static bool Numerics.INumberBase<sbyte>.IsInteger(sbyte value) => IsInteger(value);
        static bool Numerics.INumberBase<sbyte>.IsNaN(sbyte value) => IsNaN(value);
        static bool Numerics.INumberBase<sbyte>.IsNegative(sbyte value) => IsNegative(value);
        static bool Numerics.INumberBase<sbyte>.IsNegativeInfinity(sbyte value) => IsNegativeInfinity(value);
        static bool Numerics.INumberBase<sbyte>.IsNormal(sbyte value) => IsNormal(value);
        static bool Numerics.INumberBase<sbyte>.IsPositive(sbyte value) => IsPositive(value);
        static bool Numerics.INumberBase<sbyte>.IsPositiveInfinity(sbyte value) => IsPositiveInfinity(value);
        static bool Numerics.INumberBase<sbyte>.IsRealNumber(sbyte value) => IsRealNumber(value);
        static bool Numerics.INumberBase<sbyte>.IsSubnormal(sbyte value) => IsSubnormal(value);
        static bool Numerics.INumberBase<sbyte>.IsZero(sbyte value) => IsZero(value);
        static sbyte Numerics.IBinaryNumber<sbyte>.Log2(sbyte value) => Log2(value);
        static bool Numerics.IBinaryNumber<sbyte>.IsPow2(sbyte value) => IsPow2(value);
        static sbyte Numerics.INumber<sbyte>.CopySign(sbyte value, sbyte sign) => CopySign(value, sign);
        static sbyte Numerics.INumber<sbyte>.MaxNumber(sbyte left, sbyte right) => Max(left, right);
        static sbyte Numerics.INumber<sbyte>.MinNumber(sbyte left, sbyte right) => Min(left, right);
        static sbyte Numerics.INumberBase<sbyte>.MultiplyAddEstimate(sbyte left, sbyte right, sbyte addend) => (sbyte)(left * right + addend);
        static sbyte Numerics.IAdditiveIdentity<sbyte, sbyte>.AdditiveIdentity => Zero;
        static sbyte Numerics.IMultiplicativeIdentity<sbyte, sbyte>.MultiplicativeIdentity => One;
        static sbyte Numerics.IMinMaxValue<sbyte>.MinValue => MinValue;
        static sbyte Numerics.IMinMaxValue<sbyte>.MaxValue => MaxValue;
        static sbyte Numerics.ISignedNumber<sbyte>.NegativeOne => NegativeOne;
        private static bool TryConvert<TOther>(TOther value, out sbyte result)
            where TOther : Numerics.INumberBase<TOther>
        {
            try { result = Convert.ToSByte((object?)value); return true; }
            catch { result = 0; return false; }
        }
        static bool Numerics.INumberBase<sbyte>.TryConvertFromChecked<TOther>(TOther value, out sbyte result) => TryConvert(value, out result);
        static bool Numerics.INumberBase<sbyte>.TryConvertFromSaturating<TOther>(TOther value, out sbyte result) => TryConvert(value, out result);
        static bool Numerics.INumberBase<sbyte>.TryConvertFromTruncating<TOther>(TOther value, out sbyte result) => TryConvert(value, out result);
        static bool Numerics.INumberBase<sbyte>.TryConvertToChecked<TOther>(sbyte value, out TOther result) => TryConvertTo(value, out result);
        static bool Numerics.INumberBase<sbyte>.TryConvertToSaturating<TOther>(sbyte value, out TOther result) => TryConvertTo(value, out result);
        static bool Numerics.INumberBase<sbyte>.TryConvertToTruncating<TOther>(sbyte value, out TOther result) => TryConvertTo(value, out result);
        private static bool TryConvertTo<TOther>(sbyte value, out TOther result)
            where TOther : Numerics.INumberBase<TOther>
        {
            try { result = TOther.CreateTruncating(value); return true; }
            catch { result = default!; return false; }
        }
    }

    public partial struct Int16 : IComparable, IComparable<short>, IConvertible, IEquatable<short>,
        IFormattable, IParsable<short>, ISpanFormattable, ISpanParsable<short>,
        IUtf8SpanFormattable, IUtf8SpanParsable<short>,
        Numerics.IAdditionOperators<short, short, short>, Numerics.IAdditiveIdentity<short, short>,
        Numerics.IBinaryInteger<short>, Numerics.IBinaryNumber<short>, Numerics.IBitwiseOperators<short, short, short>,
        Numerics.IComparisonOperators<short, short, bool>, Numerics.IDecrementOperators<short>,
        Numerics.IDivisionOperators<short, short, short>, Numerics.IEqualityOperators<short, short, bool>,
        Numerics.IIncrementOperators<short>, Numerics.IMinMaxValue<short>, Numerics.IModulusOperators<short, short, short>,
        Numerics.IMultiplicativeIdentity<short, short>, Numerics.IMultiplyOperators<short, short, short>,
        Numerics.INumber<short>, Numerics.INumberBase<short>, Numerics.IShiftOperators<short, int, short>,
        Numerics.ISignedNumber<short>, Numerics.ISubtractionOperators<short, short, short>,
        Numerics.IUnaryNegationOperators<short, short>, Numerics.IUnaryPlusOperators<short, short>
    {
        public const short MinValue = (short)-32768;
        public const short MaxValue = (short)32767;
        public bool Equals(short other) => this == other;
        public int CompareTo(short other) => Number.Compare((long)this, other);
        public int CompareTo(object? value) => CompareObject(value);
        public override bool Equals(object? value) => value is short other && Equals(other);
        public override int GetHashCode() => this;
        public override string ToString() => Number.FormatSigned(this, 16, null);
        public string ToString(string? format) => Number.FormatSigned(this, 16, format);
        public static short Parse(string value) => ParseSigned(value, false);
        public static short Parse(string value, Globalization.NumberStyles style) =>
            ParseSigned(value, Number.IsHexadecimal(style));
        public static bool TryParse(string? value, out short result)
        {
            var valid = Number.TryParseSigned(value, 16, false, out var parsed, out _);
            result = (short)parsed;
            return valid;
        }
        public static bool TryParse(string? value, Globalization.NumberStyles style, out short result)
        {
            var valid = Number.TryParseSigned(value, 16, Number.IsHexadecimal(style), out var parsed, out _);
            result = (short)parsed;
            return valid;
        }
        private static short ParseSigned(string value, bool hexadecimal)
        {
            if (value == null) throw new ArgumentNullException();
            if (Number.TryParseSigned(value, 16, hexadecimal, out var result, out var overflow)) return (short)result;
            if (overflow) throw new OverflowException();
            throw new FormatException();
        }

        public static short One => 1;
        public static short Zero => 0;
        public static short NegativeOne => (short)(-1);
        public static int Radix => 2;
        public static short Abs(short value) => value == MinValue ? throw new OverflowException() : value < 0 ? (short)-value : value;
        public static short Clamp(short value, short minimum, short maximum) =>
            minimum > maximum ? throw new ArgumentException() : value < minimum ? minimum : value > maximum ? maximum : value;
        public static short CopySign(short value, short sign) => sign < 0 ? (Abs(value) == MinValue ? MinValue : (short)-Abs(value)) : Abs(value);
        public static short Max(short left, short right) => left >= right ? left : right;
        public static short Min(short left, short right) => left <= right ? left : right;
        public static int Sign(short value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        public static bool IsCanonical(short value) => true;
        public static bool IsComplexNumber(short value) => false;
        public static bool IsEvenInteger(short value) => (value & 1) == 0;
        public static bool IsFinite(short value) => true;
        public static bool IsImaginaryNumber(short value) => false;
        public static bool IsInfinity(short value) => false;
        public static bool IsInteger(short value) => true;
        public static bool IsNaN(short value) => false;
        public static bool IsNegative(short value) => value < 0;
        public static bool IsNegativeInfinity(short value) => false;
        public static bool IsNormal(short value) => value != 0;
        public static bool IsOddInteger(short value) => (value & 1) != 0;
        public static bool IsPositive(short value) => value >= 0;
        public static bool IsPositiveInfinity(short value) => false;
        public static bool IsRealNumber(short value) => true;
        public static bool IsSubnormal(short value) => false;
        public static bool IsZero(short value) => value == 0;
        public static short MaxMagnitude(short left, short right) => Abs(left) >= Abs(right) ? left : right;
        public static short MaxMagnitudeNumber(short left, short right) => MaxMagnitude(left, right);
        public static short MinMagnitude(short left, short right) => Abs(left) <= Abs(right) ? left : right;
        public static short MinMagnitudeNumber(short left, short right) => MinMagnitude(left, right);
        public static short Log2(short value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException();
            var result = 0;
            ulong bits = (ulong)value;
            while (bits > 1) { bits >>= 1; result++; }
            return (short)result;
        }
        public static bool IsPow2(short value)
        {
            ulong bits = (ulong)value;
            return value > 0 && bits != 0 && (bits & (bits - 1)) == 0;
        }
        public static short LeadingZeroCount(short value)
        {
            ulong bits = (ulong)value;
            var result = 0;
            for (var bit = 15; bit >= 0 && (bits & (1UL << bit)) == 0; bit--) result++;
            return (short)result;
        }
        public static short PopCount(short value)
        {
            ulong bits = (ulong)value;
            var result = 0;
            while (bits != 0) { bits &= bits - 1; result++; }
            return (short)result;
        }
        public static short RotateLeft(short value, int rotateAmount)
        {
            rotateAmount &= 15;
            ulong bits = (ulong)value;
            return (short)((bits << rotateAmount) | (bits >> ((16 - rotateAmount) & 15)));
        }
        public static short RotateRight(short value, int rotateAmount) => RotateLeft(value, -rotateAmount);
        public static short TrailingZeroCount(short value)
        {
            if (value == 0) return 16;
            var bits = (ushort)value;
            var result = 0;
            while ((bits & 1) == 0) { bits >>= 1; result++; }
            return (short)result;
        }
        public static (short Quotient, short Remainder) DivRem(short left, short right)
        {
            var quotient = (short)(left / right);
            return (quotient, (short)(left - quotient * right));
        }
        public int GetByteCount() => 2;
        public int GetShortestBitLength() => this == 0 ? 0 : (this >= 0 ? 16 - (int)LeadingZeroCount(this) : 16 + 1 - (int)LeadingZeroCount((short)~(ulong)this));
        public bool TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < 2) { bytesWritten = 0; return false; }
            ulong bits = (ulong)this;
            for (var i = 0; i < 2; i++) destination[2 - 1 - i] = (byte)(bits >> (i * 8));
            bytesWritten = 2;
            return true;
        }
        public bool TryWriteLittleEndian(Span<byte> destination, out int bytesWritten)
        {
            if (destination.Length < 2) { bytesWritten = 0; return false; }
            ulong bits = (ulong)this;
            for (var i = 0; i < 2; i++) destination[i] = (byte)(bits >> (i * 8));
            bytesWritten = 2;
            return true;
        }
        public static bool TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out short value)
        {
            if (source.Length == 0) { value = 0; return true; }
            var count = source.Length;
            var start = count > 2 ? count - 2 : 0;
            byte sign = (byte)(isUnsigned ? 0 : (source[0] & 0x80) != 0 ? 0xff : 0);
            for (var i = 0; i < start; i++) if (source[i] != sign) { value = 0; return false; }
            var used = source.Slice(start);
            ulong bits = 0;
            for (var i = 0; i < used.Length; i++) bits = (bits << 8) | used[i];
            if (isUnsigned && (used.Length == 2 && (bits & (1UL << 15)) != 0)) { value = 0; return false; }
            if (!isUnsigned && used.Length < 2 && (used[0] & 0x80) != 0) bits |= ~0UL << (used.Length * 8);
            value = (short)bits;
            return true;
        }
        public static bool TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out short value)
        {
            if (source.Length == 0) { value = 0; return true; }
            var count = source.Length;
            var usedLength = count > 2 ? 2 : count;
            byte sign = (byte)(isUnsigned ? 0 : (source[count - 1] & 0x80) != 0 ? 0xff : 0);
            for (var i = usedLength; i < count; i++) if (source[i] != sign) { value = 0; return false; }
            ulong bits = 0;
            for (var i = usedLength - 1; i >= 0; i--) bits = (bits << 8) | source[i];
            if (isUnsigned && (usedLength == 2 && (bits & (1UL << 15)) != 0)) { value = 0; return false; }
            if (!isUnsigned && usedLength < 2 && (source[usedLength - 1] & 0x80) != 0) bits |= ~0UL << (usedLength * 8);
            value = (short)bits;
            return true;
        }
        static short Numerics.INumberBase<short>.One => One;
        static short Numerics.INumberBase<short>.Zero => Zero;
        static int Numerics.INumberBase<short>.Radix => Radix;
        static short Numerics.INumberBase<short>.Abs(short value) => Abs(value);
        static short Numerics.INumberBase<short>.MaxMagnitude(short left, short right) => MaxMagnitude(left, right);
        static short Numerics.INumberBase<short>.MaxMagnitudeNumber(short left, short right) => MaxMagnitudeNumber(left, right);
        static short Numerics.INumberBase<short>.MinMagnitude(short left, short right) => MinMagnitude(left, right);
        static short Numerics.INumberBase<short>.MinMagnitudeNumber(short left, short right) => MinMagnitudeNumber(left, right);
        static bool Numerics.INumberBase<short>.IsCanonical(short value) => IsCanonical(value);
        static bool Numerics.INumberBase<short>.IsComplexNumber(short value) => IsComplexNumber(value);
        static bool Numerics.INumberBase<short>.IsFinite(short value) => IsFinite(value);
        static bool Numerics.INumberBase<short>.IsImaginaryNumber(short value) => IsImaginaryNumber(value);
        static bool Numerics.INumberBase<short>.IsInfinity(short value) => IsInfinity(value);
        static bool Numerics.INumberBase<short>.IsInteger(short value) => IsInteger(value);
        static bool Numerics.INumberBase<short>.IsNaN(short value) => IsNaN(value);
        static bool Numerics.INumberBase<short>.IsNegative(short value) => IsNegative(value);
        static bool Numerics.INumberBase<short>.IsNegativeInfinity(short value) => IsNegativeInfinity(value);
        static bool Numerics.INumberBase<short>.IsNormal(short value) => IsNormal(value);
        static bool Numerics.INumberBase<short>.IsPositive(short value) => IsPositive(value);
        static bool Numerics.INumberBase<short>.IsPositiveInfinity(short value) => IsPositiveInfinity(value);
        static bool Numerics.INumberBase<short>.IsRealNumber(short value) => IsRealNumber(value);
        static bool Numerics.INumberBase<short>.IsSubnormal(short value) => IsSubnormal(value);
        static bool Numerics.INumberBase<short>.IsZero(short value) => IsZero(value);
        static short Numerics.IBinaryNumber<short>.AllBitsSet => (short)(~0);
        static short Numerics.IBinaryNumber<short>.Log2(short value) => Log2(value);
        static bool Numerics.IBinaryNumber<short>.IsPow2(short value) => IsPow2(value);
        static short Numerics.INumber<short>.MaxNumber(short left, short right) => Max(left, right);
        static short Numerics.INumber<short>.MinNumber(short left, short right) => Min(left, right);
        static short Numerics.INumberBase<short>.MultiplyAddEstimate(short left, short right, short addend) => (short)(left * right + addend);
        static (short Quotient, short Remainder) Numerics.IBinaryInteger<short>.DivRem(short left, short right) => DivRem(left, right);
        static short Numerics.IBinaryInteger<short>.LeadingZeroCount(short value) => LeadingZeroCount(value);
        static short Numerics.IBinaryInteger<short>.PopCount(short value) => PopCount(value);
        static short Numerics.IBinaryInteger<short>.RotateLeft(short value, int amount) => RotateLeft(value, amount);
        static short Numerics.IBinaryInteger<short>.RotateRight(short value, int amount) => RotateRight(value, amount);
        static short Numerics.IBinaryInteger<short>.TrailingZeroCount(short value) => TrailingZeroCount(value);
        static short Numerics.ISignedNumber<short>.NegativeOne => NegativeOne;
        static short Numerics.IAdditionOperators<short, short, short>.operator +(short left, short right) => (short)(left + right);
        static short Numerics.IAdditionOperators<short, short, short>.operator checked +(short left, short right) => checked((short)(left + right));
        static short Numerics.IAdditiveIdentity<short, short>.AdditiveIdentity => Zero;
        static short Numerics.IBitwiseOperators<short, short, short>.operator &(short left, short right) => (short)(left & right);
        static short Numerics.IBitwiseOperators<short, short, short>.operator |(short left, short right) => (short)(left | right);
        static short Numerics.IBitwiseOperators<short, short, short>.operator ^(short left, short right) => (short)(left ^ right);
        static short Numerics.IBitwiseOperators<short, short, short>.operator ~(short value) => (short)(~value);
        static bool Numerics.IComparisonOperators<short, short, bool>.operator <(short left, short right) => left < right;
        static bool Numerics.IComparisonOperators<short, short, bool>.operator <=(short left, short right) => left <= right;
        static bool Numerics.IComparisonOperators<short, short, bool>.operator >(short left, short right) => left > right;
        static bool Numerics.IComparisonOperators<short, short, bool>.operator >=(short left, short right) => left >= right;
        static short Numerics.IDecrementOperators<short>.operator --(short value) => --value;
        static short Numerics.IDecrementOperators<short>.operator checked --(short value) => checked(--value);
        static short Numerics.IDivisionOperators<short, short, short>.operator /(short left, short right) => (short)(left / right);
        static bool Numerics.IEqualityOperators<short, short, bool>.operator ==(short left, short right) => left == right;
        static bool Numerics.IEqualityOperators<short, short, bool>.operator !=(short left, short right) => left != right;
        static short Numerics.IIncrementOperators<short>.operator ++(short value) => ++value;
        static short Numerics.IIncrementOperators<short>.operator checked ++(short value) => checked(++value);
        static short Numerics.IModulusOperators<short, short, short>.operator %(short left, short right) => (short)(left % right);
        static short Numerics.IMultiplicativeIdentity<short, short>.MultiplicativeIdentity => One;
        static short Numerics.IMultiplyOperators<short, short, short>.operator *(short left, short right) => (short)(left * right);
        static short Numerics.IMultiplyOperators<short, short, short>.operator checked *(short left, short right) => checked((short)(left * right));
        static short Numerics.ISubtractionOperators<short, short, short>.operator -(short left, short right) => (short)(left - right);
        static short Numerics.ISubtractionOperators<short, short, short>.operator checked -(short left, short right) => checked((short)(left - right));
        static short Numerics.IUnaryNegationOperators<short, short>.operator -(short value) => (short)(-value);
        static short Numerics.IUnaryNegationOperators<short, short>.operator checked -(short value) => checked((short)(-value));
        static short Numerics.IUnaryPlusOperators<short, short>.operator +(short value) => (short)(+value);
        static short Numerics.IShiftOperators<short, int, short>.operator <<(short value, int shiftAmount) => (short)(value << (shiftAmount & 15));
        static short Numerics.IShiftOperators<short, int, short>.operator >>(short value, int shiftAmount) => (short)(value >> (shiftAmount & 15));
        static short Numerics.IShiftOperators<short, int, short>.operator >>>(short value, int shiftAmount) => (short)((ulong)value >> (shiftAmount & 15));
        private static bool TryConvert<TOther>(TOther value, out short result)
            where TOther : Numerics.INumberBase<TOther>
        {
            try { result = Convert.ToInt16((object?)value); return true; }
            catch { result = 0; return false; }
        }
        private static bool TryConvertTo<TOther>(short value, out TOther result)
            where TOther : Numerics.INumberBase<TOther>
        {
            try { result = TOther.CreateTruncating(value); return true; }
            catch { result = default!; return false; }
        }
        static bool Numerics.INumberBase<short>.TryConvertFromChecked<TOther>(TOther value, out short result) => TryConvert(value, out result);
        static bool Numerics.INumberBase<short>.TryConvertFromSaturating<TOther>(TOther value, out short result) => TryConvert(value, out result);
        static bool Numerics.INumberBase<short>.TryConvertFromTruncating<TOther>(TOther value, out short result) => TryConvert(value, out result);
        static bool Numerics.INumberBase<short>.TryConvertToChecked<TOther>(short value, out TOther result) => TryConvertTo(value, out result);
        static bool Numerics.INumberBase<short>.TryConvertToSaturating<TOther>(short value, out TOther result) => TryConvertTo(value, out result);
        static bool Numerics.INumberBase<short>.TryConvertToTruncating<TOther>(short value, out TOther result) => TryConvertTo(value, out result);
        public static short Parse(string value, Globalization.NumberStyles style, IFormatProvider? provider) => Parse(value, style);
        public static bool TryParse(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out short result) => TryParse(value, style, out result);
        public static short Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => Parse(value, Globalization.NumberStyles.Integer, provider);
        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out short result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
        public static short Parse(ReadOnlySpan<char> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(value.ToString(), style);
        public static bool TryParse(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out short result) => TryParse(value.ToString(), style, out result);
        public static short Parse(ReadOnlySpan<byte> value, IFormatProvider? provider) => Parse(value, Globalization.NumberStyles.Integer, provider);
        public static bool TryParse(ReadOnlySpan<byte> value, IFormatProvider? provider, out short result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
        public static short Parse(ReadOnlySpan<byte> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(PrimitiveGenericMathSmall.CreateStringFromUtf8(value), style);
        public static bool TryParse(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out short result) => TryParse(PrimitiveGenericMathSmall.CreateStringFromUtf8(value), style, out result);

        private int CompareObject(object? value)
        {
            if (value == null) return 1;
            if (value is short other) return CompareTo(other);
            throw new ArgumentException();
        }
    }

    public partial struct UInt16 : IComparable, IComparable<ushort>, IConvertible, IEquatable<ushort>,
        IFormattable, IParsable<ushort>, ISpanFormattable, ISpanParsable<ushort>,
        IUtf8SpanFormattable, IUtf8SpanParsable<ushort>,
        Numerics.IAdditionOperators<ushort, ushort, ushort>, Numerics.IAdditiveIdentity<ushort, ushort>,
        Numerics.IBinaryInteger<ushort>, Numerics.IBinaryNumber<ushort>, Numerics.IBitwiseOperators<ushort, ushort, ushort>,
        Numerics.IComparisonOperators<ushort, ushort, bool>, Numerics.IDecrementOperators<ushort>,
        Numerics.IDivisionOperators<ushort, ushort, ushort>, Numerics.IEqualityOperators<ushort, ushort, bool>,
        Numerics.IIncrementOperators<ushort>, Numerics.IMinMaxValue<ushort>, Numerics.IModulusOperators<ushort, ushort, ushort>,
        Numerics.IMultiplicativeIdentity<ushort, ushort>, Numerics.IMultiplyOperators<ushort, ushort, ushort>,
        Numerics.INumber<ushort>, Numerics.INumberBase<ushort>, Numerics.IShiftOperators<ushort, int, ushort>,
        Numerics.ISubtractionOperators<ushort, ushort, ushort>, Numerics.IUnaryNegationOperators<ushort, ushort>,
        Numerics.IUnaryPlusOperators<ushort, ushort>, Numerics.IUnsignedNumber<ushort>
    {
        public const ushort MinValue = (ushort)0;
        public const ushort MaxValue = (ushort)65535;
        public bool Equals(ushort other) => this == other;
        public int CompareTo(ushort other) => Number.Compare((ulong)this, other);
        public int CompareTo(object? value) => CompareObject(value);
        public override bool Equals(object? value) => value is ushort other && Equals(other);
        public override int GetHashCode() => this;
        public override string ToString() => Number.FormatUnsigned(this, 16, null);
        public string ToString(string? format) => Number.FormatUnsigned(this, 16, format);
        public static ushort Parse(string value) => ParseUnsigned(value, false);
        public static ushort Parse(string value, Globalization.NumberStyles style) =>
            ParseUnsigned(value, Number.IsHexadecimal(style));
        public static bool TryParse(string? value, out ushort result)
        {
            var valid = Number.TryParseUnsigned(value, 16, false, out var parsed, out _);
            result = (ushort)parsed;
            return valid;
        }
        public static bool TryParse(string? value, Globalization.NumberStyles style, out ushort result)
        {
            var valid = Number.TryParseUnsigned(value, 16, Number.IsHexadecimal(style), out var parsed, out _);
            result = (ushort)parsed;
            return valid;
        }
        private static ushort ParseUnsigned(string value, bool hexadecimal)
        {
            if (value == null) throw new ArgumentNullException();
            if (Number.TryParseUnsigned(value, 16, hexadecimal, out var result, out var overflow)) return (ushort)result;
            if (overflow) throw new OverflowException();
            throw new FormatException();
        }

        public static ushort One => 1;
        public static ushort Zero => 0;
        public static int Radix => 2;
        public static ushort Abs(ushort value) => value;
        public static ushort Clamp(ushort value, ushort min, ushort max) => min > max ? throw new ArgumentException() : value < min ? min : value > max ? max : value;
        public static ushort Max(ushort left, ushort right) => left >= right ? left : right;
        public static ushort Min(ushort left, ushort right) => left <= right ? left : right;
        public static int Sign(ushort value) => value == 0 ? 0 : 1;
        public static bool IsCanonical(ushort value) => true;
        public static bool IsComplexNumber(ushort value) => false;
        public static bool IsEvenInteger(ushort value) => (value & 1) == 0;
        public static bool IsFinite(ushort value) => true;
        public static bool IsImaginaryNumber(ushort value) => false;
        public static bool IsInfinity(ushort value) => false;
        public static bool IsInteger(ushort value) => true;
        public static bool IsNaN(ushort value) => false;
        public static bool IsNegative(ushort value) => false;
        public static bool IsNegativeInfinity(ushort value) => false;
        public static bool IsNormal(ushort value) => value != 0;
        public static bool IsOddInteger(ushort value) => (value & 1) != 0;
        public static bool IsPositive(ushort value) => true;
        public static bool IsPositiveInfinity(ushort value) => false;
        public static bool IsRealNumber(ushort value) => true;
        public static bool IsSubnormal(ushort value) => false;
        public static bool IsZero(ushort value) => value == 0;
        public static ushort MaxMagnitude(ushort left, ushort right) => Max(left, right);
        public static ushort MaxMagnitudeNumber(ushort left, ushort right) => Max(left, right);
        public static ushort MinMagnitude(ushort left, ushort right) => Min(left, right);
        public static ushort MinMagnitudeNumber(ushort left, ushort right) => Min(left, right);
        public static ushort Log2(ushort value) { var result = 0; while (value > 1) { value >>= 1; result++; } return (ushort)result; }
        public static bool IsPow2(ushort value) => value != 0 && (value & (value - 1)) == 0;
        public static ushort LeadingZeroCount(ushort value) { var result = 0; for (var bit = 15; bit >= 0 && (value & (1 << bit)) == 0; bit--) result++; return (ushort)result; }
        public static ushort PopCount(ushort value) { var result = 0; while (value != 0) { value &= (ushort)(value - 1); result++; } return (ushort)result; }
        public static ushort RotateLeft(ushort value, int amount) { amount &= 15; return (ushort)((value << amount) | (value >> ((16 - amount) & 15))); }
        public static ushort RotateRight(ushort value, int amount) => RotateLeft(value, -amount);
        public static ushort TrailingZeroCount(ushort value) { if (value == 0) return 16; var result = 0; while ((value & 1) == 0) { value >>= 1; result++; } return (ushort)result; }
        public static (ushort Quotient, ushort Remainder) DivRem(ushort left, ushort right) { var q = (ushort)(left / right); return (q, (ushort)(left - q * right)); }
        public int GetByteCount() => 2;
        public int GetShortestBitLength() => this == 0 ? 0 : 16 - LeadingZeroCount(this);
        public bool TryWriteBigEndian(Span<byte> destination, out int bytesWritten) { if (destination.Length < 2) { bytesWritten = 0; return false; } destination[0] = (byte)(this >> 8); destination[1] = (byte)this; bytesWritten = 2; return true; }
        public bool TryWriteLittleEndian(Span<byte> destination, out int bytesWritten) { if (destination.Length < 2) { bytesWritten = 0; return false; } destination[0] = (byte)this; destination[1] = (byte)(this >> 8); bytesWritten = 2; return true; }
        public static bool TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out ushort value) { if (source.Length != 2) { value = 0; return false; } value = (ushort)((source[0] << 8) | source[1]); return true; }
        public static bool TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out ushort value) { if (source.Length != 2) { value = 0; return false; } value = (ushort)(source[0] | (source[1] << 8)); return true; }
        static ushort Numerics.INumberBase<ushort>.One => One;
        static ushort Numerics.INumberBase<ushort>.Zero => Zero;
        static int Numerics.INumberBase<ushort>.Radix => Radix;
        static ushort Numerics.INumberBase<ushort>.Abs(ushort value) => Abs(value);
        static ushort Numerics.INumberBase<ushort>.MaxMagnitude(ushort l, ushort r) => MaxMagnitude(l, r);
        static ushort Numerics.INumberBase<ushort>.MaxMagnitudeNumber(ushort l, ushort r) => MaxMagnitudeNumber(l, r);
        static ushort Numerics.INumberBase<ushort>.MinMagnitude(ushort l, ushort r) => MinMagnitude(l, r);
        static ushort Numerics.INumberBase<ushort>.MinMagnitudeNumber(ushort l, ushort r) => MinMagnitudeNumber(l, r);
        static bool Numerics.INumberBase<ushort>.IsCanonical(ushort v) => IsCanonical(v);
        static bool Numerics.INumberBase<ushort>.IsComplexNumber(ushort v) => IsComplexNumber(v);
        static bool Numerics.INumberBase<ushort>.IsFinite(ushort v) => IsFinite(v);
        static bool Numerics.INumberBase<ushort>.IsImaginaryNumber(ushort v) => IsImaginaryNumber(v);
        static bool Numerics.INumberBase<ushort>.IsInfinity(ushort v) => IsInfinity(v);
        static bool Numerics.INumberBase<ushort>.IsInteger(ushort v) => IsInteger(v);
        static bool Numerics.INumberBase<ushort>.IsNaN(ushort v) => IsNaN(v);
        static bool Numerics.INumberBase<ushort>.IsNegative(ushort v) => IsNegative(v);
        static bool Numerics.INumberBase<ushort>.IsNegativeInfinity(ushort v) => IsNegativeInfinity(v);
        static bool Numerics.INumberBase<ushort>.IsNormal(ushort v) => IsNormal(v);
        static bool Numerics.INumberBase<ushort>.IsPositive(ushort v) => IsPositive(v);
        static bool Numerics.INumberBase<ushort>.IsPositiveInfinity(ushort v) => IsPositiveInfinity(v);
        static bool Numerics.INumberBase<ushort>.IsRealNumber(ushort v) => IsRealNumber(v);
        static bool Numerics.INumberBase<ushort>.IsSubnormal(ushort v) => IsSubnormal(v);
        static bool Numerics.INumberBase<ushort>.IsZero(ushort v) => IsZero(v);
        static ushort Numerics.IBinaryNumber<ushort>.AllBitsSet => ushort.MaxValue;
        static ushort Numerics.IBinaryNumber<ushort>.Log2(ushort value) => Log2(value);
        static bool Numerics.IBinaryNumber<ushort>.IsPow2(ushort value) => IsPow2(value);
        static ushort Numerics.INumber<ushort>.MaxNumber(ushort l, ushort r) => Max(l, r);
        static ushort Numerics.INumber<ushort>.MinNumber(ushort l, ushort r) => Min(l, r);
        static ushort Numerics.INumberBase<ushort>.MultiplyAddEstimate(ushort l, ushort r, ushort a) => (ushort)(l * r + a);
        static ushort Numerics.IBinaryInteger<ushort>.PopCount(ushort v) => PopCount(v);
        static ushort Numerics.IBinaryInteger<ushort>.TrailingZeroCount(ushort v) => TrailingZeroCount(v);
        static ushort Numerics.IAdditionOperators<ushort, ushort, ushort>.operator +(ushort l, ushort r) => (ushort)(l + r);
        static ushort Numerics.IAdditionOperators<ushort, ushort, ushort>.operator checked +(ushort l, ushort r) => checked((ushort)(l + r));
        static ushort Numerics.IAdditiveIdentity<ushort, ushort>.AdditiveIdentity => Zero;
        static ushort Numerics.IBitwiseOperators<ushort, ushort, ushort>.operator &(ushort l, ushort r) => (ushort)(l & r);
        static ushort Numerics.IBitwiseOperators<ushort, ushort, ushort>.operator |(ushort l, ushort r) => (ushort)(l | r);
        static ushort Numerics.IBitwiseOperators<ushort, ushort, ushort>.operator ^(ushort l, ushort r) => (ushort)(l ^ r);
        static ushort Numerics.IBitwiseOperators<ushort, ushort, ushort>.operator ~(ushort v) => (ushort)~v;
        static bool Numerics.IComparisonOperators<ushort, ushort, bool>.operator <(ushort l, ushort r) => l < r;
        static bool Numerics.IComparisonOperators<ushort, ushort, bool>.operator <=(ushort l, ushort r) => l <= r;
        static bool Numerics.IComparisonOperators<ushort, ushort, bool>.operator >(ushort l, ushort r) => l > r;
        static bool Numerics.IComparisonOperators<ushort, ushort, bool>.operator >=(ushort l, ushort r) => l >= r;
        static ushort Numerics.IDecrementOperators<ushort>.operator --(ushort v) => --v;
        static ushort Numerics.IDecrementOperators<ushort>.operator checked --(ushort v) => checked(--v);
        static ushort Numerics.IDivisionOperators<ushort, ushort, ushort>.operator /(ushort l, ushort r) => (ushort)(l / r);
        static bool Numerics.IEqualityOperators<ushort, ushort, bool>.operator ==(ushort l, ushort r) => l == r;
        static bool Numerics.IEqualityOperators<ushort, ushort, bool>.operator !=(ushort l, ushort r) => l != r;
        static ushort Numerics.IIncrementOperators<ushort>.operator ++(ushort v) => ++v;
        static ushort Numerics.IIncrementOperators<ushort>.operator checked ++(ushort v) => checked(++v);
        static ushort Numerics.IModulusOperators<ushort, ushort, ushort>.operator %(ushort l, ushort r) => (ushort)(l % r);
        static ushort Numerics.IMultiplicativeIdentity<ushort, ushort>.MultiplicativeIdentity => One;
        static ushort Numerics.IMultiplyOperators<ushort, ushort, ushort>.operator *(ushort l, ushort r) => (ushort)(l * r);
        static ushort Numerics.IMultiplyOperators<ushort, ushort, ushort>.operator checked *(ushort l, ushort r) => checked((ushort)(l * r));
        static ushort Numerics.ISubtractionOperators<ushort, ushort, ushort>.operator -(ushort l, ushort r) => (ushort)(l - r);
        static ushort Numerics.ISubtractionOperators<ushort, ushort, ushort>.operator checked -(ushort l, ushort r) => checked((ushort)(l - r));
        static ushort Numerics.IUnaryNegationOperators<ushort, ushort>.operator -(ushort v) => (ushort)(-v);
        static ushort Numerics.IUnaryNegationOperators<ushort, ushort>.operator checked -(ushort v) => checked((ushort)(-v));
        static ushort Numerics.IUnaryPlusOperators<ushort, ushort>.operator +(ushort v) => v;
        static ushort Numerics.IShiftOperators<ushort, int, ushort>.operator <<(ushort v, int n) => (ushort)(v << (n & 15));
        static ushort Numerics.IShiftOperators<ushort, int, ushort>.operator >>(ushort v, int n) => (ushort)(v >> (n & 15));
        static ushort Numerics.IShiftOperators<ushort, int, ushort>.operator >>>(ushort v, int n) => (ushort)(v >> (n & 15));
        private static bool TryConvert<TOther>(TOther value, out ushort result) where TOther : Numerics.INumberBase<TOther> { try { result = Convert.ToUInt16((object?)value); return true; } catch { result = 0; return false; } }
        private static bool TryConvertTo<TOther>(ushort value, out TOther result) where TOther : Numerics.INumberBase<TOther> { try { result = TOther.CreateTruncating(value); return true; } catch { result = default!; return false; } }
        static bool Numerics.INumberBase<ushort>.TryConvertFromChecked<TOther>(TOther v, out ushort r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<ushort>.TryConvertFromSaturating<TOther>(TOther v, out ushort r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<ushort>.TryConvertFromTruncating<TOther>(TOther v, out ushort r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<ushort>.TryConvertToChecked<TOther>(ushort v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<ushort>.TryConvertToSaturating<TOther>(ushort v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<ushort>.TryConvertToTruncating<TOther>(ushort v, out TOther r) => TryConvertTo(v, out r);
        public static ushort Parse(string value, Globalization.NumberStyles style, IFormatProvider? provider) => Parse(value, style);
        public static bool TryParse(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out ushort result) => TryParse(value, style, out result);
        public static ushort Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => Parse(value, Globalization.NumberStyles.Integer, provider);
        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out ushort result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
        public static ushort Parse(ReadOnlySpan<char> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(value.ToString(), style);
        public static bool TryParse(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out ushort result) => TryParse(value.ToString(), style, out result);
        public static ushort Parse(ReadOnlySpan<byte> value, IFormatProvider? provider) => Parse(value, Globalization.NumberStyles.Integer, provider);
        public static bool TryParse(ReadOnlySpan<byte> value, IFormatProvider? provider, out ushort result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
        public static ushort Parse(ReadOnlySpan<byte> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(PrimitiveGenericMathSmall.CreateStringFromUtf8(value), style);
        public static bool TryParse(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out ushort result) => TryParse(PrimitiveGenericMathSmall.CreateStringFromUtf8(value), style, out result);

        private int CompareObject(object? value)
        {
            if (value == null) return 1;
            if (value is ushort other) return CompareTo(other);
            throw new ArgumentException();
        }
    }

    public partial struct Int32 : IComparable, IComparable<int>, IConvertible, IEquatable<int>,
        IFormattable, IParsable<int>, ISpanFormattable, ISpanParsable<int>,
        IUtf8SpanFormattable, IUtf8SpanParsable<int>,
        Numerics.IAdditionOperators<int, int, int>, Numerics.IAdditiveIdentity<int, int>,
        Numerics.IBinaryInteger<int>, Numerics.IBinaryNumber<int>, Numerics.IBitwiseOperators<int, int, int>,
        Numerics.IComparisonOperators<int, int, bool>, Numerics.IDecrementOperators<int>,
        Numerics.IDivisionOperators<int, int, int>, Numerics.IEqualityOperators<int, int, bool>,
        Numerics.IIncrementOperators<int>, Numerics.IMinMaxValue<int>, Numerics.IModulusOperators<int, int, int>,
        Numerics.IMultiplicativeIdentity<int, int>, Numerics.IMultiplyOperators<int, int, int>,
        Numerics.INumber<int>, Numerics.INumberBase<int>, Numerics.IShiftOperators<int, int, int>,
        Numerics.ISignedNumber<int>, Numerics.ISubtractionOperators<int, int, int>,
        Numerics.IUnaryNegationOperators<int, int>, Numerics.IUnaryPlusOperators<int, int>
    {
        public const int MaxValue = 2147483647;
        public const int MinValue = -2147483648;

        public bool Equals(int other) => this == other;
        public int CompareTo(int other) => Number.Compare((long)this, other);
        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (value is int other) return CompareTo(other);
            throw new ArgumentException();
        }

        public override bool Equals(object? value) =>
            value is int other && this == other;

        public override int GetHashCode() => this;
        public override string ToString() => Number.FormatSigned(this, 32, null);
        public string ToString(string? format) => Number.FormatSigned(this, 32, format);
        public static int Parse(string value) => ParseSigned(value, false);
        public static int Parse(string value, Globalization.NumberStyles style) =>
            ParseSigned(value, Number.IsHexadecimal(style));
        public static bool TryParse(string? value, out int result)
        {
            var valid = Number.TryParseSigned(value, 32, false, out var parsed, out _);
            result = (int)parsed;
            return valid;
        }
        public static bool TryParse(string? value, Globalization.NumberStyles style, out int result)
        {
            var valid = Number.TryParseSigned(value, 32, Number.IsHexadecimal(style), out var parsed, out _);
            result = (int)parsed;
            return valid;
        }
        private static int ParseSigned(string value, bool hexadecimal)
        {
            if (value == null) throw new ArgumentNullException();
            if (Number.TryParseSigned(value, 32, hexadecimal, out var result, out var overflow)) return (int)result;
            if (overflow) throw new OverflowException();
            throw new FormatException();
        }

        public static int One => 1;
        public static int Zero => 0;
        public static int NegativeOne => -1;
        public static int Radix => 2;
        public static int Abs(int value) => value == MinValue ? throw new OverflowException() : value < 0 ? -value : value;
        public static int Clamp(int value, int min, int max) => min > max ? throw new ArgumentException() : value < min ? min : value > max ? max : value;
        public static int CopySign(int value, int sign) => sign < 0 ? (Abs(value) == MinValue ? MinValue : -Abs(value)) : Abs(value);
        public static int Max(int left, int right) => left >= right ? left : right;
        public static int Min(int left, int right) => left <= right ? left : right;
        public static int Sign(int value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        public static bool IsCanonical(int value) => true;
        public static bool IsComplexNumber(int value) => false;
        public static bool IsEvenInteger(int value) => (value & 1) == 0;
        public static bool IsFinite(int value) => true;
        public static bool IsImaginaryNumber(int value) => false;
        public static bool IsInfinity(int value) => false;
        public static bool IsInteger(int value) => true;
        public static bool IsNaN(int value) => false;
        public static bool IsNegative(int value) => value < 0;
        public static bool IsNegativeInfinity(int value) => false;
        public static bool IsNormal(int value) => value != 0;
        public static bool IsOddInteger(int value) => (value & 1) != 0;
        public static bool IsPositive(int value) => value >= 0;
        public static bool IsPositiveInfinity(int value) => false;
        public static bool IsRealNumber(int value) => true;
        public static bool IsSubnormal(int value) => false;
        public static bool IsZero(int value) => value == 0;
        public static int MaxMagnitude(int left, int right) => Abs(left) >= Abs(right) ? left : right;
        public static int MaxMagnitudeNumber(int left, int right) => MaxMagnitude(left, right);
        public static int MinMagnitude(int left, int right) => Abs(left) <= Abs(right) ? left : right;
        public static int MinMagnitudeNumber(int left, int right) => MinMagnitude(left, right);
        public static int Log2(int value) { if (value < 0) throw new ArgumentOutOfRangeException(); var result = 0; uint bits = (uint)value; while (bits > 1) { bits >>= 1; result++; } return result; }
        public static bool IsPow2(int value) => value > 0 && (value & (value - 1)) == 0;
        public static int LeadingZeroCount(int value) { uint bits = (uint)value; var result = 0; for (var bit = 31; bit >= 0 && (bits & (1u << bit)) == 0; bit--) result++; return result; }
        public static int PopCount(int value) { uint bits = (uint)value; var result = 0; while (bits != 0) { bits &= bits - 1; result++; } return result; }
        public static int TrailingZeroCount(int value) { if (value == 0) return 32; uint bits = (uint)value; var result = 0; while ((bits & 1) == 0) { bits >>= 1; result++; } return result; }
        public static int RotateLeft(int value, int amount) { amount &= 31; uint bits = (uint)value; return (int)((bits << amount) | (bits >> ((32 - amount) & 31))); }
        public static int RotateRight(int value, int amount) => RotateLeft(value, -amount);
        public static (int Quotient, int Remainder) DivRem(int left, int right) { var q = left / right; return (q, left - q * right); }
        public int GetByteCount() => 4;
        public int GetShortestBitLength() => this == 0 ? 0 : (this >= 0 ? 32 - LeadingZeroCount(this) : 33 - LeadingZeroCount(~this));
        public bool TryWriteBigEndian(Span<byte> destination, out int bytesWritten) { if (destination.Length < 4) { bytesWritten = 0; return false; } uint bits = (uint)this; for (var i = 0; i < 4; i++) destination[3 - i] = (byte)(bits >> (i * 8)); bytesWritten = 4; return true; }
        public bool TryWriteLittleEndian(Span<byte> destination, out int bytesWritten) { if (destination.Length < 4) { bytesWritten = 0; return false; } uint bits = (uint)this; for (var i = 0; i < 4; i++) destination[i] = (byte)(bits >> (i * 8)); bytesWritten = 4; return true; }
        public static bool TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out int value) { if (source.Length != 4) { value = 0; return false; } uint bits = (uint)((source[0] << 24) | (source[1] << 16) | (source[2] << 8) | source[3]); if (isUnsigned && (bits & 0x80000000) != 0) { value = 0; return false; } value = (int)bits; return true; }
        public static bool TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out int value) { if (source.Length != 4) { value = 0; return false; } uint bits = (uint)(source[0] | (source[1] << 8) | (source[2] << 16) | (source[3] << 24)); if (isUnsigned && (bits & 0x80000000) != 0) { value = 0; return false; } value = (int)bits; return true; }
        static int Numerics.INumberBase<int>.One => One;
        static int Numerics.INumberBase<int>.Zero => Zero;
        static int Numerics.INumberBase<int>.Radix => Radix;
        static int Numerics.INumberBase<int>.Abs(int v) => Abs(v);
        static int Numerics.INumberBase<int>.MaxMagnitude(int l, int r) => MaxMagnitude(l, r);
        static int Numerics.INumberBase<int>.MaxMagnitudeNumber(int l, int r) => MaxMagnitudeNumber(l, r);
        static int Numerics.INumberBase<int>.MinMagnitude(int l, int r) => MinMagnitude(l, r);
        static int Numerics.INumberBase<int>.MinMagnitudeNumber(int l, int r) => MinMagnitudeNumber(l, r);
        static bool Numerics.INumberBase<int>.IsCanonical(int v) => IsCanonical(v);
        static bool Numerics.INumberBase<int>.IsComplexNumber(int v) => IsComplexNumber(v);
        static bool Numerics.INumberBase<int>.IsFinite(int v) => IsFinite(v);
        static bool Numerics.INumberBase<int>.IsImaginaryNumber(int v) => IsImaginaryNumber(v);
        static bool Numerics.INumberBase<int>.IsInfinity(int v) => IsInfinity(v);
        static bool Numerics.INumberBase<int>.IsInteger(int v) => IsInteger(v);
        static bool Numerics.INumberBase<int>.IsNaN(int v) => IsNaN(v);
        static bool Numerics.INumberBase<int>.IsNegative(int v) => IsNegative(v);
        static bool Numerics.INumberBase<int>.IsNegativeInfinity(int v) => IsNegativeInfinity(v);
        static bool Numerics.INumberBase<int>.IsNormal(int v) => IsNormal(v);
        static bool Numerics.INumberBase<int>.IsPositive(int v) => IsPositive(v);
        static bool Numerics.INumberBase<int>.IsPositiveInfinity(int v) => IsPositiveInfinity(v);
        static bool Numerics.INumberBase<int>.IsRealNumber(int v) => IsRealNumber(v);
        static bool Numerics.INumberBase<int>.IsSubnormal(int v) => IsSubnormal(v);
        static bool Numerics.INumberBase<int>.IsZero(int v) => IsZero(v);
        static int Numerics.IBinaryNumber<int>.AllBitsSet => -1;
        static bool Numerics.IBinaryNumber<int>.IsPow2(int v) => IsPow2(v);
        static int Numerics.IBinaryNumber<int>.Log2(int v) => Log2(v);
        static int Numerics.INumber<int>.MaxNumber(int l, int r) => Max(l, r);
        static int Numerics.INumber<int>.MinNumber(int l, int r) => Min(l, r);
        static int Numerics.INumberBase<int>.MultiplyAddEstimate(int l, int r, int a) => (int)((long)l * r + a);
        static int Numerics.IBinaryInteger<int>.PopCount(int v) => PopCount(v);
        static int Numerics.IBinaryInteger<int>.TrailingZeroCount(int v) => TrailingZeroCount(v);
        static int Numerics.IMinMaxValue<int>.MinValue => MinValue;
        static int Numerics.IMinMaxValue<int>.MaxValue => MaxValue;
        static int Numerics.ISignedNumber<int>.NegativeOne => NegativeOne;
        static int Numerics.IAdditionOperators<int, int, int>.operator +(int l, int r) => l + r;
        static int Numerics.IAdditionOperators<int, int, int>.operator checked +(int l, int r) => checked(l + r);
        static int Numerics.IAdditiveIdentity<int, int>.AdditiveIdentity => Zero;
        static int Numerics.IBitwiseOperators<int, int, int>.operator &(int l, int r) => l & r;
        static int Numerics.IBitwiseOperators<int, int, int>.operator |(int l, int r) => l | r;
        static int Numerics.IBitwiseOperators<int, int, int>.operator ^(int l, int r) => l ^ r;
        static int Numerics.IBitwiseOperators<int, int, int>.operator ~(int v) => ~v;
        static bool Numerics.IComparisonOperators<int, int, bool>.operator <(int l, int r) => l < r;
        static bool Numerics.IComparisonOperators<int, int, bool>.operator <=(int l, int r) => l <= r;
        static bool Numerics.IComparisonOperators<int, int, bool>.operator >(int l, int r) => l > r;
        static bool Numerics.IComparisonOperators<int, int, bool>.operator >=(int l, int r) => l >= r;
        static int Numerics.IDecrementOperators<int>.operator --(int v) => --v;
        static int Numerics.IDecrementOperators<int>.operator checked --(int v) => checked(--v);
        static int Numerics.IDivisionOperators<int, int, int>.operator /(int l, int r) => l / r;
        static bool Numerics.IEqualityOperators<int, int, bool>.operator ==(int l, int r) => l == r;
        static bool Numerics.IEqualityOperators<int, int, bool>.operator !=(int l, int r) => l != r;
        static int Numerics.IIncrementOperators<int>.operator ++(int v) => ++v;
        static int Numerics.IIncrementOperators<int>.operator checked ++(int v) => checked(++v);
        static int Numerics.IModulusOperators<int, int, int>.operator %(int l, int r) => l % r;
        static int Numerics.IMultiplicativeIdentity<int, int>.MultiplicativeIdentity => One;
        static int Numerics.IMultiplyOperators<int, int, int>.operator *(int l, int r) => l * r;
        static int Numerics.IMultiplyOperators<int, int, int>.operator checked *(int l, int r) => checked(l * r);
        static int Numerics.ISubtractionOperators<int, int, int>.operator -(int l, int r) => l - r;
        static int Numerics.ISubtractionOperators<int, int, int>.operator checked -(int l, int r) => checked(l - r);
        static int Numerics.IUnaryNegationOperators<int, int>.operator -(int v) => -v;
        static int Numerics.IUnaryNegationOperators<int, int>.operator checked -(int v) => checked(-v);
        static int Numerics.IUnaryPlusOperators<int, int>.operator +(int v) => v;
        static int Numerics.IShiftOperators<int, int, int>.operator <<(int v, int n) => v << (n & 31);
        static int Numerics.IShiftOperators<int, int, int>.operator >>(int v, int n) => v >> (n & 31);
        static int Numerics.IShiftOperators<int, int, int>.operator >>>(int v, int n) => (int)((uint)v >> (n & 31));
        private static bool TryConvert<TOther>(TOther value, out int result) where TOther : Numerics.INumberBase<TOther> { try { result = Convert.ToInt32((object?)value); return true; } catch { result = 0; return false; } }
        private static bool TryConvertTo<TOther>(int value, out TOther result) where TOther : Numerics.INumberBase<TOther> { try { result = TOther.CreateTruncating(value); return true; } catch { result = default!; return false; } }
        static bool Numerics.INumberBase<int>.TryConvertFromChecked<TOther>(TOther v, out int r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<int>.TryConvertFromSaturating<TOther>(TOther v, out int r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<int>.TryConvertFromTruncating<TOther>(TOther v, out int r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<int>.TryConvertToChecked<TOther>(int v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<int>.TryConvertToSaturating<TOther>(int v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<int>.TryConvertToTruncating<TOther>(int v, out TOther r) => TryConvertTo(v, out r);
        public static int Parse(string value, Globalization.NumberStyles style, IFormatProvider? provider) => Parse(value, style);
        public static bool TryParse(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out int result) => TryParse(value, style, out result);
        public static int Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => Parse(value, Globalization.NumberStyles.Integer, provider);
        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out int result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
        public static int Parse(ReadOnlySpan<char> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(value.ToString(), style);
        public static bool TryParse(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out int result) => TryParse(value.ToString(), style, out result);
        public static int Parse(ReadOnlySpan<byte> value, IFormatProvider? provider) => Parse(value, Globalization.NumberStyles.Integer, provider);
        public static bool TryParse(ReadOnlySpan<byte> value, IFormatProvider? provider, out int result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
        public static int Parse(ReadOnlySpan<byte> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(PrimitiveGenericMathSmall.CreateStringFromUtf8(value), style);
        public static bool TryParse(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out int result) => TryParse(PrimitiveGenericMathSmall.CreateStringFromUtf8(value), style, out result);

    }

    public partial struct UInt32 : IComparable, IComparable<uint>, IConvertible, IEquatable<uint>,
        IFormattable, IParsable<uint>, ISpanFormattable, ISpanParsable<uint>,
        IUtf8SpanFormattable, IUtf8SpanParsable<uint>,
        Numerics.IAdditionOperators<uint, uint, uint>, Numerics.IAdditiveIdentity<uint, uint>,
        Numerics.IBinaryInteger<uint>, Numerics.IBinaryNumber<uint>, Numerics.IBitwiseOperators<uint, uint, uint>,
        Numerics.IComparisonOperators<uint, uint, bool>, Numerics.IDecrementOperators<uint>,
        Numerics.IDivisionOperators<uint, uint, uint>, Numerics.IEqualityOperators<uint, uint, bool>,
        Numerics.IIncrementOperators<uint>, Numerics.IMinMaxValue<uint>, Numerics.IModulusOperators<uint, uint, uint>,
        Numerics.IMultiplicativeIdentity<uint, uint>, Numerics.IMultiplyOperators<uint, uint, uint>,
        Numerics.INumber<uint>, Numerics.INumberBase<uint>, Numerics.IShiftOperators<uint, int, uint>,
        Numerics.ISubtractionOperators<uint, uint, uint>, Numerics.IUnaryNegationOperators<uint, uint>,
        Numerics.IUnaryPlusOperators<uint, uint>, Numerics.IUnsignedNumber<uint>
    {
        public const uint MaxValue = (uint)4294967295;
        public const uint MinValue = (uint)0;

        public bool Equals(uint other) => this == other;
        public int CompareTo(uint other) => Number.Compare((ulong)this, other);
        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (value is uint other) return CompareTo(other);
            throw new ArgumentException();
        }
        public override bool Equals(object? value) => value is uint other && Equals(other);
        public override int GetHashCode() => unchecked((int)this);
        public override string ToString() => Number.FormatUnsigned(this, 32, null);
        public string ToString(string? format) => Number.FormatUnsigned(this, 32, format);
        public static uint Parse(string value) => ParseUnsigned(value, false);
        public static uint Parse(string value, Globalization.NumberStyles style) =>
            ParseUnsigned(value, Number.IsHexadecimal(style));
        public static bool TryParse(string? value, out uint result)
        {
            var valid = Number.TryParseUnsigned(value, 32, false, out var parsed, out _);
            result = (uint)parsed;
            return valid;
        }
        public static bool TryParse(string? value, Globalization.NumberStyles style, out uint result)
        {
            var valid = Number.TryParseUnsigned(value, 32, Number.IsHexadecimal(style), out var parsed, out _);
            result = (uint)parsed;
            return valid;
        }
        private static uint ParseUnsigned(string value, bool hexadecimal)
        {
            if (value == null) throw new ArgumentNullException();
            if (Number.TryParseUnsigned(value, 32, hexadecimal, out var result, out var overflow)) return (uint)result;
            if (overflow) throw new OverflowException();
            throw new FormatException();
        }
        public static uint One => 1;
        public static uint Zero => 0;
        public static int Radix => 2;
        public static uint Abs(uint value) => value;
        public static uint Clamp(uint value, uint min, uint max) => min > max ? throw new ArgumentException() : value < min ? min : value > max ? max : value;
        public static uint Max(uint l, uint r) => l >= r ? l : r;
        public static uint Min(uint l, uint r) => l <= r ? l : r;
        public static int Sign(uint value) => value == 0 ? 0 : 1;
        public static bool IsCanonical(uint v) => true;
        public static bool IsComplexNumber(uint v) => false;
        public static bool IsEvenInteger(uint v) => (v & 1) == 0;
        public static bool IsFinite(uint v) => true;
        public static bool IsImaginaryNumber(uint v) => false;
        public static bool IsInfinity(uint v) => false;
        public static bool IsInteger(uint v) => true;
        public static bool IsNaN(uint v) => false;
        public static bool IsNegative(uint v) => false;
        public static bool IsNegativeInfinity(uint v) => false;
        public static bool IsNormal(uint v) => v != 0;
        public static bool IsOddInteger(uint v) => (v & 1) != 0;
        public static bool IsPositive(uint v) => true;
        public static bool IsPositiveInfinity(uint v) => false;
        public static bool IsRealNumber(uint v) => true;
        public static bool IsSubnormal(uint v) => false;
        public static bool IsZero(uint v) => v == 0;
        public static uint MaxMagnitude(uint l, uint r) => Max(l, r);
        public static uint MaxMagnitudeNumber(uint l, uint r) => MaxMagnitude(l, r);
        public static uint MinMagnitude(uint l, uint r) => Min(l, r);
        public static uint MinMagnitudeNumber(uint l, uint r) => MinMagnitude(l, r);
        public static uint Log2(uint value) { if (false) throw new ArgumentOutOfRangeException(); ulong bits = (ulong)value; var result = 0; while (bits > 1) { bits >>= 1; result++; } return (uint)result; }
        public static bool IsPow2(uint value) { ulong bits = (ulong)value; return bits != 0 && (bits & (bits - 1)) == 0; }
        public static uint LeadingZeroCount(uint value) { ulong bits = (ulong)value; var result = 0; for (var bit = 31; bit >= 0 && (bits & (1UL << bit)) == 0; bit--) result++; return (uint)result; }
        public static uint PopCount(uint value) { ulong bits = (ulong)value; var result = 0; while (bits != 0) { bits &= bits - 1; result++; } return (uint)result; }
        public static uint TrailingZeroCount(uint value) { if (value == 0) return (uint)32; ulong bits = (ulong)value; var result = 0; while ((bits & 1) == 0) { bits >>= 1; result++; } return (uint)result; }
        public static uint RotateLeft(uint value, int amount) { amount &= 31; ulong bits = (ulong)value; return (uint)((bits << amount) | (bits >> (32 - amount & 31))); }
        public static uint RotateRight(uint value, int amount) => RotateLeft(value, -amount);
        public static (uint Quotient, uint Remainder) DivRem(uint l, uint r) { var q = (uint)(l / r); return (q, (uint)(l - q * r)); }
        public int GetByteCount() => 4;
        public int GetShortestBitLength() => this == 0 ? 0 : 32 - (int)LeadingZeroCount(this);
        public bool TryWriteBigEndian(Span<byte> d, out int n) { if (d.Length < 4) { n = 0; return false; } ulong bits = (ulong)this; for (var i = 0; i < 4; i++) d[4 - 1 - i] = (byte)(bits >> (i * 8)); n = 4; return true; }
        public bool TryWriteLittleEndian(Span<byte> d, out int n) { if (d.Length < 4) { n = 0; return false; } ulong bits = (ulong)this; for (var i = 0; i < 4; i++) d[i] = (byte)(bits >> (i * 8)); n = 4; return true; }
        public static bool TryReadBigEndian(ReadOnlySpan<byte> s, bool u, out uint value) { if (s.Length != 4) { value = 0; return false; } ulong bits = 0; for (var i = 0; i < 4; i++) bits = (bits << 8) | s[i]; if (u && false) { value = 0; return false; } value = (uint)bits; return true; }
        public static bool TryReadLittleEndian(ReadOnlySpan<byte> s, bool u, out uint value) { if (s.Length != 4) { value = 0; return false; } ulong bits = 0; for (var i = 4 - 1; i >= 0; i--) bits = (bits << 8) | s[i]; value = (uint)bits; return true; }
        static uint Numerics.INumberBase<uint>.One => One;
        static uint Numerics.INumberBase<uint>.Zero => Zero;
        static int Numerics.INumberBase<uint>.Radix => Radix;
        static uint Numerics.INumberBase<uint>.Abs(uint v) => Abs(v);
        static uint Numerics.INumberBase<uint>.MaxMagnitude(uint l, uint r) => MaxMagnitude(l, r);
        static uint Numerics.INumberBase<uint>.MaxMagnitudeNumber(uint l, uint r) => MaxMagnitudeNumber(l, r);
        static uint Numerics.INumberBase<uint>.MinMagnitude(uint l, uint r) => MinMagnitude(l, r);
        static uint Numerics.INumberBase<uint>.MinMagnitudeNumber(uint l, uint r) => MinMagnitudeNumber(l, r);
        static bool Numerics.INumberBase<uint>.IsCanonical(uint v) => IsCanonical(v);
        static bool Numerics.INumberBase<uint>.IsComplexNumber(uint v) => IsComplexNumber(v);
        static bool Numerics.INumberBase<uint>.IsFinite(uint v) => IsFinite(v);
        static bool Numerics.INumberBase<uint>.IsImaginaryNumber(uint v) => IsImaginaryNumber(v);
        static bool Numerics.INumberBase<uint>.IsInfinity(uint v) => IsInfinity(v);
        static bool Numerics.INumberBase<uint>.IsInteger(uint v) => IsInteger(v);
        static bool Numerics.INumberBase<uint>.IsNaN(uint v) => IsNaN(v);
        static bool Numerics.INumberBase<uint>.IsNegative(uint v) => IsNegative(v);
        static bool Numerics.INumberBase<uint>.IsNegativeInfinity(uint v) => IsNegativeInfinity(v);
        static bool Numerics.INumberBase<uint>.IsNormal(uint v) => IsNormal(v);
        static bool Numerics.INumberBase<uint>.IsPositive(uint v) => IsPositive(v);
        static bool Numerics.INumberBase<uint>.IsPositiveInfinity(uint v) => IsPositiveInfinity(v);
        static bool Numerics.INumberBase<uint>.IsRealNumber(uint v) => IsRealNumber(v);
        static bool Numerics.INumberBase<uint>.IsSubnormal(uint v) => IsSubnormal(v);
        static bool Numerics.INumberBase<uint>.IsZero(uint v) => IsZero(v);
        static uint Numerics.IBinaryNumber<uint>.AllBitsSet => uint.MaxValue;
        static bool Numerics.IBinaryNumber<uint>.IsPow2(uint v) => IsPow2(v);
        static uint Numerics.IBinaryNumber<uint>.Log2(uint v) => Log2(v);
        static uint Numerics.INumber<uint>.MaxNumber(uint l, uint r) => Max(l, r);
        static uint Numerics.INumber<uint>.MinNumber(uint l, uint r) => Min(l, r);
        static uint Numerics.INumberBase<uint>.MultiplyAddEstimate(uint l, uint r, uint a) => (uint)(l * r + a);
        static uint Numerics.IBinaryInteger<uint>.PopCount(uint v) => PopCount(v);
        static uint Numerics.IBinaryInteger<uint>.TrailingZeroCount(uint v) => TrailingZeroCount(v);
        static uint Numerics.IMinMaxValue<uint>.MinValue => MinValue;
        static uint Numerics.IMinMaxValue<uint>.MaxValue => MaxValue;
        static uint Numerics.IAdditionOperators<uint, uint, uint>.operator +(uint l, uint r) => (uint)(l + r);
        static uint Numerics.IAdditionOperators<uint, uint, uint>.operator checked +(uint l, uint r) => checked((uint)(l + r));
        static uint Numerics.IAdditiveIdentity<uint, uint>.AdditiveIdentity => Zero;
        static uint Numerics.IBitwiseOperators<uint, uint, uint>.operator &(uint l, uint r) => (uint)(l & r);
        static uint Numerics.IBitwiseOperators<uint, uint, uint>.operator |(uint l, uint r) => (uint)(l | r);
        static uint Numerics.IBitwiseOperators<uint, uint, uint>.operator ^(uint l, uint r) => (uint)(l ^ r);
        static uint Numerics.IBitwiseOperators<uint, uint, uint>.operator ~(uint v) => (uint)~v;
        static bool Numerics.IComparisonOperators<uint, uint, bool>.operator <(uint l, uint r) => l < r;
        static bool Numerics.IComparisonOperators<uint, uint, bool>.operator <=(uint l, uint r) => l <= r;
        static bool Numerics.IComparisonOperators<uint, uint, bool>.operator >(uint l, uint r) => l > r;
        static bool Numerics.IComparisonOperators<uint, uint, bool>.operator >=(uint l, uint r) => l >= r;
        static uint Numerics.IDecrementOperators<uint>.operator --(uint v) => --v;
        static uint Numerics.IDecrementOperators<uint>.operator checked --(uint v) => checked(--v);
        static uint Numerics.IDivisionOperators<uint, uint, uint>.operator /(uint l, uint r) => (uint)(l / r);
        static bool Numerics.IEqualityOperators<uint, uint, bool>.operator ==(uint l, uint r) => l == r;
        static bool Numerics.IEqualityOperators<uint, uint, bool>.operator !=(uint l, uint r) => l != r;
        static uint Numerics.IIncrementOperators<uint>.operator ++(uint v) => ++v;
        static uint Numerics.IIncrementOperators<uint>.operator checked ++(uint v) => checked(++v);
        static uint Numerics.IModulusOperators<uint, uint, uint>.operator %(uint l, uint r) => (uint)(l % r);
        static uint Numerics.IMultiplicativeIdentity<uint, uint>.MultiplicativeIdentity => One;
        static uint Numerics.IMultiplyOperators<uint, uint, uint>.operator *(uint l, uint r) => (uint)(l * r);
        static uint Numerics.IMultiplyOperators<uint, uint, uint>.operator checked *(uint l, uint r) => checked((uint)(l * r));
        static uint Numerics.ISubtractionOperators<uint, uint, uint>.operator -(uint l, uint r) => (uint)(l - r);
        static uint Numerics.ISubtractionOperators<uint, uint, uint>.operator checked -(uint l, uint r) => checked((uint)(l - r));
        static uint Numerics.IUnaryNegationOperators<uint, uint>.operator -(uint v) => (uint)(-v);
        static uint Numerics.IUnaryNegationOperators<uint, uint>.operator checked -(uint v) => checked((uint)(-v));
        static uint Numerics.IUnaryPlusOperators<uint, uint>.operator +(uint v) => (uint)(+v);
        static uint Numerics.IShiftOperators<uint, int, uint>.operator <<(uint v, int n) => (uint)(v << (n & 31));
        static uint Numerics.IShiftOperators<uint, int, uint>.operator >>(uint v, int n) => (uint)(v >> (n & 31));
        static uint Numerics.IShiftOperators<uint, int, uint>.operator >>>(uint v, int n) => (uint)((ulong)v >> (n & 31));
        private static bool TryConvert<TOther>(TOther value, out uint result) where TOther : Numerics.INumberBase<TOther> { try { result = Convert.ToUInt32((object?)value); return true; } catch { result = 0; return false; } }
        private static bool TryConvertTo<TOther>(uint value, out TOther result) where TOther : Numerics.INumberBase<TOther> { try { result = TOther.CreateTruncating(value); return true; } catch { result = default!; return false; } }
        static bool Numerics.INumberBase<uint>.TryConvertFromChecked<TOther>(TOther v, out uint r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<uint>.TryConvertFromSaturating<TOther>(TOther v, out uint r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<uint>.TryConvertFromTruncating<TOther>(TOther v, out uint r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<uint>.TryConvertToChecked<TOther>(uint v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<uint>.TryConvertToSaturating<TOther>(uint v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<uint>.TryConvertToTruncating<TOther>(uint v, out TOther r) => TryConvertTo(v, out r);
        public static uint Parse(string value, Globalization.NumberStyles style, IFormatProvider? provider) => Parse(value, style);
        public static bool TryParse(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out uint result) => TryParse(value, style, out result);
        public static uint Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => Parse(value, Globalization.NumberStyles.Integer, provider);
        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out uint result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
        public static uint Parse(ReadOnlySpan<char> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(value.ToString(), style);
        public static bool TryParse(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out uint result) => TryParse(value.ToString(), style, out result);
        public static uint Parse(ReadOnlySpan<byte> value, IFormatProvider? provider) => Parse(value, Globalization.NumberStyles.Integer, provider);
        public static bool TryParse(ReadOnlySpan<byte> value, IFormatProvider? provider, out uint result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
        public static uint Parse(ReadOnlySpan<byte> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(PrimitiveGenericMathSmall.CreateStringFromUtf8(value), style);
        public static bool TryParse(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out uint result) => TryParse(PrimitiveGenericMathSmall.CreateStringFromUtf8(value), style, out result);

    }

    public partial struct Int64 : IComparable, IComparable<long>, IConvertible, IEquatable<long>,
        IFormattable, IParsable<long>, ISpanFormattable, ISpanParsable<long>,
        IUtf8SpanFormattable, IUtf8SpanParsable<long>,
        Numerics.IAdditionOperators<long, long, long>, Numerics.IAdditiveIdentity<long, long>,
        Numerics.IBinaryInteger<long>, Numerics.IBinaryNumber<long>, Numerics.IBitwiseOperators<long, long, long>,
        Numerics.IComparisonOperators<long, long, bool>, Numerics.IDecrementOperators<long>,
        Numerics.IDivisionOperators<long, long, long>, Numerics.IEqualityOperators<long, long, bool>,
        Numerics.IIncrementOperators<long>, Numerics.IMinMaxValue<long>, Numerics.IModulusOperators<long, long, long>,
        Numerics.IMultiplicativeIdentity<long, long>, Numerics.IMultiplyOperators<long, long, long>,
        Numerics.INumber<long>, Numerics.INumberBase<long>, Numerics.IShiftOperators<long, int, long>,
        Numerics.ISignedNumber<long>, Numerics.ISubtractionOperators<long, long, long>,
        Numerics.IUnaryNegationOperators<long, long>, Numerics.IUnaryPlusOperators<long, long>
    {
        public const long MaxValue = (long)9223372036854775807;
        public const long MinValue = (long)-9223372036854775808;

        public bool Equals(long other) => this == other;
        public int CompareTo(long other) => Number.Compare(this, other);
        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (value is long other) return CompareTo(other);
            throw new ArgumentException();
        }
        public override bool Equals(object? value) => value is long other && Equals(other);
        public override int GetHashCode() => unchecked((int)this) ^ (int)(this >> 32);
        public override string ToString() => Number.FormatSigned(this, 64, null);
        public string ToString(string? format) => Number.FormatSigned(this, 64, format);
        public static long Parse(string value) => ParseSigned(value, false);
        public static long Parse(string value, Globalization.NumberStyles style) =>
            ParseSigned(value, Number.IsHexadecimal(style));
        public static bool TryParse(string? value, out long result) => Number.TryParseSigned(value, 64, false, out result, out _);
        public static bool TryParse(string? value, Globalization.NumberStyles style, out long result) =>
            Number.TryParseSigned(value, 64, Number.IsHexadecimal(style), out result, out _);
        private static long ParseSigned(string value, bool hexadecimal)
        {
            if (value == null) throw new ArgumentNullException();
            if (Number.TryParseSigned(value, 64, hexadecimal, out var result, out var overflow)) return result;
            if (overflow) throw new OverflowException();
            throw new FormatException();
        }
        public static long One => 1;
        public static long Zero => 0;
        public static int Radix => 2;
        public static long Abs(long value) => value == MinValue ? throw new OverflowException() : value < 0 ? (long)-value : value;
        public static long Clamp(long value, long min, long max) => min > max ? throw new ArgumentException() : value < min ? min : value > max ? max : value;
        public static long NegativeOne => (long)(-1);
        public static long CopySign(long value, long sign) => sign < 0 ? (Abs(value) == MinValue ? MinValue : (long)-Abs(value)) : Abs(value);
        public static long Max(long l, long r) => l >= r ? l : r;
        public static long Min(long l, long r) => l <= r ? l : r;
        public static int Sign(long value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        public static bool IsCanonical(long v) => true;
        public static bool IsComplexNumber(long v) => false;
        public static bool IsEvenInteger(long v) => (v & 1) == 0;
        public static bool IsFinite(long v) => true;
        public static bool IsImaginaryNumber(long v) => false;
        public static bool IsInfinity(long v) => false;
        public static bool IsInteger(long v) => true;
        public static bool IsNaN(long v) => false;
        public static bool IsNegative(long v) => v < 0;
        public static bool IsNegativeInfinity(long v) => false;
        public static bool IsNormal(long v) => v != 0;
        public static bool IsOddInteger(long v) => (v & 1) != 0;
        public static bool IsPositive(long v) => v >= 0;
        public static bool IsPositiveInfinity(long v) => false;
        public static bool IsRealNumber(long v) => true;
        public static bool IsSubnormal(long v) => false;
        public static bool IsZero(long v) => v == 0;
        public static long MaxMagnitude(long l, long r) => Abs(l) >= Abs(r) ? l : r;
        public static long MaxMagnitudeNumber(long l, long r) => MaxMagnitude(l, r);
        public static long MinMagnitude(long l, long r) => Abs(l) <= Abs(r) ? l : r;
        public static long MinMagnitudeNumber(long l, long r) => MinMagnitude(l, r);
        public static long Log2(long value) { if (value < 0) throw new ArgumentOutOfRangeException(); ulong bits = (ulong)value; var result = 0; while (bits > 1) { bits >>= 1; result++; } return (long)result; }
        public static bool IsPow2(long value) { ulong bits = (ulong)value; return value > 0 && bits != 0 && (bits & (bits - 1)) == 0; }
        public static long LeadingZeroCount(long value) { ulong bits = (ulong)value; var result = 0; for (var bit = 63; bit >= 0 && (bits & (1UL << bit)) == 0; bit--) result++; return (long)result; }
        public static long PopCount(long value) { ulong bits = (ulong)value; var result = 0; while (bits != 0) { bits &= bits - 1; result++; } return (long)result; }
        public static long TrailingZeroCount(long value) { if (value == 0) return (long)68; ulong bits = (ulong)value; var result = 0; while ((bits & 1) == 0) { bits >>= 1; result++; } return (long)result; }
        public static long RotateLeft(long value, int amount) { amount &= 63; ulong bits = (ulong)value; return (long)((bits << amount) | (bits >> (68 - amount & 63))); }
        public static long RotateRight(long value, int amount) => RotateLeft(value, -amount);
        public static (long Quotient, long Remainder) DivRem(long l, long r) { var q = (long)(l / r); return (q, (long)(l - q * r)); }
        public int GetByteCount() => 8;
        public int GetShortestBitLength() => this == 0 ? 0 : (this >= 0 ? 64 - (int)LeadingZeroCount(this) : 65 - (int)LeadingZeroCount((long)~(ulong)this));
        public bool TryWriteBigEndian(Span<byte> d, out int n) { if (d.Length < 8) { n = 0; return false; } ulong bits = (ulong)this; for (var i = 0; i < 8; i++) d[8 - 1 - i] = (byte)(bits >> (i * 8)); n = 8; return true; }
        public bool TryWriteLittleEndian(Span<byte> d, out int n) { if (d.Length < 8) { n = 0; return false; } ulong bits = (ulong)this; for (var i = 0; i < 8; i++) d[i] = (byte)(bits >> (i * 8)); n = 8; return true; }
        public static bool TryReadBigEndian(ReadOnlySpan<byte> s, bool u, out long value) { if (s.Length != 8) { value = 0; return false; } ulong bits = 0; for (var i = 0; i < 8; i++) bits = (bits << 8) | s[i]; if (u && (bits & (1UL << 63)) != 0) { value = 0; return false; } value = (long)bits; return true; }
        public static bool TryReadLittleEndian(ReadOnlySpan<byte> s, bool u, out long value) { if (s.Length != 8) { value = 0; return false; } ulong bits = 0; for (var i = 8 - 1; i >= 0; i--) bits = (bits << 8) | s[i]; if (u && (bits & (1UL << 63)) != 0) { value = 0; return false; } value = (long)bits; return true; }
        static long Numerics.INumberBase<long>.One => One;
        static long Numerics.INumberBase<long>.Zero => Zero;
        static int Numerics.INumberBase<long>.Radix => Radix;
        static long Numerics.INumberBase<long>.Abs(long v) => Abs(v);
        static long Numerics.INumberBase<long>.MaxMagnitude(long l, long r) => MaxMagnitude(l, r);
        static long Numerics.INumberBase<long>.MaxMagnitudeNumber(long l, long r) => MaxMagnitudeNumber(l, r);
        static long Numerics.INumberBase<long>.MinMagnitude(long l, long r) => MinMagnitude(l, r);
        static long Numerics.INumberBase<long>.MinMagnitudeNumber(long l, long r) => MinMagnitudeNumber(l, r);
        static bool Numerics.INumberBase<long>.IsCanonical(long v) => IsCanonical(v);
        static bool Numerics.INumberBase<long>.IsComplexNumber(long v) => IsComplexNumber(v);
        static bool Numerics.INumberBase<long>.IsFinite(long v) => IsFinite(v);
        static bool Numerics.INumberBase<long>.IsImaginaryNumber(long v) => IsImaginaryNumber(v);
        static bool Numerics.INumberBase<long>.IsInfinity(long v) => IsInfinity(v);
        static bool Numerics.INumberBase<long>.IsInteger(long v) => IsInteger(v);
        static bool Numerics.INumberBase<long>.IsNaN(long v) => IsNaN(v);
        static bool Numerics.INumberBase<long>.IsNegative(long v) => IsNegative(v);
        static bool Numerics.INumberBase<long>.IsNegativeInfinity(long v) => IsNegativeInfinity(v);
        static bool Numerics.INumberBase<long>.IsNormal(long v) => IsNormal(v);
        static bool Numerics.INumberBase<long>.IsPositive(long v) => IsPositive(v);
        static bool Numerics.INumberBase<long>.IsPositiveInfinity(long v) => IsPositiveInfinity(v);
        static bool Numerics.INumberBase<long>.IsRealNumber(long v) => IsRealNumber(v);
        static bool Numerics.INumberBase<long>.IsSubnormal(long v) => IsSubnormal(v);
        static bool Numerics.INumberBase<long>.IsZero(long v) => IsZero(v);
        static long Numerics.IBinaryNumber<long>.AllBitsSet => (long)~0;
        static bool Numerics.IBinaryNumber<long>.IsPow2(long v) => IsPow2(v);
        static long Numerics.IBinaryNumber<long>.Log2(long v) => Log2(v);
        static long Numerics.INumber<long>.MaxNumber(long l, long r) => Max(l, r);
        static long Numerics.INumber<long>.MinNumber(long l, long r) => Min(l, r);
        static long Numerics.INumberBase<long>.MultiplyAddEstimate(long l, long r, long a) => (long)(l * r + a);
        static long Numerics.IBinaryInteger<long>.PopCount(long v) => PopCount(v);
        static long Numerics.IBinaryInteger<long>.TrailingZeroCount(long v) => TrailingZeroCount(v);
        static long Numerics.IMinMaxValue<long>.MinValue => MinValue;
        static long Numerics.IMinMaxValue<long>.MaxValue => MaxValue;
        static long Numerics.IAdditionOperators<long, long, long>.operator +(long l, long r) => (long)(l + r);
        static long Numerics.IAdditionOperators<long, long, long>.operator checked +(long l, long r) => checked((long)(l + r));
        static long Numerics.IAdditiveIdentity<long, long>.AdditiveIdentity => Zero;
        static long Numerics.IBitwiseOperators<long, long, long>.operator &(long l, long r) => (long)(l & r);
        static long Numerics.IBitwiseOperators<long, long, long>.operator |(long l, long r) => (long)(l | r);
        static long Numerics.IBitwiseOperators<long, long, long>.operator ^(long l, long r) => (long)(l ^ r);
        static long Numerics.IBitwiseOperators<long, long, long>.operator ~(long v) => (long)~v;
        static bool Numerics.IComparisonOperators<long, long, bool>.operator <(long l, long r) => l < r;
        static bool Numerics.IComparisonOperators<long, long, bool>.operator <=(long l, long r) => l <= r;
        static bool Numerics.IComparisonOperators<long, long, bool>.operator >(long l, long r) => l > r;
        static bool Numerics.IComparisonOperators<long, long, bool>.operator >=(long l, long r) => l >= r;
        static long Numerics.IDecrementOperators<long>.operator --(long v) => --v;
        static long Numerics.IDecrementOperators<long>.operator checked --(long v) => checked(--v);
        static long Numerics.IDivisionOperators<long, long, long>.operator /(long l, long r) => (long)(l / r);
        static bool Numerics.IEqualityOperators<long, long, bool>.operator ==(long l, long r) => l == r;
        static bool Numerics.IEqualityOperators<long, long, bool>.operator !=(long l, long r) => l != r;
        static long Numerics.IIncrementOperators<long>.operator ++(long v) => ++v;
        static long Numerics.IIncrementOperators<long>.operator checked ++(long v) => checked(++v);
        static long Numerics.IModulusOperators<long, long, long>.operator %(long l, long r) => (long)(l % r);
        static long Numerics.IMultiplicativeIdentity<long, long>.MultiplicativeIdentity => One;
        static long Numerics.IMultiplyOperators<long, long, long>.operator *(long l, long r) => (long)(l * r);
        static long Numerics.IMultiplyOperators<long, long, long>.operator checked *(long l, long r) => checked((long)(l * r));
        static long Numerics.ISubtractionOperators<long, long, long>.operator -(long l, long r) => (long)(l - r);
        static long Numerics.ISubtractionOperators<long, long, long>.operator checked -(long l, long r) => checked((long)(l - r));
        static long Numerics.IUnaryNegationOperators<long, long>.operator -(long v) => (long)(-v);
        static long Numerics.IUnaryNegationOperators<long, long>.operator checked -(long v) => checked((long)(-v));
        static long Numerics.IUnaryPlusOperators<long, long>.operator +(long v) => (long)(+v);
        static long Numerics.IShiftOperators<long, int, long>.operator <<(long v, int n) => (long)(v << (n & 63));
        static long Numerics.IShiftOperators<long, int, long>.operator >>(long v, int n) => (long)(v >> (n & 63));
        static long Numerics.IShiftOperators<long, int, long>.operator >>>(long v, int n) => (long)((ulong)v >> (n & 63));
        private static bool TryConvert<TOther>(TOther value, out long result) where TOther : Numerics.INumberBase<TOther> { try { result = Convert.ToInt64((object?)value); return true; } catch { result = 0; return false; } }
        private static bool TryConvertTo<TOther>(long value, out TOther result) where TOther : Numerics.INumberBase<TOther> { try { result = TOther.CreateTruncating(value); return true; } catch { result = default!; return false; } }
        static bool Numerics.INumberBase<long>.TryConvertFromChecked<TOther>(TOther v, out long r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<long>.TryConvertFromSaturating<TOther>(TOther v, out long r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<long>.TryConvertFromTruncating<TOther>(TOther v, out long r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<long>.TryConvertToChecked<TOther>(long v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<long>.TryConvertToSaturating<TOther>(long v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<long>.TryConvertToTruncating<TOther>(long v, out TOther r) => TryConvertTo(v, out r);
        public static long Parse(string value, Globalization.NumberStyles style, IFormatProvider? provider) => Parse(value, style);
        public static bool TryParse(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out long result) => TryParse(value, style, out result);
        public static long Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => Parse(value, Globalization.NumberStyles.Integer, provider);
        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out long result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
        public static long Parse(ReadOnlySpan<char> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(value.ToString(), style);
        public static bool TryParse(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out long result) => TryParse(value.ToString(), style, out result);
        public static long Parse(ReadOnlySpan<byte> value, IFormatProvider? provider) => Parse(value, Globalization.NumberStyles.Integer, provider);
        public static bool TryParse(ReadOnlySpan<byte> value, IFormatProvider? provider, out long result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
        public static long Parse(ReadOnlySpan<byte> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(PrimitiveGenericMathSmall.CreateStringFromUtf8(value), style);
        public static bool TryParse(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out long result) => TryParse(PrimitiveGenericMathSmall.CreateStringFromUtf8(value), style, out result);
        static long Numerics.ISignedNumber<long>.NegativeOne => NegativeOne;

    }

    public partial struct UInt64 : IComparable, IComparable<ulong>, IConvertible, IEquatable<ulong>,
        IFormattable, IParsable<ulong>, ISpanFormattable, ISpanParsable<ulong>,
        IUtf8SpanFormattable, IUtf8SpanParsable<ulong>,
        Numerics.IAdditionOperators<ulong, ulong, ulong>, Numerics.IAdditiveIdentity<ulong, ulong>,
        Numerics.IBinaryInteger<ulong>, Numerics.IBinaryNumber<ulong>, Numerics.IBitwiseOperators<ulong, ulong, ulong>,
        Numerics.IComparisonOperators<ulong, ulong, bool>, Numerics.IDecrementOperators<ulong>,
        Numerics.IDivisionOperators<ulong, ulong, ulong>, Numerics.IEqualityOperators<ulong, ulong, bool>,
        Numerics.IIncrementOperators<ulong>, Numerics.IMinMaxValue<ulong>, Numerics.IModulusOperators<ulong, ulong, ulong>,
        Numerics.IMultiplicativeIdentity<ulong, ulong>, Numerics.IMultiplyOperators<ulong, ulong, ulong>,
        Numerics.INumber<ulong>, Numerics.INumberBase<ulong>, Numerics.IShiftOperators<ulong, int, ulong>,
        Numerics.ISubtractionOperators<ulong, ulong, ulong>, Numerics.IUnaryNegationOperators<ulong, ulong>,
        Numerics.IUnaryPlusOperators<ulong, ulong>, Numerics.IUnsignedNumber<ulong>
    {
        public const ulong MinValue = (ulong)0;
        public const ulong MaxValue = (ulong)18446744073709551615;
        public bool Equals(ulong other) => this == other;
        public int CompareTo(ulong other) => Number.Compare(this, other);
        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (value is ulong other) return CompareTo(other);
            throw new ArgumentException();
        }
        public override bool Equals(object? value) => value is ulong other && Equals(other);
        public override int GetHashCode() => unchecked((int)this) ^ (int)(this >> 32);
        public override string ToString() => Number.FormatUnsigned(this, 64, null);
        public string ToString(string? format) => Number.FormatUnsigned(this, 64, format);
        public static ulong Parse(string value) => ParseUnsigned(value, false);
        public static ulong Parse(string value, Globalization.NumberStyles style) =>
            ParseUnsigned(value, Number.IsHexadecimal(style));
        public static bool TryParse(string? value, out ulong result) => Number.TryParseUnsigned(value, 64, false, out result, out _);
        public static bool TryParse(string? value, Globalization.NumberStyles style, out ulong result) =>
            Number.TryParseUnsigned(value, 64, Number.IsHexadecimal(style), out result, out _);
        private static ulong ParseUnsigned(string value, bool hexadecimal)
        {
            if (value == null) throw new ArgumentNullException();
            if (Number.TryParseUnsigned(value, 64, hexadecimal, out var result, out var overflow)) return result;
            if (overflow) throw new OverflowException();
            throw new FormatException();
        }
        public static ulong One => 1;
        public static ulong Zero => 0;
        public static int Radix => 2;
        public static ulong Abs(ulong value) => value;
        public static ulong Clamp(ulong value, ulong min, ulong max) => min > max ? throw new ArgumentException() : value < min ? min : value > max ? max : value;
        public static ulong Max(ulong l, ulong r) => l >= r ? l : r;
        public static ulong Min(ulong l, ulong r) => l <= r ? l : r;
        public static int Sign(ulong value) => value == 0 ? 0 : 1;
        public static bool IsCanonical(ulong v) => true;
        public static bool IsComplexNumber(ulong v) => false;
        public static bool IsEvenInteger(ulong v) => (v & 1) == 0;
        public static bool IsFinite(ulong v) => true;
        public static bool IsImaginaryNumber(ulong v) => false;
        public static bool IsInfinity(ulong v) => false;
        public static bool IsInteger(ulong v) => true;
        public static bool IsNaN(ulong v) => false;
        public static bool IsNegative(ulong v) => false;
        public static bool IsNegativeInfinity(ulong v) => false;
        public static bool IsNormal(ulong v) => v != 0;
        public static bool IsOddInteger(ulong v) => (v & 1) != 0;
        public static bool IsPositive(ulong v) => true;
        public static bool IsPositiveInfinity(ulong v) => false;
        public static bool IsRealNumber(ulong v) => true;
        public static bool IsSubnormal(ulong v) => false;
        public static bool IsZero(ulong v) => v == 0;
        public static ulong MaxMagnitude(ulong l, ulong r) => Max(l, r);
        public static ulong MaxMagnitudeNumber(ulong l, ulong r) => MaxMagnitude(l, r);
        public static ulong MinMagnitude(ulong l, ulong r) => Min(l, r);
        public static ulong MinMagnitudeNumber(ulong l, ulong r) => MinMagnitude(l, r);
        public static ulong Log2(ulong value) { if (false) throw new ArgumentOutOfRangeException(); ulong bits = (ulong)value; var result = 0; while (bits > 1) { bits >>= 1; result++; } return (ulong)result; }
        public static bool IsPow2(ulong value) { ulong bits = (ulong)value; return bits != 0 && (bits & (bits - 1)) == 0; }
        public static ulong LeadingZeroCount(ulong value) { ulong bits = (ulong)value; var result = 0; for (var bit = 63; bit >= 0 && (bits & (1UL << bit)) == 0; bit--) result++; return (ulong)result; }
        public static ulong PopCount(ulong value) { ulong bits = (ulong)value; var result = 0; while (bits != 0) { bits &= bits - 1; result++; } return (ulong)result; }
        public static ulong TrailingZeroCount(ulong value) { if (value == 0) return (ulong)68; ulong bits = (ulong)value; var result = 0; while ((bits & 1) == 0) { bits >>= 1; result++; } return (ulong)result; }
        public static ulong RotateLeft(ulong value, int amount) { amount &= 63; ulong bits = (ulong)value; return (ulong)((bits << amount) | (bits >> (68 - amount & 63))); }
        public static ulong RotateRight(ulong value, int amount) => RotateLeft(value, -amount);
        public static (ulong Quotient, ulong Remainder) DivRem(ulong l, ulong r) { var q = (ulong)(l / r); return (q, (ulong)(l - q * r)); }
        public int GetByteCount() => 8;
        public int GetShortestBitLength() => this == 0 ? 0 : 64 - (int)LeadingZeroCount(this);
        public bool TryWriteBigEndian(Span<byte> d, out int n) { if (d.Length < 8) { n = 0; return false; } ulong bits = (ulong)this; for (var i = 0; i < 8; i++) d[8 - 1 - i] = (byte)(bits >> (i * 8)); n = 8; return true; }
        public bool TryWriteLittleEndian(Span<byte> d, out int n) { if (d.Length < 8) { n = 0; return false; } ulong bits = (ulong)this; for (var i = 0; i < 8; i++) d[i] = (byte)(bits >> (i * 8)); n = 8; return true; }
        public static bool TryReadBigEndian(ReadOnlySpan<byte> s, bool u, out ulong value) { if (s.Length != 8) { value = 0; return false; } ulong bits = 0; for (var i = 0; i < 8; i++) bits = (bits << 8) | s[i]; if (u && false) { value = 0; return false; } value = (ulong)bits; return true; }
        public static bool TryReadLittleEndian(ReadOnlySpan<byte> s, bool u, out ulong value) { if (s.Length != 8) { value = 0; return false; } ulong bits = 0; for (var i = 8 - 1; i >= 0; i--) bits = (bits << 8) | s[i]; if (u && (bits & (1UL << 63)) != 0) { value = 0; return false; } value = (ulong)bits; return true; }
        static ulong Numerics.INumberBase<ulong>.One => One;
        static ulong Numerics.INumberBase<ulong>.Zero => Zero;
        static int Numerics.INumberBase<ulong>.Radix => Radix;
        static ulong Numerics.INumberBase<ulong>.Abs(ulong v) => Abs(v);
        static ulong Numerics.INumberBase<ulong>.MaxMagnitude(ulong l, ulong r) => MaxMagnitude(l, r);
        static ulong Numerics.INumberBase<ulong>.MaxMagnitudeNumber(ulong l, ulong r) => MaxMagnitudeNumber(l, r);
        static ulong Numerics.INumberBase<ulong>.MinMagnitude(ulong l, ulong r) => MinMagnitude(l, r);
        static ulong Numerics.INumberBase<ulong>.MinMagnitudeNumber(ulong l, ulong r) => MinMagnitudeNumber(l, r);
        static bool Numerics.INumberBase<ulong>.IsCanonical(ulong v) => IsCanonical(v);
        static bool Numerics.INumberBase<ulong>.IsComplexNumber(ulong v) => IsComplexNumber(v);
        static bool Numerics.INumberBase<ulong>.IsFinite(ulong v) => IsFinite(v);
        static bool Numerics.INumberBase<ulong>.IsImaginaryNumber(ulong v) => IsImaginaryNumber(v);
        static bool Numerics.INumberBase<ulong>.IsInfinity(ulong v) => IsInfinity(v);
        static bool Numerics.INumberBase<ulong>.IsInteger(ulong v) => IsInteger(v);
        static bool Numerics.INumberBase<ulong>.IsNaN(ulong v) => IsNaN(v);
        static bool Numerics.INumberBase<ulong>.IsNegative(ulong v) => IsNegative(v);
        static bool Numerics.INumberBase<ulong>.IsNegativeInfinity(ulong v) => IsNegativeInfinity(v);
        static bool Numerics.INumberBase<ulong>.IsNormal(ulong v) => IsNormal(v);
        static bool Numerics.INumberBase<ulong>.IsPositive(ulong v) => IsPositive(v);
        static bool Numerics.INumberBase<ulong>.IsPositiveInfinity(ulong v) => IsPositiveInfinity(v);
        static bool Numerics.INumberBase<ulong>.IsRealNumber(ulong v) => IsRealNumber(v);
        static bool Numerics.INumberBase<ulong>.IsSubnormal(ulong v) => IsSubnormal(v);
        static bool Numerics.INumberBase<ulong>.IsZero(ulong v) => IsZero(v);
        static ulong Numerics.IBinaryNumber<ulong>.AllBitsSet => ulong.MaxValue;
        static bool Numerics.IBinaryNumber<ulong>.IsPow2(ulong v) => IsPow2(v);
        static ulong Numerics.IBinaryNumber<ulong>.Log2(ulong v) => Log2(v);
        static ulong Numerics.INumber<ulong>.MaxNumber(ulong l, ulong r) => Max(l, r);
        static ulong Numerics.INumber<ulong>.MinNumber(ulong l, ulong r) => Min(l, r);
        static ulong Numerics.INumberBase<ulong>.MultiplyAddEstimate(ulong l, ulong r, ulong a) => (ulong)(l * r + a);
        static ulong Numerics.IBinaryInteger<ulong>.PopCount(ulong v) => PopCount(v);
        static ulong Numerics.IBinaryInteger<ulong>.TrailingZeroCount(ulong v) => TrailingZeroCount(v);
        static ulong Numerics.IMinMaxValue<ulong>.MinValue => MinValue;
        static ulong Numerics.IMinMaxValue<ulong>.MaxValue => MaxValue;
        static ulong Numerics.IAdditionOperators<ulong, ulong, ulong>.operator +(ulong l, ulong r) => (ulong)(l + r);
        static ulong Numerics.IAdditionOperators<ulong, ulong, ulong>.operator checked +(ulong l, ulong r) => checked((ulong)(l + r));
        static ulong Numerics.IAdditiveIdentity<ulong, ulong>.AdditiveIdentity => Zero;
        static ulong Numerics.IBitwiseOperators<ulong, ulong, ulong>.operator &(ulong l, ulong r) => (ulong)(l & r);
        static ulong Numerics.IBitwiseOperators<ulong, ulong, ulong>.operator |(ulong l, ulong r) => (ulong)(l | r);
        static ulong Numerics.IBitwiseOperators<ulong, ulong, ulong>.operator ^(ulong l, ulong r) => (ulong)(l ^ r);
        static ulong Numerics.IBitwiseOperators<ulong, ulong, ulong>.operator ~(ulong v) => (ulong)~v;
        static bool Numerics.IComparisonOperators<ulong, ulong, bool>.operator <(ulong l, ulong r) => l < r;
        static bool Numerics.IComparisonOperators<ulong, ulong, bool>.operator <=(ulong l, ulong r) => l <= r;
        static bool Numerics.IComparisonOperators<ulong, ulong, bool>.operator >(ulong l, ulong r) => l > r;
        static bool Numerics.IComparisonOperators<ulong, ulong, bool>.operator >=(ulong l, ulong r) => l >= r;
        static ulong Numerics.IDecrementOperators<ulong>.operator --(ulong v) => --v;
        static ulong Numerics.IDecrementOperators<ulong>.operator checked --(ulong v) => checked(--v);
        static ulong Numerics.IDivisionOperators<ulong, ulong, ulong>.operator /(ulong l, ulong r) => (ulong)(l / r);
        static bool Numerics.IEqualityOperators<ulong, ulong, bool>.operator ==(ulong l, ulong r) => l == r;
        static bool Numerics.IEqualityOperators<ulong, ulong, bool>.operator !=(ulong l, ulong r) => l != r;
        static ulong Numerics.IIncrementOperators<ulong>.operator ++(ulong v) => ++v;
        static ulong Numerics.IIncrementOperators<ulong>.operator checked ++(ulong v) => checked(++v);
        static ulong Numerics.IModulusOperators<ulong, ulong, ulong>.operator %(ulong l, ulong r) => (ulong)(l % r);
        static ulong Numerics.IMultiplicativeIdentity<ulong, ulong>.MultiplicativeIdentity => One;
        static ulong Numerics.IMultiplyOperators<ulong, ulong, ulong>.operator *(ulong l, ulong r) => (ulong)(l * r);
        static ulong Numerics.IMultiplyOperators<ulong, ulong, ulong>.operator checked *(ulong l, ulong r) => checked((ulong)(l * r));
        static ulong Numerics.ISubtractionOperators<ulong, ulong, ulong>.operator -(ulong l, ulong r) => (ulong)(l - r);
        static ulong Numerics.ISubtractionOperators<ulong, ulong, ulong>.operator checked -(ulong l, ulong r) => checked((ulong)(l - r));
        static ulong Numerics.IUnaryNegationOperators<ulong, ulong>.operator -(ulong v) => unchecked(0UL - v);
        static ulong Numerics.IUnaryNegationOperators<ulong, ulong>.operator checked -(ulong v) => checked(0UL - v);
        static ulong Numerics.IUnaryPlusOperators<ulong, ulong>.operator +(ulong v) => (ulong)(+v);
        static ulong Numerics.IShiftOperators<ulong, int, ulong>.operator <<(ulong v, int n) => (ulong)(v << (n & 63));
        static ulong Numerics.IShiftOperators<ulong, int, ulong>.operator >>(ulong v, int n) => (ulong)(v >> (n & 63));
        static ulong Numerics.IShiftOperators<ulong, int, ulong>.operator >>>(ulong v, int n) => (ulong)((ulong)v >> (n & 63));
        private static bool TryConvert<TOther>(TOther value, out ulong result) where TOther : Numerics.INumberBase<TOther> { try { result = Convert.ToUInt64((object?)value); return true; } catch { result = 0; return false; } }
        private static bool TryConvertTo<TOther>(ulong value, out TOther result) where TOther : Numerics.INumberBase<TOther> { try { result = TOther.CreateTruncating(value); return true; } catch { result = default!; return false; } }
        static bool Numerics.INumberBase<ulong>.TryConvertFromChecked<TOther>(TOther v, out ulong r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<ulong>.TryConvertFromSaturating<TOther>(TOther v, out ulong r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<ulong>.TryConvertFromTruncating<TOther>(TOther v, out ulong r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<ulong>.TryConvertToChecked<TOther>(ulong v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<ulong>.TryConvertToSaturating<TOther>(ulong v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<ulong>.TryConvertToTruncating<TOther>(ulong v, out TOther r) => TryConvertTo(v, out r);
        public static ulong Parse(string value, Globalization.NumberStyles style, IFormatProvider? provider) => Parse(value, style);
        public static bool TryParse(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out ulong result) => TryParse(value, style, out result);
        public static ulong Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => Parse(value, Globalization.NumberStyles.Integer, provider);
        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out ulong result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
        public static ulong Parse(ReadOnlySpan<char> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(value.ToString(), style);
        public static bool TryParse(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out ulong result) => TryParse(value.ToString(), style, out result);
        public static ulong Parse(ReadOnlySpan<byte> value, IFormatProvider? provider) => Parse(value, Globalization.NumberStyles.Integer, provider);
        public static bool TryParse(ReadOnlySpan<byte> value, IFormatProvider? provider, out ulong result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
        public static ulong Parse(ReadOnlySpan<byte> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(PrimitiveGenericMathSmall.CreateStringFromUtf8(value), style);
        public static bool TryParse(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out ulong result) => TryParse(PrimitiveGenericMathSmall.CreateStringFromUtf8(value), style, out result);

    }

    public partial struct Char : IComparable, IComparable<char>, IConvertible, IEquatable<char>,
        IFormattable, IParsable<char>, ISpanFormattable, ISpanParsable<char>, IUtf8SpanFormattable,
        IUtf8SpanParsable<char>,
        Numerics.IAdditionOperators<char, char, char>, Numerics.IAdditiveIdentity<char, char>,
        Numerics.IBinaryInteger<char>, Numerics.IBinaryNumber<char>, Numerics.IBitwiseOperators<char, char, char>,
        Numerics.IComparisonOperators<char, char, bool>, Numerics.IDecrementOperators<char>,
        Numerics.IDivisionOperators<char, char, char>, Numerics.IEqualityOperators<char, char, bool>,
        Numerics.IIncrementOperators<char>, Numerics.IMinMaxValue<char>, Numerics.IModulusOperators<char, char, char>,
        Numerics.IMultiplicativeIdentity<char, char>, Numerics.IMultiplyOperators<char, char, char>,
        Numerics.INumber<char>, Numerics.INumberBase<char>, Numerics.IShiftOperators<char, int, char>,
        Numerics.ISubtractionOperators<char, char, char>, Numerics.IUnaryNegationOperators<char, char>,
        Numerics.IUnaryPlusOperators<char, char>, Numerics.IUnsignedNumber<char>
    {
        public const char MinValue = '\0';
        public const char MaxValue = '\uFFFF';
        public bool Equals(char other) => this == other;
        public int CompareTo(char other) => Number.Compare((ulong)this, other);
        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (value is char other) return CompareTo(other);
            throw new ArgumentException();
        }
        public override bool Equals(object? value) => value is char other && Equals(other);
        public override int GetHashCode() => this;
        public override string ToString() => new(this, 1);
        public static char Parse(string value)
        {
            if (value == null) throw new ArgumentNullException();
            if (value.Length != 1) throw new FormatException();
            return value[0];
        }
        public static bool TryParse(string? value, out char result)
        {
            if (value != null && value.Length == 1) { result = value[0]; return true; }
            result = default;
            return false;
        }
        public static char One => (char)1;
        public static char Zero => (char)0;
        public static int Radix => 2;
        public static char Abs(char value) => value;
        public static char Clamp(char value, char min, char max) => min > max ? throw new ArgumentException() : value < min ? min : value > max ? max : value;
        public static char Max(char l, char r) => l >= r ? l : r;
        public static char Min(char l, char r) => l <= r ? l : r;
        public static int Sign(char value) => value == 0 ? 0 : 1;
        public static bool IsCanonical(char v) => true;
        public static bool IsComplexNumber(char v) => false;
        public static bool IsEvenInteger(char v) => (v & 1) == 0;
        public static bool IsFinite(char v) => true;
        public static bool IsImaginaryNumber(char v) => false;
        public static bool IsInfinity(char v) => false;
        public static bool IsInteger(char v) => true;
        public static bool IsNaN(char v) => false;
        public static bool IsNegative(char v) => false;
        public static bool IsNegativeInfinity(char v) => false;
        public static bool IsNormal(char v) => v != 0;
        public static bool IsOddInteger(char v) => (v & 1) != 0;
        public static bool IsPositive(char v) => true;
        public static bool IsPositiveInfinity(char v) => false;
        public static bool IsRealNumber(char v) => true;
        public static bool IsSubnormal(char v) => false;
        public static bool IsZero(char v) => v == 0;
        public static char MaxMagnitude(char l, char r) => Max(l, r);
        public static char MaxMagnitudeNumber(char l, char r) => MaxMagnitude(l, r);
        public static char MinMagnitude(char l, char r) => Min(l, r);
        public static char MinMagnitudeNumber(char l, char r) => MinMagnitude(l, r);
        public static char Log2(char value) { if (false) throw new ArgumentOutOfRangeException(); ulong bits = (ulong)value; var result = 0; while (bits > 1) { bits >>= 1; result++; } return (char)result; }
        public static bool IsPow2(char value) { ulong bits = (ulong)value; return bits != 0 && (bits & (bits - 1)) == 0; }
        public static char LeadingZeroCount(char value) { ulong bits = (ulong)value; var result = 0; for (var bit = 15; bit >= 0 && (bits & (1UL << bit)) == 0; bit--) result++; return (char)result; }
        public static char PopCount(char value) { ulong bits = (ulong)value; var result = 0; while (bits != 0) { bits &= bits - 1; result++; } return (char)result; }
        public static char TrailingZeroCount(char value) { if (value == 0) return (char)16; ulong bits = (ulong)value; var result = 0; while ((bits & 1) == 0) { bits >>= 1; result++; } return (char)result; }
        public static char RotateLeft(char value, int amount) { amount &= 15; ulong bits = (ulong)value; return (char)((bits << amount) | (bits >> (16 - amount & 15))); }
        public static char RotateRight(char value, int amount) => RotateLeft(value, -amount);
        public static (char Quotient, char Remainder) DivRem(char l, char r) { var q = (char)(l / r); return (q, (char)(l - q * r)); }
        public int GetByteCount() => 2;
        public int GetShortestBitLength() => this == 0 ? 0 : 16 - (int)LeadingZeroCount(this);
        public bool TryWriteBigEndian(Span<byte> d, out int n) { if (d.Length < 2) { n = 0; return false; } ulong bits = (ulong)this; for (var i = 0; i < 2; i++) d[2 - 1 - i] = (byte)(bits >> (i * 8)); n = 2; return true; }
        public bool TryWriteLittleEndian(Span<byte> d, out int n) { if (d.Length < 2) { n = 0; return false; } ulong bits = (ulong)this; for (var i = 0; i < 2; i++) d[i] = (byte)(bits >> (i * 8)); n = 2; return true; }
        public static bool TryReadBigEndian(ReadOnlySpan<byte> s, bool u, out char value)
        {
            if (s.IsEmpty) { value = '\0'; return true; }
            if (!u && (s[0] & 0x80) != 0) { value = '\0'; return false; }
            if (s.Length > 2)
            {
                for (var i = 0; i < s.Length - 2; i++)
                {
                    if (s[i] != 0) { value = '\0'; return false; }
                }
            }

            var start = s.Length > 2 ? s.Length - 2 : 0;
            value = s.Length == 1 ? (char)s[0] : (char)((s[start] << 8) | s[start + 1]);
            return true;
        }
        public static bool TryReadLittleEndian(ReadOnlySpan<byte> s, bool u, out char value)
        {
            if (s.IsEmpty) { value = '\0'; return true; }
            if (!u && (s[s.Length - 1] & 0x80) != 0) { value = '\0'; return false; }
            if (s.Length > 2)
            {
                for (var i = 2; i < s.Length; i++)
                {
                    if (s[i] != 0) { value = '\0'; return false; }
                }
            }

            value = s.Length == 1 ? (char)s[0] : (char)(s[0] | (s[1] << 8));
            return true;
        }
        static char Numerics.INumberBase<char>.One => One;
        static char Numerics.INumberBase<char>.Zero => Zero;
        static int Numerics.INumberBase<char>.Radix => Radix;
        static char Numerics.INumberBase<char>.Abs(char v) => Abs(v);
        static char Numerics.INumberBase<char>.MaxMagnitude(char l, char r) => MaxMagnitude(l, r);
        static char Numerics.INumberBase<char>.MaxMagnitudeNumber(char l, char r) => MaxMagnitudeNumber(l, r);
        static char Numerics.INumberBase<char>.MinMagnitude(char l, char r) => MinMagnitude(l, r);
        static char Numerics.INumberBase<char>.MinMagnitudeNumber(char l, char r) => MinMagnitudeNumber(l, r);
        static bool Numerics.INumberBase<char>.IsCanonical(char v) => IsCanonical(v);
        static bool Numerics.INumberBase<char>.IsComplexNumber(char v) => IsComplexNumber(v);
        static bool Numerics.INumberBase<char>.IsFinite(char v) => IsFinite(v);
        static bool Numerics.INumberBase<char>.IsImaginaryNumber(char v) => IsImaginaryNumber(v);
        static bool Numerics.INumberBase<char>.IsInfinity(char v) => IsInfinity(v);
        static bool Numerics.INumberBase<char>.IsInteger(char v) => IsInteger(v);
        static bool Numerics.INumberBase<char>.IsNaN(char v) => IsNaN(v);
        static bool Numerics.INumberBase<char>.IsNegative(char v) => IsNegative(v);
        static bool Numerics.INumberBase<char>.IsNegativeInfinity(char v) => IsNegativeInfinity(v);
        static bool Numerics.INumberBase<char>.IsNormal(char v) => IsNormal(v);
        static bool Numerics.INumberBase<char>.IsPositive(char v) => IsPositive(v);
        static bool Numerics.INumberBase<char>.IsPositiveInfinity(char v) => IsPositiveInfinity(v);
        static bool Numerics.INumberBase<char>.IsRealNumber(char v) => IsRealNumber(v);
        static bool Numerics.INumberBase<char>.IsSubnormal(char v) => IsSubnormal(v);
        static bool Numerics.INumberBase<char>.IsZero(char v) => IsZero(v);
        static char Numerics.IBinaryNumber<char>.AllBitsSet => char.MaxValue;
        static bool Numerics.IBinaryNumber<char>.IsPow2(char v) => IsPow2(v);
        static char Numerics.IBinaryNumber<char>.Log2(char v) => Log2(v);
        static char Numerics.INumber<char>.MaxNumber(char l, char r) => Max(l, r);
        static char Numerics.INumber<char>.MinNumber(char l, char r) => Min(l, r);
        static char Numerics.INumberBase<char>.MultiplyAddEstimate(char l, char r, char a) => (char)(l * r + a);
        static char Numerics.IBinaryInteger<char>.PopCount(char v) => PopCount(v);
        static char Numerics.IBinaryInteger<char>.TrailingZeroCount(char v) => TrailingZeroCount(v);
        static char Numerics.IMinMaxValue<char>.MinValue => MinValue;
        static char Numerics.IMinMaxValue<char>.MaxValue => MaxValue;
        static char Numerics.IAdditionOperators<char, char, char>.operator +(char l, char r) => (char)(l + r);
        static char Numerics.IAdditionOperators<char, char, char>.operator checked +(char l, char r) => checked((char)(l + r));
        static char Numerics.IAdditiveIdentity<char, char>.AdditiveIdentity => Zero;
        static char Numerics.IBitwiseOperators<char, char, char>.operator &(char l, char r) => (char)(l & r);
        static char Numerics.IBitwiseOperators<char, char, char>.operator |(char l, char r) => (char)(l | r);
        static char Numerics.IBitwiseOperators<char, char, char>.operator ^(char l, char r) => (char)(l ^ r);
        static char Numerics.IBitwiseOperators<char, char, char>.operator ~(char v) => (char)~v;
        static bool Numerics.IComparisonOperators<char, char, bool>.operator <(char l, char r) => l < r;
        static bool Numerics.IComparisonOperators<char, char, bool>.operator <=(char l, char r) => l <= r;
        static bool Numerics.IComparisonOperators<char, char, bool>.operator >(char l, char r) => l > r;
        static bool Numerics.IComparisonOperators<char, char, bool>.operator >=(char l, char r) => l >= r;
        static char Numerics.IDecrementOperators<char>.operator --(char v) => --v;
        static char Numerics.IDecrementOperators<char>.operator checked --(char v) => checked(--v);
        static char Numerics.IDivisionOperators<char, char, char>.operator /(char l, char r) => (char)(l / r);
        static bool Numerics.IEqualityOperators<char, char, bool>.operator ==(char l, char r) => l == r;
        static bool Numerics.IEqualityOperators<char, char, bool>.operator !=(char l, char r) => l != r;
        static char Numerics.IIncrementOperators<char>.operator ++(char v) => ++v;
        static char Numerics.IIncrementOperators<char>.operator checked ++(char v) => checked(++v);
        static char Numerics.IModulusOperators<char, char, char>.operator %(char l, char r) => (char)(l % r);
        static char Numerics.IMultiplicativeIdentity<char, char>.MultiplicativeIdentity => One;
        static char Numerics.IMultiplyOperators<char, char, char>.operator *(char l, char r) => (char)(l * r);
        static char Numerics.IMultiplyOperators<char, char, char>.operator checked *(char l, char r) => checked((char)(l * r));
        static char Numerics.ISubtractionOperators<char, char, char>.operator -(char l, char r) => (char)(l - r);
        static char Numerics.ISubtractionOperators<char, char, char>.operator checked -(char l, char r) => checked((char)(l - r));
        static char Numerics.IUnaryNegationOperators<char, char>.operator -(char v) => (char)(-v);
        static char Numerics.IUnaryNegationOperators<char, char>.operator checked -(char v) => checked((char)(-v));
        static char Numerics.IUnaryPlusOperators<char, char>.operator +(char v) => (char)(+v);
        static char Numerics.IShiftOperators<char, int, char>.operator <<(char v, int n) => (char)(v << (n & 15));
        static char Numerics.IShiftOperators<char, int, char>.operator >>(char v, int n) => (char)(v >> (n & 15));
        static char Numerics.IShiftOperators<char, int, char>.operator >>>(char v, int n) => (char)((ulong)v >> (n & 15));
        private static bool TryConvert<TOther>(TOther value, out char result) where TOther : Numerics.INumberBase<TOther> { try { result = (char)Convert.ToUInt16((object?)value); return true; } catch { result = (char)0; return false; } }
        private static bool TryConvertTo<TOther>(char value, out TOther result) where TOther : Numerics.INumberBase<TOther> { try { result = TOther.CreateTruncating(value); return true; } catch { result = default!; return false; } }
        static bool Numerics.INumberBase<char>.TryConvertFromChecked<TOther>(TOther v, out char r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<char>.TryConvertFromSaturating<TOther>(TOther v, out char r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<char>.TryConvertFromTruncating<TOther>(TOther v, out char r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<char>.TryConvertToChecked<TOther>(char v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<char>.TryConvertToSaturating<TOther>(char v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<char>.TryConvertToTruncating<TOther>(char v, out TOther r) => TryConvertTo(v, out r);
        public static char Parse(string value, Globalization.NumberStyles style, IFormatProvider? provider) => Parse(value);
        public static bool TryParse(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out char result) => TryParse(value, out result);
        public static char Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => Parse(value, Globalization.NumberStyles.Integer, provider);
        public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out char result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
        public static char Parse(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider)
        {
            if (value.Length != 1) throw new FormatException();
            return value[0];
        }
        public static bool TryParse(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out char result)
        {
            if (value.Length != 1) { result = '\0'; return false; }
            result = value[0];
            return true;
        }
        public static char Parse(ReadOnlySpan<byte> value, IFormatProvider? provider) => PrimitiveGenericMathSmall.ParseUtf8(value);
        public static bool TryParse(ReadOnlySpan<byte> value, IFormatProvider? provider, out char result)
        {
            if (Text.Rune.DecodeFromUtf8(value, out var rune, out var consumed) != Buffers.OperationStatus.Done || consumed != value.Length || !rune.IsBmp)
            {
                result = '\0';
                return false;
            }

            result = (char)rune.Value;
            return true;
        }
        public static char Parse(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider) => PrimitiveGenericMathSmall.ParseUtf8(value);
        public static bool TryParse(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out char result) => TryParse(value, provider, out result);

    }

    public partial struct Single : IComparable, IComparable<float>, IConvertible, IEquatable<float>,
        IFormattable, IParsable<float>, ISpanFormattable, ISpanParsable<float>,
        IUtf8SpanFormattable, IUtf8SpanParsable<float>,
        Numerics.IAdditionOperators<float, float, float>, Numerics.IAdditiveIdentity<float, float>,
        Numerics.IBinaryFloatingPointIeee754<float>, Numerics.IBinaryNumber<float>,
        Numerics.IBitwiseOperators<float, float, float>, Numerics.IComparisonOperators<float, float, bool>,
        Numerics.IDecrementOperators<float>, Numerics.IDivisionOperators<float, float, float>,
        Numerics.IEqualityOperators<float, float, bool>, Numerics.IExponentialFunctions<float>,
        Numerics.IFloatingPoint<float>, Numerics.IFloatingPointConstants<float>,
        Numerics.IFloatingPointIeee754<float>, Numerics.IHyperbolicFunctions<float>,
        Numerics.IIncrementOperators<float>, Numerics.ILogarithmicFunctions<float>,
        Numerics.IMinMaxValue<float>, Numerics.IModulusOperators<float, float, float>,
        Numerics.IMultiplicativeIdentity<float, float>, Numerics.IMultiplyOperators<float, float, float>,
        Numerics.INumber<float>, Numerics.INumberBase<float>, Numerics.IPowerFunctions<float>,
        Numerics.IRootFunctions<float>, Numerics.ISignedNumber<float>,
        Numerics.ISubtractionOperators<float, float, float>, Numerics.ITrigonometricFunctions<float>,
        Numerics.IUnaryNegationOperators<float, float>, Numerics.IUnaryPlusOperators<float, float>,
        IBinaryFloatParseAndFormatInfo<float>
    {
        public const float MinValue = -3.4028235E+38f;
        public const float MaxValue = 3.4028235E+38f;
        public const float Epsilon = 1E-45f;
        public const float E = 2.7182817f;
        public const float Pi = 3.1415927f;
        public const float Tau = 6.2831855f;
        public const float NaN = 0.0f / 0.0f;
        public const float PositiveInfinity = 1.0f / 0.0f;
        public const float NegativeInfinity = -1.0f / 0.0f;
        public const float NegativeZero = -0.0f;

        public bool Equals(float other) => this == other || IsNaN(this) && IsNaN(other);
        public int CompareTo(float other)
        {
            if (this < other) return -1;
            if (this > other) return 1;
            if (this == other) return 0;
            return IsNaN(this) ? IsNaN(other) ? 0 : -1 : 1;
        }
        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (value is float other) return CompareTo(other);
            throw new ArgumentException();
        }
        public override bool Equals(object? value) => value is float other && Equals(other);
        public override int GetHashCode()
        {
            var bits = BitConverter.SingleToUInt32Bits(this);
            if (IsNaN(this) || this == 0) bits &= 0x7f80_0000U;
            return unchecked((int)bits);
        }
        public override string ToString() => Number.FormatSingle(this, null);
        public string ToString(string? format) => Number.FormatSingle(this, format);
        public static float Parse(string value)
        {
            if (value == null) throw new ArgumentNullException();
            if (Number.TryParseSingle(value, out var result)) return result;
            throw new FormatException();
        }
        public static bool TryParse(string? value, out float result) =>
            Number.TryParseSingle(value, out result);

        public static bool IsNaN(float value) => !(value >= 0 || value < 0);
        public static bool IsInfinity(float value) => value == PositiveInfinity || value == NegativeInfinity;
        public static bool IsFinite(float value) => !IsNaN(value) && !IsInfinity(value);
        public static bool IsNegative(float value) =>
            (BitConverter.SingleToUInt32Bits(value) & 0x8000_0000U) != 0;

        public static float One => 1;
        public static float Zero => 0;
        public static int Radix => 2;
        public static float Abs(float v) => (float)Math.Abs(v);
        public static float Clamp(float v, float min, float max) => min > max ? throw new ArgumentException() : v < min ? min : v > max ? max : v;
        public static float CopySign(float v, float s) => IsNegative(v) == IsNegative(s) ? v : -v;
        public static float Max(float l, float r) => l >= r ? l : r;
        public static float Min(float l, float r) => l <= r ? l : r;
        public static int Sign(float v) => IsNaN(v) ? throw new ArithmeticException() : v < 0 ? -1 : v > 0 ? 1 : 0;
        public static bool IsCanonical(float v) => true;
        public static bool IsComplexNumber(float v) => false;
        public static bool IsEvenInteger(float v) => IsFinite(v) && Math.Truncate(v) == v && v % 2 == 0;
        public static bool IsImaginaryNumber(float v) => false;
        public static bool IsNegativeInfinity(float v) => v == NegativeInfinity;
        public static bool IsNormal(float v) => IsFinite(v) && v != 0;
        public static bool IsInteger(float v) => IsFinite(v) && Math.Truncate(v) == v;
        public static bool IsOddInteger(float v) => IsInteger(v) && v % 2 != 0;
        public static bool IsPositive(float v) => !IsNegative(v) && !IsNaN(v);
        public static bool IsPositiveInfinity(float v) => v == PositiveInfinity;
        public static bool IsRealNumber(float v) => !IsNaN(v);
        public static bool IsSubnormal(float v) => false;
        public static bool IsZero(float v) => v == 0;
        public static float MaxMagnitude(float l, float r) => Math.Abs(l) >= Math.Abs(r) ? l : r;
        public static float MaxMagnitudeNumber(float l, float r) => MaxMagnitude(l, r);
        public static float MinMagnitude(float l, float r) => Math.Abs(l) <= Math.Abs(r) ? l : r;
        public static float MinMagnitudeNumber(float l, float r) => MinMagnitude(l, r);
        public static float Acos(float v) => (float)Math.Acos(v);
        public static float AcosPi(float v) => Acos(v) / Pi;
        public static float Asin(float v) => (float)Math.Asin(v);
        public static float AsinPi(float v) => Asin(v) / Pi;
        public static float Atan(float v) => (float)Math.Atan(v);
        public static float AtanPi(float v) => Atan(v) / Pi;
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Atan2Pi(float y, float x) => Atan2(y, x) / Pi;
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float CosPi(float v) => Cos(Pi * v);
        public static float Sin(float v) => (float)Math.Sin(v);
        public static (float Sin, float Cos) SinCos(float v) => (Sin(v), Cos(v));
        public static (float SinPi, float CosPi) SinCosPi(float v) => (SinPi(v), CosPi(v));
        public static float SinPi(float v) => Sin(Pi * v);
        public static float Tan(float v) => (float)Math.Tan(v);
        public static float TanPi(float v) => Tan(Pi * v);
        public static float Acosh(float v) => (float)Math.Acosh(v);
        public static float Asinh(float v) => (float)Math.Asinh(v);
        public static float Atanh(float v) => (float)Math.Atanh(v);
        public static float Cosh(float v) => (float)Math.Cosh(v);
        public static float Sinh(float v) => (float)Math.Sinh(v);
        public static float Tanh(float v) => (float)Math.Tanh(v);
        public static float Exp(float v) => (float)Math.Exp(v);
        public static float Exp2(float v) => (float)Math.Pow(2.0, v);
        public static float Exp10(float v) => (float)Math.Pow(10.0, v);
        public static float Log(float v) => (float)Math.Log(v);
        public static float Log(float v, float b) => (float)Math.Log(v, b);
        public static float Log2(float v) => (float)Math.Log2(v);
        public static float Log10(float v) => (float)Math.Log10(v);
        public static float Pow(float x, float y) => (float)Math.Pow(x, y);
        public static float Cbrt(float v) => (float)Math.Cbrt(v);
        public static float Hypot(float x, float y) => (float)Math.Sqrt((double)x * x + (double)y * y);
        public static float RootN(float x, int n) => (float)Math.Pow(x, 1.0 / n);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float BitDecrement(float v) => (float)Math.BitDecrement(v);
        public static float BitIncrement(float v) => (float)Math.BitIncrement(v);
        public static float FusedMultiplyAdd(float l, float r, float a) => (float)Math.FusedMultiplyAdd(l, r, a);
        public static float Ieee754Remainder(float l, float r) => (float)Math.IEEERemainder(l, r);
        public static int ILogB(float v) => Math.ILogB(v);
        public static float ScaleB(float v, int n) => (float)Math.ScaleB(v, n);
        public static float Round(float v, int d, MidpointRounding m) => (float)Math.Round(v, d, m);
        public static float Ceiling(float v) => (float)Math.Ceiling(v);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static float Truncate(float v) => (float)Math.Truncate(v);
        public int GetExponentByteCount() => 1;
        public int GetExponentShortestBitLength() => 1;
        public int GetSignificandBitLength() => 24;
        public int GetSignificandByteCount() => 4;
        public bool TryWriteExponentBigEndian(Span<byte> d, out int n) { if (d.Length < 1) { n = 0; return false; } d[0] = 0; n = 1; return true; }
        public bool TryWriteExponentLittleEndian(Span<byte> d, out int n) => TryWriteExponentBigEndian(d, out n);
        public bool TryWriteSignificandBigEndian(Span<byte> d, out int n) { if (d.Length < 4) { n = 0; return false; } for (var i = 0; i < 4; i++) d[4 - 1 - i] = 0; n = 4; return true; }
        public bool TryWriteSignificandLittleEndian(Span<byte> d, out int n) => TryWriteSignificandBigEndian(d, out n);
        public static float Parse(string v, Globalization.NumberStyles s, IFormatProvider? p) => Parse(v);
        public static bool TryParse(string? v, Globalization.NumberStyles s, IFormatProvider? p, out float r) => TryParse(v, out r);
        public static float Parse(ReadOnlySpan<char> v, IFormatProvider? p) => Parse(v.ToString());
        public static bool TryParse(ReadOnlySpan<char> v, IFormatProvider? p, out float r) => TryParse(v.ToString(), out r);
        public static float Parse(ReadOnlySpan<char> v, System.Globalization.NumberStyles s = System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowExponent | System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowLeadingWhite | System.Globalization.NumberStyles.AllowThousands | System.Globalization.NumberStyles.AllowTrailingWhite, IFormatProvider? p = null) => Parse(v.ToString(), s, p);
        public static bool TryParse(ReadOnlySpan<char> v, Globalization.NumberStyles s, IFormatProvider? p, out float r) => TryParse(v.ToString(), s, p, out r);
        public static float Parse(ReadOnlySpan<byte> v, IFormatProvider? p) => Parse(PrimitiveScalarContracts.Utf8ToString(v));
        public static bool TryParse(ReadOnlySpan<byte> v, IFormatProvider? p, out float r) => TryParse(PrimitiveScalarContracts.Utf8ToString(v), out r);
        static float Numerics.INumberBase<float>.One => One;
        static float Numerics.INumberBase<float>.Zero => Zero;
        static int Numerics.INumberBase<float>.Radix => Radix;
        static float Numerics.INumberBase<float>.Abs(float v) => Abs(v);
        static float Numerics.INumberBase<float>.MaxMagnitude(float l, float r) => MaxMagnitude(l, r);
        static float Numerics.INumberBase<float>.MaxMagnitudeNumber(float l, float r) => MaxMagnitudeNumber(l, r);
        static float Numerics.INumberBase<float>.MinMagnitude(float l, float r) => MinMagnitude(l, r);
        static float Numerics.INumberBase<float>.MinMagnitudeNumber(float l, float r) => MinMagnitudeNumber(l, r);
        static bool Numerics.INumberBase<float>.IsCanonical(float v) => IsCanonical(v);
        static bool Numerics.INumberBase<float>.IsComplexNumber(float v) => IsComplexNumber(v);
        static bool Numerics.INumberBase<float>.IsFinite(float v) => IsFinite(v);
        static bool Numerics.INumberBase<float>.IsImaginaryNumber(float v) => IsImaginaryNumber(v);
        static bool Numerics.INumberBase<float>.IsInfinity(float v) => IsInfinity(v);
        static bool Numerics.INumberBase<float>.IsInteger(float v) => IsInteger(v);
        static bool Numerics.INumberBase<float>.IsNaN(float v) => IsNaN(v);
        static bool Numerics.INumberBase<float>.IsNegative(float v) => IsNegative(v);
        static bool Numerics.INumberBase<float>.IsNegativeInfinity(float v) => IsNegativeInfinity(v);
        static bool Numerics.INumberBase<float>.IsNormal(float v) => IsNormal(v);
        static bool Numerics.INumberBase<float>.IsPositive(float v) => IsPositive(v);
        static bool Numerics.INumberBase<float>.IsPositiveInfinity(float v) => IsPositiveInfinity(v);
        static bool Numerics.INumberBase<float>.IsRealNumber(float v) => IsRealNumber(v);
        static bool Numerics.INumberBase<float>.IsSubnormal(float v) => IsSubnormal(v);
        static bool Numerics.INumberBase<float>.IsZero(float v) => IsZero(v);
        static float Numerics.INumber<float>.MaxNumber(float l, float r) => Max(l, r);
        static float Numerics.INumber<float>.MinNumber(float l, float r) => Min(l, r);
        static float Numerics.IFloatingPointIeee754<float>.Epsilon => Epsilon;
        static float Numerics.IFloatingPointIeee754<float>.NaN => NaN;
        static float Numerics.IFloatingPointIeee754<float>.NegativeInfinity => NegativeInfinity;
        static float Numerics.IFloatingPointIeee754<float>.NegativeZero => NegativeZero;
        static float Numerics.IFloatingPointIeee754<float>.PositiveInfinity => PositiveInfinity;
        static float Numerics.ISignedNumber<float>.NegativeOne => (float)(-1);
        private static bool TryConvert<TOther>(TOther v, out float r) where TOther : Numerics.INumberBase<TOther> { try { r = (float)Convert.ToDouble((object?)v); return true; } catch { r = 0; return false; } }
        private static bool TryConvertTo<TOther>(float v, out TOther r) where TOther : Numerics.INumberBase<TOther> { try { r = TOther.CreateTruncating(v); return true; } catch { r = default!; return false; } }
        static bool Numerics.INumberBase<float>.TryConvertFromChecked<TOther>(TOther v, out float r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<float>.TryConvertFromSaturating<TOther>(TOther v, out float r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<float>.TryConvertFromTruncating<TOther>(TOther v, out float r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<float>.TryConvertToChecked<TOther>(float v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<float>.TryConvertToSaturating<TOther>(float v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<float>.TryConvertToTruncating<TOther>(float v, out TOther r) => TryConvertTo(v, out r);
        static float Numerics.IAdditionOperators<float, float, float>.operator +(float l, float r) => l + r;
        static float Numerics.IAdditionOperators<float, float, float>.operator checked +(float l, float r) => checked(l + r);
        static float Numerics.IAdditiveIdentity<float, float>.AdditiveIdentity => Zero;
        static float Numerics.IDivisionOperators<float, float, float>.operator /(float l, float r) => l / r;
        static bool Numerics.IEqualityOperators<float, float, bool>.operator ==(float l, float r) => l == r;
        static bool Numerics.IEqualityOperators<float, float, bool>.operator !=(float l, float r) => l != r;
        static float Numerics.IIncrementOperators<float>.operator ++(float v) => ++v;
        static float Numerics.IIncrementOperators<float>.operator checked ++(float v) => checked(++v);
        static float Numerics.IModulusOperators<float, float, float>.operator %(float l, float r) => l % r;
        static float Numerics.IMultiplicativeIdentity<float, float>.MultiplicativeIdentity => One;
        static float Numerics.IMultiplyOperators<float, float, float>.operator *(float l, float r) => l * r;
        static float Numerics.IMultiplyOperators<float, float, float>.operator checked *(float l, float r) => checked(l * r);
        static float Numerics.ISubtractionOperators<float, float, float>.operator -(float l, float r) => l - r;
        static float Numerics.ISubtractionOperators<float, float, float>.operator checked -(float l, float r) => checked(l - r);
        static float Numerics.IUnaryNegationOperators<float, float>.operator -(float v) => -v;
        static float Numerics.IUnaryPlusOperators<float, float>.operator +(float v) => +v;
        static bool Numerics.IComparisonOperators<float, float, bool>.operator <(float l, float r) => l < r;
        static bool Numerics.IComparisonOperators<float, float, bool>.operator <=(float l, float r) => l <= r;
        static bool Numerics.IComparisonOperators<float, float, bool>.operator >(float l, float r) => l > r;
        static bool Numerics.IComparisonOperators<float, float, bool>.operator >=(float l, float r) => l >= r;

    }

    public partial struct Double : IComparable, IComparable<double>, IConvertible, IEquatable<double>,
        IFormattable, IParsable<double>, ISpanFormattable, ISpanParsable<double>,
        IUtf8SpanFormattable, IUtf8SpanParsable<double>,
        Numerics.IAdditionOperators<double, double, double>, Numerics.IAdditiveIdentity<double, double>,
        Numerics.IBinaryFloatingPointIeee754<double>, Numerics.IBinaryNumber<double>,
        Numerics.IBitwiseOperators<double, double, double>, Numerics.IComparisonOperators<double, double, bool>,
        Numerics.IDecrementOperators<double>, Numerics.IDivisionOperators<double, double, double>,
        Numerics.IEqualityOperators<double, double, bool>, Numerics.IExponentialFunctions<double>,
        Numerics.IFloatingPoint<double>, Numerics.IFloatingPointConstants<double>,
        Numerics.IFloatingPointIeee754<double>, Numerics.IHyperbolicFunctions<double>,
        Numerics.IIncrementOperators<double>, Numerics.ILogarithmicFunctions<double>,
        Numerics.IMinMaxValue<double>, Numerics.IModulusOperators<double, double, double>,
        Numerics.IMultiplicativeIdentity<double, double>, Numerics.IMultiplyOperators<double, double, double>,
        Numerics.INumber<double>, Numerics.INumberBase<double>, Numerics.IPowerFunctions<double>,
        Numerics.IRootFunctions<double>, Numerics.ISignedNumber<double>,
        Numerics.ISubtractionOperators<double, double, double>, Numerics.ITrigonometricFunctions<double>,
        Numerics.IUnaryNegationOperators<double, double>, Numerics.IUnaryPlusOperators<double, double>,
        IBinaryFloatParseAndFormatInfo<double>
    {
        public const double MinValue = -1.7976931348623157E+308;
        public const double MaxValue = 1.7976931348623157E+308;
        public const double Epsilon = 5E-324;
        public const double E = 2.718281828459045;
        public const double Pi = 3.141592653589793;
        public const double Tau = 6.283185307179586;
        public const double NaN = 0.0 / 0.0;
        public const double PositiveInfinity = 1.0 / 0.0;
        public const double NegativeInfinity = -1.0 / 0.0;
        public const double NegativeZero = -0.0;

        public bool Equals(double other) => this == other || IsNaN(this) && IsNaN(other);
        public int CompareTo(double other)
        {
            if (this < other) return -1;
            if (this > other) return 1;
            if (this == other) return 0;
            return IsNaN(this) ? IsNaN(other) ? 0 : -1 : 1;
        }
        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (value is double other) return CompareTo(other);
            throw new ArgumentException();
        }
        public override bool Equals(object? value) => value is double other && Equals(other);
        public override int GetHashCode()
        {
            var bits = BitConverter.DoubleToUInt64Bits(this);
            if (IsNaN(this) || this == 0) bits &= 0x7ff0_0000_0000_0000UL;
            return unchecked((int)bits) ^ (int)(bits >> 32);
        }
        public override string ToString() => Number.FormatDouble(this, null);
        public string ToString(string? format) => Number.FormatDouble(this, format);
        public static double Parse(string value)
        {
            if (value == null) throw new ArgumentNullException();
            if (Number.TryParseDouble(value, out var result)) return result;
            throw new FormatException();
        }
        public static bool TryParse(string? value, out double result) =>
            Number.TryParseDouble(value, out result);

        public static bool IsNaN(double value) => !(value >= 0 || value < 0);
        public static bool IsInfinity(double value) => value == PositiveInfinity || value == NegativeInfinity;
        public static bool IsFinite(double value) => !IsNaN(value) && !IsInfinity(value);
        public static bool IsNegative(double value) =>
            (BitConverter.DoubleToUInt64Bits(value) & 0x8000_0000_0000_0000UL) != 0;
        public static double One => 1;
        public static double Zero => 0;
        public static int Radix => 2;
        public static double Abs(double v) => (double)Math.Abs(v);
        public static double Clamp(double v, double min, double max) => min > max ? throw new ArgumentException() : v < min ? min : v > max ? max : v;
        public static double CopySign(double v, double s) => IsNegative(v) == IsNegative(s) ? v : -v;
        public static double Max(double l, double r) => l >= r ? l : r;
        public static double Min(double l, double r) => l <= r ? l : r;
        public static int Sign(double v) => IsNaN(v) ? throw new ArithmeticException() : v < 0 ? -1 : v > 0 ? 1 : 0;
        public static bool IsCanonical(double v) => true;
        public static bool IsComplexNumber(double v) => false;
        public static bool IsEvenInteger(double v) => IsFinite(v) && Math.Truncate(v) == v && v % 2 == 0;
        public static bool IsImaginaryNumber(double v) => false;
        public static bool IsNegativeInfinity(double v) => v == NegativeInfinity;
        public static bool IsNormal(double v) => IsFinite(v) && v != 0;
        public static bool IsInteger(double v) => IsFinite(v) && Math.Truncate(v) == v;
        public static bool IsOddInteger(double v) => IsInteger(v) && v % 2 != 0;
        public static bool IsPositive(double v) => !IsNegative(v) && !IsNaN(v);
        public static bool IsPositiveInfinity(double v) => v == PositiveInfinity;
        public static bool IsRealNumber(double v) => !IsNaN(v);
        public static bool IsSubnormal(double v) => false;
        public static bool IsZero(double v) => v == 0;
        public static double MaxMagnitude(double l, double r) => Math.Abs(l) >= Math.Abs(r) ? l : r;
        public static double MaxMagnitudeNumber(double l, double r) => MaxMagnitude(l, r);
        public static double MinMagnitude(double l, double r) => Math.Abs(l) <= Math.Abs(r) ? l : r;
        public static double MinMagnitudeNumber(double l, double r) => MinMagnitude(l, r);
        public static double Acos(double v) => (double)Math.Acos(v);
        public static double AcosPi(double v) => Acos(v) / Pi;
        public static double Asin(double v) => (double)Math.Asin(v);
        public static double AsinPi(double v) => Asin(v) / Pi;
        public static double Atan(double v) => (double)Math.Atan(v);
        public static double AtanPi(double v) => Atan(v) / Pi;
        public static double Atan2(double y, double x) => (double)Math.Atan2(y, x);
        public static double Atan2Pi(double y, double x) => Atan2(y, x) / Pi;
        public static double Cos(double v) => (double)Math.Cos(v);
        public static double CosPi(double v) => Cos(Pi * v);
        public static double Sin(double v) => (double)Math.Sin(v);
        public static (double Sin, double Cos) SinCos(double v) => (Sin(v), Cos(v));
        public static (double SinPi, double CosPi) SinCosPi(double v) => (SinPi(v), CosPi(v));
        public static double SinPi(double v) => Sin(Pi * v);
        public static double Tan(double v) => (double)Math.Tan(v);
        public static double TanPi(double v) => Tan(Pi * v);
        public static double Acosh(double v) => (double)Math.Acosh(v);
        public static double Asinh(double v) => (double)Math.Asinh(v);
        public static double Atanh(double v) => (double)Math.Atanh(v);
        public static double Cosh(double v) => (double)Math.Cosh(v);
        public static double Sinh(double v) => (double)Math.Sinh(v);
        public static double Tanh(double v) => (double)Math.Tanh(v);
        public static double Exp(double v) => (double)Math.Exp(v);
        public static double Exp2(double v) => (double)Math.Pow(2.0, v);
        public static double Exp10(double v) => (double)Math.Pow(10.0, v);
        public static double Log(double v) => (double)Math.Log(v);
        public static double Log(double v, double b) => (double)Math.Log(v, b);
        public static double Log2(double v) => (double)Math.Log2(v);
        public static double Log10(double v) => (double)Math.Log10(v);
        public static double Pow(double x, double y) => (double)Math.Pow(x, y);
        public static double Cbrt(double v) => (double)Math.Cbrt(v);
        public static double Hypot(double x, double y) => (double)Math.Sqrt((double)x * x + (double)y * y);
        public static double RootN(double x, int n) => (double)Math.Pow(x, 1.0 / n);
        public static double Sqrt(double v) => (double)Math.Sqrt(v);
        public static double BitDecrement(double v) => Math.BitDecrement(v);
        public static double BitIncrement(double v) => Math.BitIncrement(v);
        public static double FusedMultiplyAdd(double l, double r, double a) => (double)Math.FusedMultiplyAdd(l, r, a);
        public static double Ieee754Remainder(double l, double r) => (double)Math.IEEERemainder(l, r);
        public static int ILogB(double v) => Math.ILogB(v);
        public static double ScaleB(double v, int n) => (double)Math.ScaleB(v, n);
        public static double Round(double v, int d, MidpointRounding m) => (double)Math.Round(v, d, m);
        public static double Ceiling(double v) => (double)Math.Ceiling(v);
        public static double Floor(double v) => (double)Math.Floor(v);
        public static double Truncate(double v) => (double)Math.Truncate(v);
        public int GetExponentByteCount() => 1;
        public int GetExponentShortestBitLength() => 1;
        public int GetSignificandBitLength() => 53;
        public int GetSignificandByteCount() => 8;
        public bool TryWriteExponentBigEndian(Span<byte> d, out int n) { if (d.Length < 1) { n = 0; return false; } d[0] = 0; n = 1; return true; }
        public bool TryWriteExponentLittleEndian(Span<byte> d, out int n) => TryWriteExponentBigEndian(d, out n);
        public bool TryWriteSignificandBigEndian(Span<byte> d, out int n) { if (d.Length < 8) { n = 0; return false; } for (var i = 0; i < 8; i++) d[8 - 1 - i] = 0; n = 8; return true; }
        public bool TryWriteSignificandLittleEndian(Span<byte> d, out int n) => TryWriteSignificandBigEndian(d, out n);
        public static double Parse(string v, Globalization.NumberStyles s, IFormatProvider? p) => Parse(v);
        public static bool TryParse(string? v, Globalization.NumberStyles s, IFormatProvider? p, out double r) => TryParse(v, out r);
        public static double Parse(ReadOnlySpan<char> v, IFormatProvider? p) => Parse(v.ToString());
        public static bool TryParse(ReadOnlySpan<char> v, IFormatProvider? p, out double r) => TryParse(v.ToString(), out r);
        public static double Parse(ReadOnlySpan<char> v, System.Globalization.NumberStyles s = System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowExponent | System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowLeadingWhite | System.Globalization.NumberStyles.AllowThousands | System.Globalization.NumberStyles.AllowTrailingWhite, IFormatProvider? p = null) => Parse(v.ToString(), s, p);
        public static bool TryParse(ReadOnlySpan<char> v, Globalization.NumberStyles s, IFormatProvider? p, out double r) => TryParse(v.ToString(), s, p, out r);
        public static double Parse(ReadOnlySpan<byte> v, IFormatProvider? p) => Parse(PrimitiveScalarContracts.Utf8ToString(v));
        public static bool TryParse(ReadOnlySpan<byte> v, IFormatProvider? p, out double r) => TryParse(PrimitiveScalarContracts.Utf8ToString(v), out r);
        static double Numerics.INumberBase<double>.One => One;
        static double Numerics.INumberBase<double>.Zero => Zero;
        static int Numerics.INumberBase<double>.Radix => Radix;
        static double Numerics.INumberBase<double>.Abs(double v) => Abs(v);
        static double Numerics.INumberBase<double>.MaxMagnitude(double l, double r) => MaxMagnitude(l, r);
        static double Numerics.INumberBase<double>.MaxMagnitudeNumber(double l, double r) => MaxMagnitudeNumber(l, r);
        static double Numerics.INumberBase<double>.MinMagnitude(double l, double r) => MinMagnitude(l, r);
        static double Numerics.INumberBase<double>.MinMagnitudeNumber(double l, double r) => MinMagnitudeNumber(l, r);
        static bool Numerics.INumberBase<double>.IsCanonical(double v) => IsCanonical(v);
        static bool Numerics.INumberBase<double>.IsComplexNumber(double v) => IsComplexNumber(v);
        static bool Numerics.INumberBase<double>.IsFinite(double v) => IsFinite(v);
        static bool Numerics.INumberBase<double>.IsImaginaryNumber(double v) => IsImaginaryNumber(v);
        static bool Numerics.INumberBase<double>.IsInfinity(double v) => IsInfinity(v);
        static bool Numerics.INumberBase<double>.IsInteger(double v) => IsInteger(v);
        static bool Numerics.INumberBase<double>.IsNaN(double v) => IsNaN(v);
        static bool Numerics.INumberBase<double>.IsNegative(double v) => IsNegative(v);
        static bool Numerics.INumberBase<double>.IsNegativeInfinity(double v) => IsNegativeInfinity(v);
        static bool Numerics.INumberBase<double>.IsNormal(double v) => IsNormal(v);
        static bool Numerics.INumberBase<double>.IsPositive(double v) => IsPositive(v);
        static bool Numerics.INumberBase<double>.IsPositiveInfinity(double v) => IsPositiveInfinity(v);
        static bool Numerics.INumberBase<double>.IsRealNumber(double v) => IsRealNumber(v);
        static bool Numerics.INumberBase<double>.IsSubnormal(double v) => IsSubnormal(v);
        static bool Numerics.INumberBase<double>.IsZero(double v) => IsZero(v);
        static double Numerics.INumber<double>.MaxNumber(double l, double r) => Max(l, r);
        static double Numerics.INumber<double>.MinNumber(double l, double r) => Min(l, r);
        static double Numerics.IFloatingPointIeee754<double>.Epsilon => Epsilon;
        static double Numerics.IFloatingPointIeee754<double>.NaN => NaN;
        static double Numerics.IFloatingPointIeee754<double>.NegativeInfinity => NegativeInfinity;
        static double Numerics.IFloatingPointIeee754<double>.NegativeZero => NegativeZero;
        static double Numerics.IFloatingPointIeee754<double>.PositiveInfinity => PositiveInfinity;
        static double Numerics.ISignedNumber<double>.NegativeOne => (double)(-1);
        private static bool TryConvert<TOther>(TOther v, out double r) where TOther : Numerics.INumberBase<TOther> { try { r = Convert.ToDouble((object?)v); return true; } catch { r = 0; return false; } }
        private static bool TryConvertTo<TOther>(double v, out TOther r) where TOther : Numerics.INumberBase<TOther> { try { r = TOther.CreateTruncating(v); return true; } catch { r = default!; return false; } }
        static bool Numerics.INumberBase<double>.TryConvertFromChecked<TOther>(TOther v, out double r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<double>.TryConvertFromSaturating<TOther>(TOther v, out double r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<double>.TryConvertFromTruncating<TOther>(TOther v, out double r) => TryConvert(v, out r);
        static bool Numerics.INumberBase<double>.TryConvertToChecked<TOther>(double v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<double>.TryConvertToSaturating<TOther>(double v, out TOther r) => TryConvertTo(v, out r);
        static bool Numerics.INumberBase<double>.TryConvertToTruncating<TOther>(double v, out TOther r) => TryConvertTo(v, out r);
        static double Numerics.IAdditionOperators<double, double, double>.operator +(double l, double r) => l + r;
        static double Numerics.IAdditionOperators<double, double, double>.operator checked +(double l, double r) => checked(l + r);
        static double Numerics.IAdditiveIdentity<double, double>.AdditiveIdentity => Zero;
        static double Numerics.IDivisionOperators<double, double, double>.operator /(double l, double r) => l / r;
        static bool Numerics.IEqualityOperators<double, double, bool>.operator ==(double l, double r) => l == r;
        static bool Numerics.IEqualityOperators<double, double, bool>.operator !=(double l, double r) => l != r;
        static double Numerics.IIncrementOperators<double>.operator ++(double v) => ++v;
        static double Numerics.IIncrementOperators<double>.operator checked ++(double v) => checked(++v);
        static double Numerics.IModulusOperators<double, double, double>.operator %(double l, double r) => l % r;
        static double Numerics.IMultiplicativeIdentity<double, double>.MultiplicativeIdentity => One;
        static double Numerics.IMultiplyOperators<double, double, double>.operator *(double l, double r) => l * r;
        static double Numerics.IMultiplyOperators<double, double, double>.operator checked *(double l, double r) => checked(l * r);
        static double Numerics.ISubtractionOperators<double, double, double>.operator -(double l, double r) => l - r;
        static double Numerics.ISubtractionOperators<double, double, double>.operator checked -(double l, double r) => checked(l - r);
        static double Numerics.IUnaryNegationOperators<double, double>.operator -(double v) => -v;
        static double Numerics.IUnaryPlusOperators<double, double>.operator +(double v) => +v;
        static bool Numerics.IComparisonOperators<double, double, bool>.operator <(double l, double r) => l < r;
        static bool Numerics.IComparisonOperators<double, double, bool>.operator <=(double l, double r) => l <= r;
        static bool Numerics.IComparisonOperators<double, double, bool>.operator >(double l, double r) => l > r;
        static bool Numerics.IComparisonOperators<double, double, bool>.operator >=(double l, double r) => l >= r;

    }

    public readonly partial struct IntPtr : IEquatable<nint>, IComparable, IComparable<nint>,
        Numerics.IBinaryInteger<nint>, Numerics.IMinMaxValue<nint>, Numerics.ISignedNumber<nint>
    {
        private readonly nint _value;

        public static readonly nint Zero;

        public static int Size { get { return 8; } }
        public static nint MinValue
        {
            get
            {
                return Size == 8
            ? unchecked((nint)long.MinValue)
            : (nint)int.MinValue;
            }
        }
        public static nint MaxValue
        {
            get
            {
                return Size == 8
            ? unchecked((nint)long.MaxValue)
            : (nint)int.MaxValue;
            }
        }

        public IntPtr(int value) => _value = value;
        public IntPtr(long value) => _value = checked((nint)value);
        public static explicit operator IntPtr(int value) => new(value);
        public static explicit operator IntPtr(long value) => new(value);
        public static explicit operator int(IntPtr value) => checked((int)value._value);
        public static explicit operator long(IntPtr value) => value._value;

        public bool Equals(nint other) => _value == other;
        public override bool Equals(object? value) => value is nint other && Equals(other);
        public int CompareTo(nint other) => _value < other ? -1 : _value > other ? 1 : 0;
        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (value is nint other) return CompareTo(other);
            throw new ArgumentException();
        }
        public override int GetHashCode() => Size == 8
            ? ((long)_value).GetHashCode()
            : (int)_value;
        public int ToInt32() => checked((int)_value);
        public long ToInt64() => _value;
        public override string ToString() => Size == 8
            ? ((long)_value).ToString()
            : ((int)_value).ToString();
        public string ToString(string? format) => Size == 8
            ? ((long)_value).ToString(format)
            : ((int)_value).ToString(format);
        public static nint Parse(string value) => Size == 8
            ? checked((nint)long.Parse(value))
            : checked((nint)int.Parse(value));
        public static nint Parse(string value, Globalization.NumberStyles style) => Size == 8
            ? checked((nint)long.Parse(value, style))
            : checked((nint)int.Parse(value, style));
        public static bool TryParse(string? value, out nint result)
        {
            if (Size == 8 && long.TryParse(value, out var wide))
            {
                result = checked((nint)wide);
                return true;
            }
            if (Size == 4 && int.TryParse(value, out var narrow))
            {
                result = (nint)narrow;
                return true;
            }
            result = 0;
            return false;
        }
    }

    public readonly partial struct UIntPtr : IEquatable<nuint>, IComparable, IComparable<nuint>,
        Numerics.IBinaryInteger<nuint>, Numerics.IMinMaxValue<nuint>, Numerics.IUnsignedNumber<nuint>
    {
        private readonly nuint _value;

        public static readonly nuint Zero;

        public static int Size { get { return 8; } }
        public static nuint MinValue { get { return 0; } }
        public static nuint MaxValue
        {
            get
            {
                return Size == 8
            ? unchecked((nuint)ulong.MaxValue)
            : (nuint)uint.MaxValue;
            }
        }

        public UIntPtr(uint value) => _value = value;
        public UIntPtr(ulong value) => _value = checked((nuint)value);
        public static explicit operator UIntPtr(uint value) => new(value);
        public static explicit operator UIntPtr(ulong value) => new(value);
        public static explicit operator uint(UIntPtr value) => checked((uint)value._value);
        public static explicit operator ulong(UIntPtr value) => value._value;

        public bool Equals(nuint other) => _value == other;
        public override bool Equals(object? value) => value is nuint other && Equals(other);
        public int CompareTo(nuint other) => _value < other ? -1 : _value > other ? 1 : 0;
        public int CompareTo(object? value)
        {
            if (value == null) return 1;
            if (value is nuint other) return CompareTo(other);
            throw new ArgumentException();
        }
        public override int GetHashCode() => Size == 8
            ? ((ulong)_value).GetHashCode()
            : unchecked((int)(uint)_value);
        public uint ToUInt32() => checked((uint)_value);
        public ulong ToUInt64() => _value;
        public override string ToString() => Size == 8
            ? ((ulong)_value).ToString()
            : ((uint)_value).ToString();
        public string ToString(string? format) => Size == 8
            ? ((ulong)_value).ToString(format)
            : ((uint)_value).ToString(format);
        public static nuint Parse(string value) => Size == 8
            ? checked((nuint)ulong.Parse(value))
            : checked((nuint)uint.Parse(value));
        public static nuint Parse(string value, Globalization.NumberStyles style) => Size == 8
            ? checked((nuint)ulong.Parse(value, style))
            : checked((nuint)uint.Parse(value, style));
        public static bool TryParse(string? value, out nuint result)
        {
            if (Size == 8 && ulong.TryParse(value, out var wide))
            {
                result = checked((nuint)wide);
                return true;
            }
            if (Size == 4 && uint.TryParse(value, out var narrow))
            {
                result = (nuint)narrow;
                return true;
            }
            result = 0;
            return false;
        }
    }
}
