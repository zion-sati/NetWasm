// Scalar generic-math and formatting contracts adapted from the corresponding
// dotnet/runtime System.Private.CoreLib primitive sources.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
// Pinned upstream runtime commit: 474ed963699fc29809015392738a15231aaae2de.

using System.Numerics;

namespace System;

internal enum PrimitiveIntegralConversionKind
{
    Checked,
    Saturating,
    Truncating,
}

internal static class PrimitiveIntegralConversions
{
    internal delegate bool PartialParser<T>(string value, out T result);

    internal static bool IsBuiltInNumber<TOther>() =>
        typeof(TOther) == typeof(byte) ||
        typeof(TOther) == typeof(sbyte) ||
        typeof(TOther) == typeof(short) ||
        typeof(TOther) == typeof(ushort) ||
        typeof(TOther) == typeof(int) ||
        typeof(TOther) == typeof(uint) ||
        typeof(TOther) == typeof(long) ||
        typeof(TOther) == typeof(ulong) ||
        typeof(TOther) == typeof(nint) ||
        typeof(TOther) == typeof(nuint) ||
        typeof(TOther) == typeof(char) ||
        typeof(TOther) == typeof(Half) ||
        typeof(TOther) == typeof(float) ||
        typeof(TOther) == typeof(double) ||
        typeof(TOther) == typeof(decimal) ||
        typeof(TOther) == typeof(Int128) ||
        typeof(TOther) == typeof(UInt128);

    internal static bool TryParsePartial<T>(string? value, Globalization.NumberStyles style, PartialParser<T> parser, out T result, out int consumed)
    {
        result = default!;
        if (value is null) { consumed = 0; return false; }

        var radix = (style & Globalization.NumberStyles.AllowHexSpecifier) != 0 ? 16 :
            (style & Globalization.NumberStyles.AllowBinarySpecifier) != 0 ? 2 : 10;
        var index = 0;
        if ((style & Globalization.NumberStyles.AllowLeadingWhite) != 0)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
        }
        if ((style & Globalization.NumberStyles.AllowParentheses) != 0 && index < value.Length && value[index] == '(') index++;
        if ((style & Globalization.NumberStyles.AllowCurrencySymbol) != 0 && index < value.Length && value[index] == '$') index++;
        if (radix == 10 && (style & Globalization.NumberStyles.AllowLeadingSign) != 0 && index < value.Length && value[index] is '+' or '-') index++;
        if ((style & Globalization.NumberStyles.AllowCurrencySymbol) != 0 && index < value.Length && value[index] == '$') index++;

        var sawDigit = false;
        var sawDecimalPoint = false;
        while (index < value.Length)
        {
            var character = value[index];
            var digit = character is >= '0' and <= '9' ? character - '0' :
                character is >= 'a' and <= 'f' ? character - 'a' + 10 :
                character is >= 'A' and <= 'F' ? character - 'A' + 10 : -1;
            if (digit >= 0 && digit < radix)
            {
                sawDigit = true;
                index++;
                continue;
            }
            if (radix == 10 && (style & Globalization.NumberStyles.AllowThousands) != 0 && character == ',')
            {
                index++;
                continue;
            }
            if (radix == 10 && !sawDecimalPoint && (style & Globalization.NumberStyles.AllowDecimalPoint) != 0 && character == '.')
            {
                sawDecimalPoint = true;
                index++;
                continue;
            }
            if (radix == 10 && sawDigit && (style & Globalization.NumberStyles.AllowExponent) != 0 && character is 'e' or 'E')
            {
                index++;
                if (index < value.Length && value[index] is '+' or '-') index++;
                while (index < value.Length && value[index] is >= '0' and <= '9') index++;
            }
            break;
        }

        if (!sawDigit) { consumed = 0; return false; }
        if ((style & Globalization.NumberStyles.AllowTrailingSign) != 0 && index < value.Length && value[index] is '+' or '-') index++;
        if ((style & Globalization.NumberStyles.AllowCurrencySymbol) != 0 && index < value.Length && value[index] == '$') index++;
        if ((style & Globalization.NumberStyles.AllowParentheses) != 0 && index < value.Length && value[index] == ')') index++;
        if ((style & Globalization.NumberStyles.AllowTrailingWhite) != 0)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
        }

        consumed = index;
        if (parser(value.Substring(0, consumed), out result)) return true;
        result = default!;
        consumed = 0;
        return false;
    }

    internal static bool TryConvert<T>(T value, int bits, bool signed,
        PrimitiveIntegralConversionKind kind, out ulong result)
    {
        var boxed = (object?)value;
        if (boxed is float single) return TryConvertFloating(single, bits, signed, kind, out result);
        if (boxed is double @double) return TryConvertFloating(@double, bits, signed, kind, out result);
        if (boxed is Half half) return TryConvertFloating((float)half, bits, signed, kind, out result);
        if (boxed is decimal money) return TryConvertDecimal(money, bits, signed, kind, out result);

        var sourceSigned = true;
        Int128 signedValue = default;
        UInt128 unsignedValue = default;
        switch (boxed)
        {
            case sbyte v: signedValue = v; break;
            case byte v: sourceSigned = false; unsignedValue = v; break;
            case short v: signedValue = v; break;
            case ushort v: sourceSigned = false; unsignedValue = v; break;
            case char v: sourceSigned = false; unsignedValue = v; break;
            case int v: signedValue = v; break;
            case uint v: sourceSigned = false; unsignedValue = v; break;
            case long v: signedValue = v; break;
            case ulong v: sourceSigned = false; unsignedValue = v; break;
            case nint v: signedValue = v; break;
            case nuint v: sourceSigned = false; unsignedValue = v; break;
            case Int128 v: signedValue = v; break;
            case UInt128 v: sourceSigned = false; unsignedValue = v; break;
            default: result = 0; return false;
        }

        var mask = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
        if (kind == PrimitiveIntegralConversionKind.Truncating)
        {
            result = (sourceSigned ? unchecked((ulong)signedValue) : unchecked((ulong)unsignedValue)) & mask;
            return true;
        }

        var signedMinimum = -(Int128.One << (bits - 1));
        var signedMaximum = (Int128.One << (bits - 1)) - 1;
        var unsignedMaximum = (UInt128.One << bits) - 1;
        if (signed)
        {
            if (sourceSigned)
            {
                if (signedValue < signedMinimum || signedValue > signedMaximum)
                {
                    if (kind == PrimitiveIntegralConversionKind.Checked) { result = 0; return false; }
                    result = (ulong)(signedValue < signedMinimum ? signedMinimum : signedMaximum) & mask;
                    return true;
                }
                result = unchecked((ulong)signedValue) & mask;
                return true;
            }

            if (unsignedValue > (UInt128)signedMaximum)
            {
                if (kind == PrimitiveIntegralConversionKind.Checked) { result = 0; return false; }
                result = (ulong)signedMaximum;
                return true;
            }
            result = (ulong)unsignedValue & mask;
            return true;
        }

        if (sourceSigned)
        {
            if (signedValue < 0)
            {
                if (kind == PrimitiveIntegralConversionKind.Checked) { result = 0; return false; }
                result = kind == PrimitiveIntegralConversionKind.Saturating ? 0 : unchecked((ulong)signedValue) & mask;
                return true;
            }
            if ((UInt128)signedValue > unsignedMaximum)
            {
                if (kind == PrimitiveIntegralConversionKind.Checked) { result = 0; return false; }
                result = (ulong)unsignedMaximum;
                return true;
            }
            result = (ulong)signedValue & mask;
            return true;
        }

        if (unsignedValue > unsignedMaximum)
        {
            if (kind == PrimitiveIntegralConversionKind.Checked) { result = 0; return false; }
            result = (ulong)unsignedMaximum;
            return true;
        }
        result = (ulong)unsignedValue & mask;
        return true;
    }

    internal static int Log10Signed(long value) => value < 0 ? throw new ArgumentOutOfRangeException() : Log10((ulong)value);

    internal static int Log10(ulong value)
    {
        var result = 0;
        while (value >= 10) { value /= 10; result++; }
        return result;
    }

    internal static object ToType(object value, Type conversionType, IFormatProvider? provider) =>
        conversionType == typeof(string) ? ((IConvertible)value).ToString(provider) :
        conversionType == typeof(bool) ? ((IConvertible)value).ToBoolean(provider) :
        conversionType == typeof(char) ? ((IConvertible)value).ToChar(provider) :
        conversionType == typeof(sbyte) ? ((IConvertible)value).ToSByte(provider) :
        conversionType == typeof(byte) ? ((IConvertible)value).ToByte(provider) :
        conversionType == typeof(short) ? ((IConvertible)value).ToInt16(provider) :
        conversionType == typeof(ushort) ? ((IConvertible)value).ToUInt16(provider) :
        conversionType == typeof(int) ? ((IConvertible)value).ToInt32(provider) :
        conversionType == typeof(uint) ? ((IConvertible)value).ToUInt32(provider) :
        conversionType == typeof(long) ? ((IConvertible)value).ToInt64(provider) :
        conversionType == typeof(ulong) ? ((IConvertible)value).ToUInt64(provider) :
        conversionType == typeof(float) ? ((IConvertible)value).ToSingle(provider) :
        conversionType == typeof(double) ? ((IConvertible)value).ToDouble(provider) :
        conversionType == typeof(decimal) ? ((IConvertible)value).ToDecimal(provider) :
        conversionType == typeof(object) ? value : throw new InvalidCastException();

    private static bool TryConvertFloating(double value, int bits, bool signed,
        PrimitiveIntegralConversionKind kind, out ulong result)
    {
        var mask = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
        if (double.IsNaN(value)) { result = 0; return kind != PrimitiveIntegralConversionKind.Checked; }
        double minimum = signed ? bits switch
        {
            8 => sbyte.MinValue,
            16 => short.MinValue,
            32 => int.MinValue,
            _ => -9.223372036854776E18,
        } : 0d;
        double maximum = signed ? bits switch
        {
            8 => sbyte.MaxValue,
            16 => short.MaxValue,
            32 => int.MaxValue,
            _ => 9.223372036854776E18,
        } : bits switch
        {
            8 => byte.MaxValue,
            16 => ushort.MaxValue,
            32 => uint.MaxValue,
            _ => 1.8446744073709552E19,
        };
        var truncated = Math.Truncate(value);
        if (kind == PrimitiveIntegralConversionKind.Checked && (double.IsInfinity(value) || truncated < minimum || truncated > maximum)) { result = 0; return false; }
        if (kind == PrimitiveIntegralConversionKind.Saturating)
        {
            if (double.IsNegativeInfinity(value) || truncated < minimum) truncated = minimum;
            if (double.IsPositiveInfinity(value) || truncated > maximum) truncated = maximum;
        }
        if (kind == PrimitiveIntegralConversionKind.Truncating && (truncated < minimum || truncated > maximum))
        {
            var modulus = bits == 64 ? 18446744073709551616d : 1UL << bits;
            truncated %= modulus;
            if (truncated < 0) truncated += modulus;
        }
        // A negative value here is in range for a signed target; converting it to
        // ulong directly saturates to zero, so it goes through long.
        result = (truncated < 0 ? unchecked((ulong)(long)truncated) : unchecked((ulong)truncated)) & mask;
        return true;
    }

    private static bool TryConvertDecimal(decimal value, int bits, bool signed,
        PrimitiveIntegralConversionKind kind, out ulong result)
    {
        var truncated = decimal.Truncate(value);
        decimal minimum = signed ? bits switch
        {
            8 => sbyte.MinValue,
            16 => short.MinValue,
            32 => int.MinValue,
            _ => long.MinValue,
        } : 0m;
        decimal maximum = signed ? bits switch
        {
            8 => sbyte.MaxValue,
            16 => short.MaxValue,
            32 => int.MaxValue,
            _ => long.MaxValue,
        } : bits switch
        {
            8 => byte.MaxValue,
            16 => ushort.MaxValue,
            32 => uint.MaxValue,
            _ => ulong.MaxValue,
        };
        if (kind == PrimitiveIntegralConversionKind.Checked && (truncated < minimum || truncated > maximum)) { result = 0; return false; }
        if (kind == PrimitiveIntegralConversionKind.Saturating) truncated = truncated < minimum ? minimum : truncated > maximum ? maximum : truncated;
        if (kind == PrimitiveIntegralConversionKind.Truncating && (truncated < minimum || truncated > maximum))
        {
            var modulus = bits == 64 ? 18446744073709551616m : (decimal)(1UL << bits);
            truncated %= modulus;
            if (truncated < 0) truncated += modulus;
        }
        result = signed ? unchecked((ulong)(long)truncated) : (ulong)truncated;
        return true;
    }
}

internal static class PrimitiveScalarContracts
{
    internal static string Utf8ToString(ReadOnlySpan<byte> value)
    {
        var characters = new char[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            characters[index] = (char)value[index];
        }

        return string.Create(characters);
    }

    internal static string? Format(ReadOnlySpan<char> format) =>
        format.Length == 0 ? null : string.Create(format.ToArray());

    internal static bool TryFormat(string text, Span<char> destination, out int charsWritten)
    {
        if (text.Length > destination.Length)
        {
            charsWritten = 0;
            return false;
        }

        var characters = text.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            destination[index] = characters[index];
        }

        charsWritten = characters.Length;
        return true;
    }
}

public partial struct Int16
{
    public string ToString(IFormatProvider? provider) => Number.FormatInt32(this, 0, null, provider);

    public string ToString(string? format, IFormatProvider? provider) =>
        Number.FormatInt32(this, 0x0000FFFF, format, provider);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
        IFormatProvider? provider = null) =>
        Number.TryFormatInt32(this, 0x0000FFFF, format, provider, destination, out charsWritten);

    public static short Parse(string value, IFormatProvider? provider) => Parse(value);

    public static bool TryParse(string? value, IFormatProvider? provider, out short result) =>
        TryParse(value, out result);

    static short Numerics.IMinMaxValue<short>.MinValue => MinValue;
    static short Numerics.IMinMaxValue<short>.MaxValue => MaxValue;
}

public partial struct UInt16
{
    public string ToString(IFormatProvider? provider) => Number.UInt32ToDecStr(this);

    public string ToString(string? format, IFormatProvider? provider) =>
        Number.FormatUInt32(this, format, provider);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
        IFormatProvider? provider = null) =>
        Number.TryFormatUInt32(this, format, provider, destination, out charsWritten);

    public static ushort Parse(string value, IFormatProvider? provider) => Parse(value);

    public static bool TryParse(string? value, IFormatProvider? provider, out ushort result) =>
        TryParse(value, out result);

    static ushort Numerics.IMinMaxValue<ushort>.MinValue => MinValue;
    static ushort Numerics.IMinMaxValue<ushort>.MaxValue => MaxValue;
}

public partial struct Int32
{
    public TypeCode GetTypeCode() => TypeCode.Int32;
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Number.TryFormatInt32(this, ~0, format, provider, destination, out bytesWritten);
    public static long BigMul(int left, int right) => (long)left * right;
    public static int Log10(int value) => PrimitiveIntegralConversions.Log10Signed(value);
    public static bool TryParse(ReadOnlySpan<char> value, out int result) => TryParse(value, null, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, out int result) => TryParse(value, null, out result);
    public static bool TryParsePartial(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out int result, out int consumed)
    {
        return PrimitiveIntegralConversions.TryParsePartial(value, style, (string text, out int parsed) => TryParse(text, style, provider, out parsed), out result, out consumed);
    }
    public static bool TryParsePartial(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out int result, out int consumed) => TryParsePartial(value.ToString(), style, provider, out result, out consumed);
    public static bool TryParsePartial(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out int result, out int consumed) => TryParsePartial(PrimitiveScalarContracts.Utf8ToString(value), style, provider, out result, out consumed);
    bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean(this);
    char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar(this);
    sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte(this);
    byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte(this);
    short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16(this);
    ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16(this);
    int IConvertible.ToInt32(IFormatProvider? provider) => this;
    uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32(this);
    long IConvertible.ToInt64(IFormatProvider? provider) => Convert.ToInt64(this);
    ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64(this);
    float IConvertible.ToSingle(IFormatProvider? provider) => Convert.ToSingle(this);
    double IConvertible.ToDouble(IFormatProvider? provider) => Convert.ToDouble(this);
    decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal(this);
    DateTime IConvertible.ToDateTime(IFormatProvider? provider) => throw new InvalidCastException();
    string IConvertible.ToString(IFormatProvider? provider) => ToString(provider);
    object IConvertible.ToType(Type conversionType, IFormatProvider? provider) => PrimitiveIntegralConversions.ToType(this, conversionType, provider);
}

public readonly partial struct IntPtr
{
    public unsafe IntPtr(void* value) => _value = (nint)value;
    public static unsafe explicit operator IntPtr(void* value) => new(value);
    public static unsafe explicit operator void*(IntPtr value) => (void*)value._value;
    public unsafe void* ToPointer() => (void*)_value;
    public string ToString(IFormatProvider? provider) => Size == 8 ? ((long)_value).ToString(provider) : ((int)_value).ToString(provider);
    public static nint Add(nint value, int offset) => new IntPtr(value._value + offset)._value;
    public static nint Subtract(nint value, int offset) => new IntPtr(value._value - offset)._value;
    public static nint BigMul(nint left, nint right, out nint low)
    {
        var product = (Int128)left._value * right._value;
        low = new IntPtr((long)product)._value;
        return new IntPtr((long)(product >> 64))._value;
    }
    public static nint Log10(nint value) => (nint)PrimitiveIntegralConversions.Log10Signed((long)value._value);
    public static nint Parse(ReadOnlySpan<byte> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(PrimitiveScalarContracts.Utf8ToString(value), style, provider);
    public static nint Parse(ReadOnlySpan<byte> value, IFormatProvider? provider) => Parse(value, System.Globalization.NumberStyles.Integer, provider);
    public static bool TryParse(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out nint result) => TryParse(PrimitiveScalarContracts.Utf8ToString(value), style, provider, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, IFormatProvider? provider, out nint result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, out nint result) => TryParse(value, null, out result);
    public static bool TryParse(ReadOnlySpan<char> value, out nint result) => TryParse(value, null, out result);
    public static bool TryParsePartial(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out nint result, out int consumed)
    {
        return PrimitiveIntegralConversions.TryParsePartial(value, style, (string text, out nint parsed) => TryParse(text, style, provider, out parsed), out result, out consumed);
    }
    public static bool TryParsePartial(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out nint result, out int consumed) => TryParsePartial(value.ToString(), style, provider, out result, out consumed);
    public static bool TryParsePartial(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out nint result, out int consumed) => TryParsePartial(PrimitiveScalarContracts.Utf8ToString(value), style, provider, out result, out consumed);
    public static bool operator ==(nint left, nint right) => left._value == right._value;
    public static bool operator !=(nint left, nint right) => left._value != right._value;
    public static nint operator +(nint value, int offset) => value._value + offset;
    public static nint operator -(nint value, int offset) => value._value - offset;
}

public readonly partial struct UIntPtr
{
    public unsafe UIntPtr(void* value) => _value = (nuint)value;
    public static unsafe explicit operator UIntPtr(void* value) => new(value);
    public static unsafe explicit operator void*(UIntPtr value) => (void*)value._value;
    public unsafe void* ToPointer() => (void*)_value;
    public string ToString(IFormatProvider? provider) => Size == 8 ? ((ulong)_value).ToString(provider) : ((uint)_value).ToString(provider);
    public static nuint Add(nuint value, int offset) => new UIntPtr(value._value + (nuint)offset)._value;
    public static nuint Subtract(nuint value, int offset) => new UIntPtr(value._value - (nuint)offset)._value;
    public static nuint BigMul(nuint left, nuint right, out nuint low)
    {
        var product = (UInt128)left._value * right._value;
        low = new UIntPtr((ulong)product)._value;
        return new UIntPtr((ulong)(product >> 64))._value;
    }
    public static nuint Log10(nuint value) => (nuint)PrimitiveIntegralConversions.Log10((ulong)value._value);
    public static nuint Parse(ReadOnlySpan<byte> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(PrimitiveScalarContracts.Utf8ToString(value), style, provider);
    public static nuint Parse(ReadOnlySpan<byte> value, IFormatProvider? provider) => Parse(value, System.Globalization.NumberStyles.Integer, provider);
    public static bool TryParse(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out nuint result) => TryParse(PrimitiveScalarContracts.Utf8ToString(value), style, provider, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, IFormatProvider? provider, out nuint result) => TryParse(value, Globalization.NumberStyles.Integer, provider, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, out nuint result) => TryParse(value, null, out result);
    public static bool TryParse(ReadOnlySpan<char> value, out nuint result) => TryParse(value, null, out result);
    public static bool TryParsePartial(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out nuint result, out int consumed)
    {
        return PrimitiveIntegralConversions.TryParsePartial(value, style, (string text, out nuint parsed) => TryParse(text, style, provider, out parsed), out result, out consumed);
    }
    public static bool TryParsePartial(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out nuint result, out int consumed) => TryParsePartial(value.ToString(), style, provider, out result, out consumed);
    public static bool TryParsePartial(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out nuint result, out int consumed) => TryParsePartial(PrimitiveScalarContracts.Utf8ToString(value), style, provider, out result, out consumed);
    public static bool operator ==(nuint left, nuint right) => left._value == right._value;
    public static bool operator !=(nuint left, nuint right) => left._value != right._value;
    public static nuint operator +(nuint value, int offset) => value._value + (nuint)offset;
    public static nuint operator -(nuint value, int offset) => value._value - (nuint)offset;
}

public partial struct UInt32
{
    public TypeCode GetTypeCode() => TypeCode.UInt32;
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Number.TryFormatUInt32(this, format, provider, destination, out bytesWritten);
    public static ulong BigMul(uint left, uint right) => (ulong)left * right;
    public static uint Log10(uint value) => (uint)PrimitiveIntegralConversions.Log10(value);
    public static bool TryParse(ReadOnlySpan<char> value, out uint result) => TryParse(value, null, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, out uint result) => TryParse(value, null, out result);
    public static bool TryParsePartial(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out uint result, out int consumed)
    {
        return PrimitiveIntegralConversions.TryParsePartial(value, style, (string text, out uint parsed) => TryParse(text, style, provider, out parsed), out result, out consumed);
    }
    public static bool TryParsePartial(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out uint result, out int consumed) => TryParsePartial(value.ToString(), style, provider, out result, out consumed);
    public static bool TryParsePartial(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out uint result, out int consumed) => TryParsePartial(PrimitiveScalarContracts.Utf8ToString(value), style, provider, out result, out consumed);
    bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean(this);
    char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar(this);
    sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte(this);
    byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte(this);
    short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16(this);
    ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16(this);
    int IConvertible.ToInt32(IFormatProvider? provider) => Convert.ToInt32(this);
    uint IConvertible.ToUInt32(IFormatProvider? provider) => this;
    long IConvertible.ToInt64(IFormatProvider? provider) => Convert.ToInt64(this);
    ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64(this);
    float IConvertible.ToSingle(IFormatProvider? provider) => Convert.ToSingle(this);
    double IConvertible.ToDouble(IFormatProvider? provider) => Convert.ToDouble(this);
    decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal(this);
    DateTime IConvertible.ToDateTime(IFormatProvider? provider) => throw new InvalidCastException();
    string IConvertible.ToString(IFormatProvider? provider) => ToString(provider);
    object IConvertible.ToType(Type conversionType, IFormatProvider? provider) => PrimitiveIntegralConversions.ToType(this, conversionType, provider);
}

public partial struct Int64
{
    public TypeCode GetTypeCode() => TypeCode.Int64;
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Number.TryFormatInt64(this, format, provider, destination, out bytesWritten);
    public static Int128 BigMul(long left, long right) => (Int128)left * right;
    public static long Log10(long value) => PrimitiveIntegralConversions.Log10Signed(value);
    public static bool TryParse(ReadOnlySpan<char> value, out long result) => TryParse(value, null, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, out long result) => TryParse(value, null, out result);
    public static bool TryParsePartial(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out long result, out int consumed)
    {
        return PrimitiveIntegralConversions.TryParsePartial(value, style, (string text, out long parsed) => TryParse(text, style, provider, out parsed), out result, out consumed);
    }
    public static bool TryParsePartial(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out long result, out int consumed) => TryParsePartial(value.ToString(), style, provider, out result, out consumed);
    public static bool TryParsePartial(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out long result, out int consumed) => TryParsePartial(PrimitiveScalarContracts.Utf8ToString(value), style, provider, out result, out consumed);
    bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean(this);
    char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar(this);
    sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte(this);
    byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte(this);
    short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16(this);
    ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16(this);
    int IConvertible.ToInt32(IFormatProvider? provider) => Convert.ToInt32(this);
    uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32(this);
    long IConvertible.ToInt64(IFormatProvider? provider) => this;
    ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64(this);
    float IConvertible.ToSingle(IFormatProvider? provider) => Convert.ToSingle(this);
    double IConvertible.ToDouble(IFormatProvider? provider) => Convert.ToDouble(this);
    decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal(this);
    DateTime IConvertible.ToDateTime(IFormatProvider? provider) => throw new InvalidCastException();
    string IConvertible.ToString(IFormatProvider? provider) => ToString(provider);
    object IConvertible.ToType(Type conversionType, IFormatProvider? provider) => PrimitiveIntegralConversions.ToType(this, conversionType, provider);
}

public partial struct UInt64
{
    public TypeCode GetTypeCode() => TypeCode.UInt64;
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Number.TryFormatUInt64(this, format, provider, destination, out bytesWritten);
    public static UInt128 BigMul(ulong left, ulong right) => (UInt128)left * right;
    public static ulong Log10(ulong value) => (ulong)PrimitiveIntegralConversions.Log10(value);
    public static bool TryParse(ReadOnlySpan<char> value, out ulong result) => TryParse(value, null, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, out ulong result) => TryParse(value, null, out result);
    public static bool TryParsePartial(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out ulong result, out int consumed)
    {
        return PrimitiveIntegralConversions.TryParsePartial(value, style, (string text, out ulong parsed) => TryParse(text, style, provider, out parsed), out result, out consumed);
    }
    public static bool TryParsePartial(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out ulong result, out int consumed) => TryParsePartial(value.ToString(), style, provider, out result, out consumed);
    public static bool TryParsePartial(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out ulong result, out int consumed) => TryParsePartial(PrimitiveScalarContracts.Utf8ToString(value), style, provider, out result, out consumed);
    bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean(this);
    char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar(this);
    sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte(this);
    byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte(this);
    short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16(this);
    ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16(this);
    int IConvertible.ToInt32(IFormatProvider? provider) => Convert.ToInt32(this);
    uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32(this);
    long IConvertible.ToInt64(IFormatProvider? provider) => Convert.ToInt64(this);
    ulong IConvertible.ToUInt64(IFormatProvider? provider) => this;
    float IConvertible.ToSingle(IFormatProvider? provider) => Convert.ToSingle(this);
    double IConvertible.ToDouble(IFormatProvider? provider) => Convert.ToDouble(this);
    decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal(this);
    DateTime IConvertible.ToDateTime(IFormatProvider? provider) => throw new InvalidCastException();
    string IConvertible.ToString(IFormatProvider? provider) => ToString(provider);
    object IConvertible.ToType(Type conversionType, IFormatProvider? provider) => PrimitiveIntegralConversions.ToType(this, conversionType, provider);
}

public partial struct Int32
{

    public string ToString(IFormatProvider? provider) => Number.FormatInt32(this, 0, null, provider);

    public string ToString(string? format, IFormatProvider? provider) =>
        Number.FormatInt32(this, ~0, format, provider);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
        IFormatProvider? provider = null) =>
        Number.TryFormatInt32(this, ~0, format, provider, destination, out charsWritten);

    public static int Parse(string value, IFormatProvider? provider) => Parse(value);

    public static bool TryParse(string? value, IFormatProvider? provider, out int result) =>
        TryParse(value, out result);
}

public partial struct UInt32
{

    public string ToString(IFormatProvider? provider) => Number.UInt32ToDecStr(this);

    public string ToString(string? format, IFormatProvider? provider) =>
        Number.FormatUInt32(this, format, provider);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
        IFormatProvider? provider = null) =>
        Number.TryFormatUInt32(this, format, provider, destination, out charsWritten);

    public static uint Parse(string value, IFormatProvider? provider) => Parse(value);

    public static bool TryParse(string? value, IFormatProvider? provider, out uint result) =>
        TryParse(value, out result);
}

public partial struct Int64
{
    public string ToString(IFormatProvider? provider) => Number.FormatInt64(this, null, provider);

    public string ToString(string? format, IFormatProvider? provider) =>
        Number.FormatInt64(this, format, provider);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
        IFormatProvider? provider = null) =>
        Number.TryFormatInt64(this, format, provider, destination, out charsWritten);

    public static long Parse(string value, IFormatProvider? provider) => Parse(value);

    public static bool TryParse(string? value, IFormatProvider? provider, out long result) =>
        TryParse(value, out result);
}

public partial struct UInt64
{
    internal static ReadOnlySpan<ulong> PowersOf10 =>
    [
        1,
        10,
        100,
        1_000,
        10_000,
        100_000,
        1_000_000,
        10_000_000,
        100_000_000,
        1_000_000_000,
        10_000_000_000,
        100_000_000_000,
        1_000_000_000_000,
        10_000_000_000_000,
        100_000_000_000_000,
        1_000_000_000_000_000,
        10_000_000_000_000_000,
        100_000_000_000_000_000,
        1_000_000_000_000_000_000,
        10_000_000_000_000_000_000,
    ];

    public string ToString(IFormatProvider? provider) => Number.UInt64ToDecStr(this);

    public string ToString(string? format, IFormatProvider? provider) =>
        Number.FormatUInt64(this, format, provider);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
        IFormatProvider? provider = null) =>
        Number.TryFormatUInt64(this, format, provider, destination, out charsWritten);

    public static ulong Parse(string value, IFormatProvider? provider) => Parse(value);

    public static bool TryParse(string? value, IFormatProvider? provider, out ulong result) =>
        TryParse(value, out result);
}

public partial struct Single
{
    public string ToString(IFormatProvider? provider) => ToString();

    public string ToString(string? format, IFormatProvider? provider) =>
        Number.FormatSingle(this, format);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
        IFormatProvider? provider = null) =>
        Number.TryFormatFloat(this, format, provider, destination, out charsWritten);

    public static float Parse(string value, IFormatProvider? provider) => Parse(value);

    public static bool TryParse(string? value, IFormatProvider? provider, out float result) =>
        TryParse(value, out result);

    public static bool IsPow2(float value)
    {
        var bits = BitConverter.SingleToUInt32Bits(value);
        if ((int)bits <= 0)
        {
            return false;
        }

        var exponent = bits & 0x7F80_0000U;
        var significand = bits & 0x007F_FFFFU;
        if (exponent == 0)
        {
            var setBits = 0;
            while (significand != 0)
            {
                significand &= significand - 1;
                setBits++;
            }

            return setBits == 1;
        }

        return exponent != 0x7F80_0000U && (bits & 0x007F_FFFFU) == 0;
    }

    static float Numerics.IBitwiseOperators<float, float, float>.operator &(float left, float right) =>
        BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(left) & BitConverter.SingleToUInt32Bits(right));

    static float Numerics.IBitwiseOperators<float, float, float>.operator |(float left, float right) =>
        BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(left) | BitConverter.SingleToUInt32Bits(right));

    static float Numerics.IBitwiseOperators<float, float, float>.operator ^(float left, float right) =>
        BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(left) ^ BitConverter.SingleToUInt32Bits(right));

    static float Numerics.IBitwiseOperators<float, float, float>.operator ~(float value) =>
        BitConverter.UInt32BitsToSingle(~BitConverter.SingleToUInt32Bits(value));

    static float Numerics.IDecrementOperators<float>.operator --(float value) => --value;
    static float Numerics.IMinMaxValue<float>.MinValue => MinValue;
    static float Numerics.IMinMaxValue<float>.MaxValue => MaxValue;

    public TypeCode GetTypeCode() => TypeCode.Single;

    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null)
    {
        return Number.TryFormatFloat(this, format, provider, destination, out bytesWritten);
    }

    private const Globalization.NumberStyles SingleParseStyle = Globalization.NumberStyles.AllowDecimalPoint | Globalization.NumberStyles.AllowExponent | Globalization.NumberStyles.AllowLeadingSign | Globalization.NumberStyles.AllowLeadingWhite | Globalization.NumberStyles.AllowThousands | Globalization.NumberStyles.AllowTrailingWhite;

    public static float Parse(string value, Globalization.NumberStyles style) => Parse(value.AsSpan(), style, null);

    public static float Parse(ReadOnlySpan<byte> value, System.Globalization.NumberStyles style = System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowExponent | System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowLeadingWhite | System.Globalization.NumberStyles.AllowThousands | System.Globalization.NumberStyles.AllowTrailingWhite, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.ParseFloat<byte, float>(value, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }
    public static bool TryParse(ReadOnlySpan<char> value, out float result) => TryParse(value, SingleParseStyle, null, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, out float result) => TryParse(value, SingleParseStyle, null, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out float result) => TryParseFloat(value, style, provider, false, out result, out _);
    public static bool TryParsePartial(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out float result, out int charsConsumed) => TryParseFloat(value.AsSpan(), style, provider, true, out result, out charsConsumed);
    public static bool TryParsePartial(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out float result, out int charsConsumed) => TryParseFloat(value, style, provider, true, out result, out charsConsumed);
    public static bool TryParsePartial(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out float result, out int bytesConsumed) => TryParseFloat(value, style, provider, true, out result, out bytesConsumed);

    private static bool TryParseFloat<TChar>(ReadOnlySpan<TChar> value, Globalization.NumberStyles style, IFormatProvider? provider, bool allowTrailingInvalid, out float result, out int consumed)
        where TChar : unmanaged
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        if (allowTrailingInvalid) style |= Number.AllowTrailingInvalidCharacters;
        return Number.TryParseFloat(value, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out consumed);
    }

    public static TInteger ConvertToInteger<TInteger>(float value) where TInteger : Numerics.IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);
    public static TInteger ConvertToIntegerNative<TInteger>(float value) where TInteger : Numerics.IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

    public static float ClampNative(float value, float min, float max) => min > max ? throw new ArgumentException() : MinNative(MaxNative(value, min), max);
    public static float MaxNative(float left, float right) => left > right ? left : right;
    public static float MinNative(float left, float right) => left < right ? left : right;
    public static float MaxNumber(float left, float right) => left != right ? !IsNaN(right) ? right < left ? left : right : left : IsNegative(right) ? left : right;
    public static float MinNumber(float left, float right) => left != right ? !IsNaN(right) ? left < right ? left : right : left : IsNegative(left) ? left : right;

    public static bool operator ==(float left, float right) => left == right;
    public static bool operator !=(float left, float right) => left != right;
    public static bool operator <(float left, float right) => left < right;
    public static bool operator <=(float left, float right) => left <= right;
    public static bool operator >(float left, float right) => left > right;
    public static bool operator >=(float left, float right) => left >= right;

    bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean(this);
    char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar(this);
    sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte(this);
    byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte(this);
    short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16(this);
    ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16(this);
    int IConvertible.ToInt32(IFormatProvider? provider) => Convert.ToInt32(this);
    uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32(this);
    long IConvertible.ToInt64(IFormatProvider? provider) => Convert.ToInt64(this);
    ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64(this);
    float IConvertible.ToSingle(IFormatProvider? provider) => this;
    double IConvertible.ToDouble(IFormatProvider? provider) => this;
    decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal(this);
    DateTime IConvertible.ToDateTime(IFormatProvider? provider) => throw new InvalidCastException();
    string IConvertible.ToString(IFormatProvider? provider) => ToString(provider);
    object IConvertible.ToType(Type conversionType, IFormatProvider? provider) =>
        conversionType == typeof(float) ? this :
        conversionType == typeof(string) ? ToString(provider) :
        conversionType == typeof(double) ? (double)this :
        conversionType == typeof(decimal) ? Convert.ToDecimal(this) :
        conversionType == typeof(sbyte) ? Convert.ToSByte(this) :
        conversionType == typeof(byte) ? Convert.ToByte(this) :
        conversionType == typeof(short) ? Convert.ToInt16(this) :
        conversionType == typeof(ushort) ? Convert.ToUInt16(this) :
        conversionType == typeof(int) ? Convert.ToInt32(this) :
        conversionType == typeof(uint) ? Convert.ToUInt32(this) :
        conversionType == typeof(long) ? Convert.ToInt64(this) :
        conversionType == typeof(ulong) ? Convert.ToUInt64(this) :
        throw new InvalidCastException();
}

public partial struct Double
{
    public string ToString(IFormatProvider? provider) => ToString();

    public string ToString(string? format, IFormatProvider? provider) =>
        Number.FormatDouble(this, format);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
        IFormatProvider? provider = null) =>
        Number.TryFormatFloat(this, format, provider, destination, out charsWritten);

    public static double Parse(string value, IFormatProvider? provider) => Parse(value);

    public static bool TryParse(string? value, IFormatProvider? provider, out double result) =>
        TryParse(value, out result);

    public static bool IsPow2(double value)
    {
        var bits = BitConverter.DoubleToUInt64Bits(value);
        if ((long)bits <= 0)
        {
            return false;
        }

        var exponent = bits & 0x7FF0_0000_0000_0000UL;
        var significand = bits & 0x000F_FFFF_FFFF_FFFFUL;
        if (exponent == 0)
        {
            var setBits = 0;
            while (significand != 0)
            {
                significand &= significand - 1;
                setBits++;
            }

            return setBits == 1;
        }

        return exponent != 0x7FF0_0000_0000_0000UL && (bits & 0x000F_FFFF_FFFF_FFFFUL) == 0;
    }

    static double Numerics.IBitwiseOperators<double, double, double>.operator &(double left, double right) =>
        BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(left) & BitConverter.DoubleToUInt64Bits(right));

    static double Numerics.IBitwiseOperators<double, double, double>.operator |(double left, double right) =>
        BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(left) | BitConverter.DoubleToUInt64Bits(right));

    static double Numerics.IBitwiseOperators<double, double, double>.operator ^(double left, double right) =>
        BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(left) ^ BitConverter.DoubleToUInt64Bits(right));

    static double Numerics.IBitwiseOperators<double, double, double>.operator ~(double value) =>
        BitConverter.UInt64BitsToDouble(~BitConverter.DoubleToUInt64Bits(value));

    static double Numerics.IDecrementOperators<double>.operator --(double value) => --value;
    static double Numerics.IMinMaxValue<double>.MinValue => MinValue;
    static double Numerics.IMinMaxValue<double>.MaxValue => MaxValue;

    public TypeCode GetTypeCode() => TypeCode.Double;

    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Number.TryFormatFloat(this, format, provider, destination, out bytesWritten);

    private const Globalization.NumberStyles DoubleParseStyle = Globalization.NumberStyles.AllowDecimalPoint | Globalization.NumberStyles.AllowExponent | Globalization.NumberStyles.AllowLeadingSign | Globalization.NumberStyles.AllowLeadingWhite | Globalization.NumberStyles.AllowThousands | Globalization.NumberStyles.AllowTrailingWhite;

    public static double Parse(string value, Globalization.NumberStyles style) => Parse(value.AsSpan(), style, null);

    public static double Parse(ReadOnlySpan<byte> value, System.Globalization.NumberStyles style = System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowExponent | System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowLeadingWhite | System.Globalization.NumberStyles.AllowThousands | System.Globalization.NumberStyles.AllowTrailingWhite, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.ParseFloat<byte, double>(value, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }
    public static bool TryParse(ReadOnlySpan<char> value, out double result) => TryParse(value, DoubleParseStyle, null, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, out double result) => TryParse(value, DoubleParseStyle, null, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out double result) => TryParseDouble(value, style, provider, false, out result, out _);
    public static bool TryParsePartial(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out double result, out int charsConsumed) => TryParseDouble(value.AsSpan(), style, provider, true, out result, out charsConsumed);
    public static bool TryParsePartial(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out double result, out int charsConsumed) => TryParseDouble(value, style, provider, true, out result, out charsConsumed);
    public static bool TryParsePartial(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out double result, out int bytesConsumed) => TryParseDouble(value, style, provider, true, out result, out bytesConsumed);

    private static bool TryParseDouble<TChar>(ReadOnlySpan<TChar> value, Globalization.NumberStyles style, IFormatProvider? provider, bool allowTrailingInvalid, out double result, out int consumed)
        where TChar : unmanaged
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        if (allowTrailingInvalid) style |= Number.AllowTrailingInvalidCharacters;
        return Number.TryParseFloat(value, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out consumed);
    }

    public static TInteger ConvertToInteger<TInteger>(double value) where TInteger : Numerics.IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);
    public static TInteger ConvertToIntegerNative<TInteger>(double value) where TInteger : Numerics.IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

    public static double ClampNative(double value, double min, double max) => min > max ? throw new ArgumentException() : MinNative(MaxNative(value, min), max);
    public static double MaxNative(double left, double right) => left > right ? left : right;
    public static double MinNative(double left, double right) => left < right ? left : right;
    public static double MaxNumber(double left, double right) => left != right ? !IsNaN(right) ? right < left ? left : right : left : IsNegative(right) ? left : right;
    public static double MinNumber(double left, double right) => left != right ? !IsNaN(right) ? left < right ? left : right : left : IsNegative(left) ? left : right;
    public static double Lerp(double value1, double value2, double amount) => MultiplyAddEstimate(value1, 1D - amount, value2 * amount);
    public static double LogP1(double value) => Math.Log(1D + value);
    public static double Log2P1(double value) => Math.Log2(1D + value);
    public static double Log10P1(double value) => Math.Log10(1D + value);
    public static double MultiplyAddEstimate(double left, double right, double addend) => Math.FusedMultiplyAdd(left, right, addend);
    public static double ReciprocalEstimate(double value) => Math.ReciprocalEstimate(value);
    public static double ReciprocalSqrtEstimate(double value) => Math.ReciprocalSqrtEstimate(value);
    public static double Round(double value) => Math.Round(value);
    public static double Round(double value, int digits) => Math.Round(value, digits);
    public static double Round(double value, MidpointRounding mode) => Math.Round(value, mode);
    public static bool operator ==(double left, double right) => left == right;
    public static bool operator !=(double left, double right) => left != right;
    public static bool operator <(double left, double right) => left < right;
    public static bool operator <=(double left, double right) => left <= right;
    public static bool operator >(double left, double right) => left > right;
    public static bool operator >=(double left, double right) => left >= right;

    bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean(this);
    char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar(this);
    sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte(this);
    byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte(this);
    short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16(this);
    ushort IConvertible.ToUInt16(IFormatProvider? provider) => Convert.ToUInt16(this);
    int IConvertible.ToInt32(IFormatProvider? provider) => Convert.ToInt32(this);
    uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32(this);
    long IConvertible.ToInt64(IFormatProvider? provider) => Convert.ToInt64(this);
    ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64(this);
    float IConvertible.ToSingle(IFormatProvider? provider) => (float)this;
    double IConvertible.ToDouble(IFormatProvider? provider) => this;
    decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal(this);
    DateTime IConvertible.ToDateTime(IFormatProvider? provider) => throw new InvalidCastException();
    string IConvertible.ToString(IFormatProvider? provider) => ToString(provider);
    object IConvertible.ToType(Type conversionType, IFormatProvider? provider) =>
        conversionType == typeof(double) ? this :
        conversionType == typeof(string) ? ToString(provider) :
        conversionType == typeof(float) ? (float)this :
        conversionType == typeof(decimal) ? Convert.ToDecimal(this) :
        conversionType == typeof(sbyte) ? Convert.ToSByte(this) :
        conversionType == typeof(byte) ? Convert.ToByte(this) :
        conversionType == typeof(short) ? Convert.ToInt16(this) :
        conversionType == typeof(ushort) ? Convert.ToUInt16(this) :
        conversionType == typeof(int) ? Convert.ToInt32(this) :
        conversionType == typeof(uint) ? Convert.ToUInt32(this) :
        conversionType == typeof(long) ? Convert.ToInt64(this) :
        conversionType == typeof(ulong) ? Convert.ToUInt64(this) :
        throw new InvalidCastException();
}

public partial struct Single
{
    // These bit-layout helpers are internal runtime conveniences used by the
    // Half/BFloat16 conversion code.  Keep them in the support partial so the
    // public primitive declaration remains focused on the public API.
    internal const uint SignMask = 0x8000_0000;
    internal const int SignShift = 31;
    internal const uint BiasedExponentMask = 0x7F80_0000;
    internal const int BiasedExponentShift = 23;
    internal const uint TrailingSignificandMask = 0x007F_FFFF;
    internal const byte ShiftedBiasedExponentMask = (byte)(BiasedExponentMask >> BiasedExponentShift);
    internal const uint MaxTrailingSignificand = 0x007F_FFFF;
    internal const uint PositiveInfinityBits = 0x7F80_0000;
    internal const uint SmallestNormalBits = 0x0080_0000;
    internal const int ExponentBias = 127;
    internal const int TrailingSignificandLength = 23;
    internal const int SignificandLength = TrailingSignificandLength + 1;

    internal static float CreateSingle(bool sign, byte exponent, uint significand) =>
        BitConverter.UInt32BitsToSingle((sign ? SignMask : 0U) |
            ((uint)exponent << BiasedExponentShift) | (significand & TrailingSignificandMask));

    public static float ExpM1(float value) => MathF.Exp(value) - 1F;
    public static float Exp2M1(float value) => Exp2(value) - 1F;
    public static float Exp10M1(float value) => Exp10(value) - 1F;
    public static float Lerp(float value1, float value2, float amount) => MultiplyAddEstimate(value1, 1F - amount, value2 * amount);
    public static float ReciprocalEstimate(float value) => (float)Math.ReciprocalEstimate(value);
    public static float ReciprocalSqrtEstimate(float value) => (float)Math.ReciprocalSqrtEstimate(value);
    public static float LogP1(float value) => MathF.Log(value + 1);
    public static float Log2P1(float value) => MathF.Log2(value + 1);
    public static float Log10P1(float value) => MathF.Log10(value + 1);
    public static float MultiplyAddEstimate(float left, float right, float addend) =>
        (float)Math.FusedMultiplyAdd(left, right, addend);
    public static float Round(float value) => MathF.Round(value);
    public static float Round(float value, int digits) => MathF.Round(value, digits);
    public static float Round(float value, MidpointRounding mode) => MathF.Round(value, mode);

    static int IBinaryFloatParseAndFormatInfo<float>.NumberBufferLength => Number.SingleNumberBufferLength;
    static ulong IBinaryFloatParseAndFormatInfo<float>.ZeroBits => 0;
    static ulong IBinaryFloatParseAndFormatInfo<float>.InfinityBits => 0x7F80_0000;
    static ulong IBinaryFloatParseAndFormatInfo<float>.NormalMantissaMask => (1UL << 24) - 1;
    static ulong IBinaryFloatParseAndFormatInfo<float>.DenormalMantissaMask => 0x007F_FFFF;
    static int IBinaryFloatParseAndFormatInfo<float>.MinBinaryExponent => -126;
    static int IBinaryFloatParseAndFormatInfo<float>.MaxBinaryExponent => 127;
    static int IBinaryFloatParseAndFormatInfo<float>.MinDecimalExponent => -45;
    static int IBinaryFloatParseAndFormatInfo<float>.MaxDecimalExponent => 39;
    static int IBinaryFloatParseAndFormatInfo<float>.ExponentBias => 127;
    static ushort IBinaryFloatParseAndFormatInfo<float>.ExponentBits => 8;
    static int IBinaryFloatParseAndFormatInfo<float>.OverflowDecimalExponent => 58;
    static int IBinaryFloatParseAndFormatInfo<float>.InfinityExponent => 0xFF;
    static ushort IBinaryFloatParseAndFormatInfo<float>.NormalMantissaBits => 24;
    static ushort IBinaryFloatParseAndFormatInfo<float>.DenormalMantissaBits => 23;
    static int IBinaryFloatParseAndFormatInfo<float>.MinFastFloatDecimalExponent => -64;
    static int IBinaryFloatParseAndFormatInfo<float>.MaxFastFloatDecimalExponent => 38;
    static int IBinaryFloatParseAndFormatInfo<float>.MinExponentRoundToEven => -17;
    static int IBinaryFloatParseAndFormatInfo<float>.MaxExponentRoundToEven => 10;
    static int IBinaryFloatParseAndFormatInfo<float>.MaxExponentFastPath => 10;
    static ulong IBinaryFloatParseAndFormatInfo<float>.MaxMantissaFastPath => 2UL << 23;
    static float IBinaryFloatParseAndFormatInfo<float>.BitsToFloat(ulong bits) => BitConverter.UInt32BitsToSingle((uint)bits);
    static ulong IBinaryFloatParseAndFormatInfo<float>.FloatToBits(float value) => BitConverter.SingleToUInt32Bits(value);
    static int IBinaryFloatParseAndFormatInfo<float>.MaxRoundTripDigits => 9;
    static int IBinaryFloatParseAndFormatInfo<float>.MaxPrecisionCustomFormat => 7;
    static float Numerics.IFloatingPointConstants<float>.E => E;
    static float Numerics.IFloatingPointConstants<float>.Pi => Pi;
    static float Numerics.IFloatingPointConstants<float>.Tau => Tau;
}

public partial struct Double
{
    // See the corresponding Single helpers above.  These constants/methods
    // are consumed by the binary floating-point conversion implementations.
    internal const ulong SignMask = 0x8000_0000_0000_0000;
    internal const int SignShift = 63;
    internal const ulong BiasedExponentMask = 0x7FF0_0000_0000_0000;
    internal const int BiasedExponentShift = 52;
    internal const ulong TrailingSignificandMask = 0x000F_FFFF_FFFF_FFFF;
    internal const ushort ShiftedBiasedExponentMask = (ushort)(BiasedExponentMask >> BiasedExponentShift);
    internal const ulong MaxTrailingSignificand = 0x000F_FFFF_FFFF_FFFF;
    internal const ulong PositiveInfinityBits = 0x7FF0_0000_0000_0000;
    internal const ulong SmallestNormalBits = 0x0010_0000_0000_0000;
    internal const int ExponentBias = 1023;
    internal const int TrailingSignificandLength = 52;
    internal const int SignificandLength = TrailingSignificandLength + 1;

    internal static double CreateDouble(bool sign, ushort exponent, ulong significand) =>
        BitConverter.UInt64BitsToDouble((sign ? SignMask : 0UL) +
            ((ulong)exponent << BiasedExponentShift) + significand);

    public static double ExpM1(double value) => Math.Exp(value) - 1D;
    public static double Exp2M1(double value) => Exp2(value) - 1D;
    public static double Exp10M1(double value) => Math.Pow(10D, value) - 1D;

    static int IBinaryFloatParseAndFormatInfo<double>.NumberBufferLength => Number.DoubleNumberBufferLength;
    static ulong IBinaryFloatParseAndFormatInfo<double>.ZeroBits => 0;
    static ulong IBinaryFloatParseAndFormatInfo<double>.InfinityBits => 0x7FF0_0000_0000_0000;
    static ulong IBinaryFloatParseAndFormatInfo<double>.NormalMantissaMask => (1UL << 53) - 1;
    static ulong IBinaryFloatParseAndFormatInfo<double>.DenormalMantissaMask => 0x000F_FFFF_FFFF_FFFF;
    static int IBinaryFloatParseAndFormatInfo<double>.MinBinaryExponent => -1022;
    static int IBinaryFloatParseAndFormatInfo<double>.MaxBinaryExponent => 1023;
    static int IBinaryFloatParseAndFormatInfo<double>.MinDecimalExponent => -324;
    static int IBinaryFloatParseAndFormatInfo<double>.MaxDecimalExponent => 309;
    static int IBinaryFloatParseAndFormatInfo<double>.ExponentBias => 1023;
    static ushort IBinaryFloatParseAndFormatInfo<double>.ExponentBits => 11;
    static int IBinaryFloatParseAndFormatInfo<double>.OverflowDecimalExponent => 376;
    static int IBinaryFloatParseAndFormatInfo<double>.InfinityExponent => 0x7FF;
    static ushort IBinaryFloatParseAndFormatInfo<double>.NormalMantissaBits => 53;
    static ushort IBinaryFloatParseAndFormatInfo<double>.DenormalMantissaBits => 52;
    static int IBinaryFloatParseAndFormatInfo<double>.MinFastFloatDecimalExponent => -342;
    static int IBinaryFloatParseAndFormatInfo<double>.MaxFastFloatDecimalExponent => 308;
    static int IBinaryFloatParseAndFormatInfo<double>.MinExponentRoundToEven => -4;
    static int IBinaryFloatParseAndFormatInfo<double>.MaxExponentRoundToEven => 23;
    static int IBinaryFloatParseAndFormatInfo<double>.MaxExponentFastPath => 22;
    static ulong IBinaryFloatParseAndFormatInfo<double>.MaxMantissaFastPath => 2UL << 52;
    static double IBinaryFloatParseAndFormatInfo<double>.BitsToFloat(ulong bits) => BitConverter.UInt64BitsToDouble(bits);
    static ulong IBinaryFloatParseAndFormatInfo<double>.FloatToBits(double value) => BitConverter.DoubleToUInt64Bits(value);
    static int IBinaryFloatParseAndFormatInfo<double>.MaxRoundTripDigits => 17;
    static int IBinaryFloatParseAndFormatInfo<double>.MaxPrecisionCustomFormat => 15;
    static double Numerics.IFloatingPointConstants<double>.E => E;
    static double Numerics.IFloatingPointConstants<double>.Pi => Pi;
    static double Numerics.IFloatingPointConstants<double>.Tau => Tau;
}

public readonly partial struct IntPtr
{
    public static nint One => (nint)1;
    public static nint NegativeOne => (nint)(-1);
    public static int Radix => 2;

    public static nint Abs(nint value) => value == MinValue ? throw new OverflowException() : value < 0 ? -value : value;
    public static nint Clamp(nint value, nint minimum, nint maximum) =>
        minimum > maximum ? throw new ArgumentException() : value < minimum ? minimum : value > maximum ? maximum : value;
    public static nint CopySign(nint value, nint sign)
    {
        if (value == MinValue)
        {
            return sign < 0 ? MinValue : throw new OverflowException();
        }

        var magnitude = value < 0 ? -value : value;
        return sign < 0 ? -magnitude : magnitude;
    }

    public static nint Max(nint left, nint right) => left >= right ? left : right;
    public static nint Min(nint left, nint right) => left <= right ? left : right;
    public static int Sign(nint value) => value < 0 ? -1 : value > 0 ? 1 : 0;
    public static bool IsCanonical(nint value) => true;
    public static bool IsComplexNumber(nint value) => false;
    public static bool IsEvenInteger(nint value) => (value & 1) == 0;
    public static bool IsFinite(nint value) => true;
    public static bool IsImaginaryNumber(nint value) => false;
    public static bool IsInfinity(nint value) => false;
    public static bool IsInteger(nint value) => true;
    public static bool IsNaN(nint value) => false;
    public static bool IsNegative(nint value) => value < 0;
    public static bool IsNegativeInfinity(nint value) => false;
    public static bool IsNormal(nint value) => value != 0;
    public static bool IsOddInteger(nint value) => (value & 1) != 0;
    public static bool IsPositive(nint value) => value >= 0;
    public static bool IsPositiveInfinity(nint value) => false;
    public static bool IsRealNumber(nint value) => true;
    public static bool IsSubnormal(nint value) => false;
    public static bool IsZero(nint value) => value == 0;

    private static nuint Magnitude(nint value) => value < 0 ? (nuint)(-(value + 1)) + 1 : (nuint)value;
    public static nint MaxMagnitude(nint left, nint right)
    {
        var leftMagnitude = Magnitude(left);
        var rightMagnitude = Magnitude(right);
        return leftMagnitude > rightMagnitude ? left : leftMagnitude < rightMagnitude ? right : left >= 0 ? left : right;
    }
    public static nint MaxMagnitudeNumber(nint left, nint right) => MaxMagnitude(left, right);
    public static nint MinMagnitude(nint left, nint right)
    {
        var leftMagnitude = Magnitude(left);
        var rightMagnitude = Magnitude(right);
        return leftMagnitude < rightMagnitude ? left : leftMagnitude > rightMagnitude ? right : left < 0 ? left : right;
    }
    public static nint MinMagnitudeNumber(nint left, nint right) => MinMagnitude(left, right);

    public static nint Log2(nint value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException();
        return Numerics.BitOperations.Log2((nuint)value);
    }
    public static bool IsPow2(nint value) => value > 0 && (value & (value - 1)) == 0;
    public static nint LeadingZeroCount(nint value)
    {
        return Numerics.BitOperations.LeadingZeroCount((nuint)value);
    }
    public static nint PopCount(nint value)
    {
        return Numerics.BitOperations.PopCount((nuint)value);
    }
    public static nint TrailingZeroCount(nint value)
    {
        return Numerics.BitOperations.TrailingZeroCount((nuint)value);
    }
    public static nint RotateLeft(nint value, int rotateAmount)
    {
        return (nint)Numerics.BitOperations.RotateLeft((nuint)value, rotateAmount);
    }
    public static nint RotateRight(nint value, int rotateAmount) =>
        (nint)Numerics.BitOperations.RotateRight((nuint)value, rotateAmount);
    public static (nint Quotient, nint Remainder) DivRem(nint left, nint right)
    {
        var quotient = left / right;
        return (quotient, left - quotient * right);
    }

    public int GetByteCount() => Size;
    public int GetShortestBitLength()
    {
        var bitCount = Size * 8;
        return this >= 0 ? bitCount - (int)LeadingZeroCount((nint)this) : bitCount + 1 - (int)LeadingZeroCount((nint)~this);
    }

    private static nint FromBits(ulong bits) => Size == 8 ? (nint)(long)bits : (nint)(int)(uint)bits;
    private static ulong ToBits(nint value) => Size == 8 ? (ulong)(long)value : (uint)(int)value;

    public bool TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
    {
        if (destination.Length < Size) { bytesWritten = 0; return false; }
        var bits = ToBits(this);
        for (var index = 0; index < Size; index++) destination[Size - 1 - index] = (byte)(bits >> (index * 8));
        bytesWritten = Size;
        return true;
    }
    public bool TryWriteLittleEndian(Span<byte> destination, out int bytesWritten)
    {
        if (destination.Length < Size) { bytesWritten = 0; return false; }
        var bits = ToBits(this);
        for (var index = 0; index < Size; index++) destination[index] = (byte)(bits >> (index * 8));
        bytesWritten = Size;
        return true;
    }
    public static bool TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out nint value) => TryReadNative(source, isUnsigned, bigEndian: true, out value);
    public static bool TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out nint value) => TryReadNative(source, isUnsigned, bigEndian: false, out value);

    private static bool TryReadNative(ReadOnlySpan<byte> source, bool isUnsigned, bool bigEndian, out nint value)
    {
        value = 0;
        if (source.IsEmpty) return true;

        var width = Size;
        var negative = !isUnsigned && (bigEndian ? (source[0] & 0x80) != 0 : (source[^1] & 0x80) != 0);
        var skip = source.Length > width ? source.Length - width : 0;
        var extension = negative ? (byte)0xFF : (byte)0;
        for (var index = 0; index < skip; index++)
        {
            if (source[index] != extension) return false;
        }
        if (skip != 0) source = source[skip..];
        if (isUnsigned && (source.Length == width) && (bigEndian ? (source[0] & 0x80) != 0 : (source[^1] & 0x80) != 0)) return false;

        ulong bits = 0;
        if (bigEndian)
        {
            for (var index = 0; index < source.Length; index++) bits = (bits << 8) | source[index];
        }
        else
        {
            for (var index = source.Length - 1; index >= 0; index--) bits = (bits << 8) | source[index];
        }
        if (negative && source.Length < width) bits |= ulong.MaxValue << (source.Length * 8);
        value = FromBits(bits);
        return true;
    }

    public string ToString(string? format, IFormatProvider? provider) => Size == 8 ? ((long)_value).ToString(format, provider) : ((int)_value).ToString(format, provider);
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Size == 8 ? ((long)_value).TryFormat(destination, out charsWritten, format, provider) : ((int)_value).TryFormat(destination, out charsWritten, format, provider);
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Size == 8 ? ((long)_value).TryFormat(destination, out bytesWritten, format, provider) : ((int)_value).TryFormat(destination, out bytesWritten, format, provider);
    public static nint Parse(string value, Globalization.NumberStyles style, IFormatProvider? provider) => Parse(value, style);
    public static bool TryParse(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out nint result) => TryParse(value, out result);
    public static nint Parse(string value, IFormatProvider? provider) => Parse(value);
    public static bool TryParse(string? value, IFormatProvider? provider, out nint result) => TryParse(value, out result);
    public static nint Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => Parse(value.ToString());
    public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out nint result) => TryParse(value.ToString(), out result);
    public static nint Parse(ReadOnlySpan<char> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(value.ToString(), style, provider);
    public static bool TryParse(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out nint result) => TryParse(value.ToString(), style, provider, out result);

    static nint Numerics.INumberBase<nint>.One => One;
    static nint Numerics.INumberBase<nint>.Zero => Zero;
    static int Numerics.INumberBase<nint>.Radix => Radix;
    static nint Numerics.INumberBase<nint>.Abs(nint value) => Abs(value);
    static nint Numerics.INumberBase<nint>.MaxMagnitude(nint left, nint right) => MaxMagnitude(left, right);
    static nint Numerics.INumberBase<nint>.MaxMagnitudeNumber(nint left, nint right) => MaxMagnitudeNumber(left, right);
    static nint Numerics.INumberBase<nint>.MinMagnitude(nint left, nint right) => MinMagnitude(left, right);
    static nint Numerics.INumberBase<nint>.MinMagnitudeNumber(nint left, nint right) => MinMagnitudeNumber(left, right);
    static bool Numerics.INumberBase<nint>.IsCanonical(nint value) => IsCanonical(value);
    static bool Numerics.INumberBase<nint>.IsComplexNumber(nint value) => IsComplexNumber(value);
    static bool Numerics.INumberBase<nint>.IsEvenInteger(nint value) => IsEvenInteger(value);
    static bool Numerics.INumberBase<nint>.IsFinite(nint value) => IsFinite(value);
    static bool Numerics.INumberBase<nint>.IsImaginaryNumber(nint value) => IsImaginaryNumber(value);
    static bool Numerics.INumberBase<nint>.IsInfinity(nint value) => IsInfinity(value);
    static bool Numerics.INumberBase<nint>.IsInteger(nint value) => IsInteger(value);
    static bool Numerics.INumberBase<nint>.IsNaN(nint value) => IsNaN(value);
    static bool Numerics.INumberBase<nint>.IsNegative(nint value) => IsNegative(value);
    static bool Numerics.INumberBase<nint>.IsNegativeInfinity(nint value) => IsNegativeInfinity(value);
    static bool Numerics.INumberBase<nint>.IsNormal(nint value) => IsNormal(value);
    static bool Numerics.INumberBase<nint>.IsOddInteger(nint value) => IsOddInteger(value);
    static bool Numerics.INumberBase<nint>.IsPositive(nint value) => IsPositive(value);
    static bool Numerics.INumberBase<nint>.IsPositiveInfinity(nint value) => IsPositiveInfinity(value);
    static bool Numerics.INumberBase<nint>.IsRealNumber(nint value) => IsRealNumber(value);
    static bool Numerics.INumberBase<nint>.IsSubnormal(nint value) => IsSubnormal(value);
    static bool Numerics.INumberBase<nint>.IsZero(nint value) => IsZero(value);
    static bool Numerics.IBinaryNumber<nint>.IsPow2(nint value) => IsPow2(value);
    static nint Numerics.IBinaryNumber<nint>.Log2(nint value) => Log2(value);
    static nint Numerics.IBinaryInteger<nint>.PopCount(nint value) => PopCount(value);
    static nint Numerics.IBinaryInteger<nint>.TrailingZeroCount(nint value) => TrailingZeroCount(value);
    static nint Numerics.IMinMaxValue<nint>.MinValue => MinValue;
    static nint Numerics.IMinMaxValue<nint>.MaxValue => MaxValue;
    static nint Numerics.ISignedNumber<nint>.NegativeOne => NegativeOne;
    static nint Numerics.INumber<nint>.MaxNumber(nint left, nint right) => Max(left, right);
    static nint Numerics.INumber<nint>.MinNumber(nint left, nint right) => Min(left, right);
    static nint Numerics.INumberBase<nint>.MultiplyAddEstimate(nint left, nint right, nint addend) => left * right + addend;
    static nint Numerics.IAdditionOperators<nint, nint, nint>.operator +(nint left, nint right) => left + right;
    static nint Numerics.IAdditionOperators<nint, nint, nint>.operator checked +(nint left, nint right) => checked(left + right);
    static nint Numerics.IAdditiveIdentity<nint, nint>.AdditiveIdentity => Zero;
    static nint Numerics.IBitwiseOperators<nint, nint, nint>.operator &(nint left, nint right) => left & right;
    static nint Numerics.IBitwiseOperators<nint, nint, nint>.operator |(nint left, nint right) => left | right;
    static nint Numerics.IBitwiseOperators<nint, nint, nint>.operator ^(nint left, nint right) => left ^ right;
    static nint Numerics.IBitwiseOperators<nint, nint, nint>.operator ~(nint value) => ~value;
    static bool Numerics.IComparisonOperators<nint, nint, bool>.operator <(nint left, nint right) => left < right;
    static bool Numerics.IComparisonOperators<nint, nint, bool>.operator <=(nint left, nint right) => left <= right;
    static bool Numerics.IComparisonOperators<nint, nint, bool>.operator >(nint left, nint right) => left > right;
    static bool Numerics.IComparisonOperators<nint, nint, bool>.operator >=(nint left, nint right) => left >= right;
    static nint Numerics.IDecrementOperators<nint>.operator --(nint value) => --value;
    static nint Numerics.IDivisionOperators<nint, nint, nint>.operator /(nint left, nint right) => left / right;
    static bool Numerics.IEqualityOperators<nint, nint, bool>.operator ==(nint left, nint right) => left == right;
    static bool Numerics.IEqualityOperators<nint, nint, bool>.operator !=(nint left, nint right) => left != right;
    static nint Numerics.IIncrementOperators<nint>.operator ++(nint value) => ++value;
    static nint Numerics.IMultiplicativeIdentity<nint, nint>.MultiplicativeIdentity => One;
    static nint Numerics.IMultiplyOperators<nint, nint, nint>.operator *(nint left, nint right) => left * right;
    static nint Numerics.ISubtractionOperators<nint, nint, nint>.operator -(nint left, nint right) => left - right;
    static nint Numerics.IUnaryNegationOperators<nint, nint>.operator -(nint value) => -value;
    static nint Numerics.IUnaryPlusOperators<nint, nint>.operator +(nint value) => value;
    static nint Numerics.IModulusOperators<nint, nint, nint>.operator %(nint left, nint right) => left % right;
    static nint Numerics.IShiftOperators<nint, int, nint>.operator <<(nint value, int amount) => value << amount;
    static nint Numerics.IShiftOperators<nint, int, nint>.operator >>(nint value, int amount) => value >> amount;
    static nint Numerics.IShiftOperators<nint, int, nint>.operator >>>(nint value, int amount) => (nint)((nuint)value >> amount);

}

public readonly partial struct UIntPtr
{
    public static nuint One => (nuint)1;
    public static int Radix => 2;
    public static nuint Abs(nuint value) => value;
    public static nuint Clamp(nuint value, nuint minimum, nuint maximum) =>
        minimum > maximum ? throw new ArgumentException() : value < minimum ? minimum : value > maximum ? maximum : value;
    public static nuint Max(nuint left, nuint right) => left >= right ? left : right;
    public static nuint Min(nuint left, nuint right) => left <= right ? left : right;
    public static int Sign(nuint value) => value == 0 ? 0 : 1;
    public static bool IsCanonical(nuint value) => true;
    public static bool IsComplexNumber(nuint value) => false;
    public static bool IsEvenInteger(nuint value) => (value & 1) == 0;
    public static bool IsFinite(nuint value) => true;
    public static bool IsImaginaryNumber(nuint value) => false;
    public static bool IsInfinity(nuint value) => false;
    public static bool IsInteger(nuint value) => true;
    public static bool IsNaN(nuint value) => false;
    public static bool IsNegative(nuint value) => false;
    public static bool IsNegativeInfinity(nuint value) => false;
    public static bool IsNormal(nuint value) => value != 0;
    public static bool IsOddInteger(nuint value) => (value & 1) != 0;
    public static bool IsPositive(nuint value) => true;
    public static bool IsPositiveInfinity(nuint value) => false;
    public static bool IsRealNumber(nuint value) => true;
    public static bool IsSubnormal(nuint value) => false;
    public static bool IsZero(nuint value) => value == 0;
    public static nuint MaxMagnitude(nuint left, nuint right) => Max(left, right);
    public static nuint MaxMagnitudeNumber(nuint left, nuint right) => Max(left, right);
    public static nuint MinMagnitude(nuint left, nuint right) => Min(left, right);
    public static nuint MinMagnitudeNumber(nuint left, nuint right) => Min(left, right);

    public static nuint Log2(nuint value) => (nuint)Numerics.BitOperations.Log2(value);
    public static bool IsPow2(nuint value) => value != 0 && (value & (value - 1)) == 0;
    public static nuint LeadingZeroCount(nuint value) =>
        (nuint)Numerics.BitOperations.LeadingZeroCount(value);
    public static nuint PopCount(nuint value) =>
        (nuint)Numerics.BitOperations.PopCount(value);
    public static nuint TrailingZeroCount(nuint value) =>
        (nuint)Numerics.BitOperations.TrailingZeroCount(value);
    public static nuint RotateLeft(nuint value, int rotateAmount) =>
        Numerics.BitOperations.RotateLeft(value, rotateAmount);
    public static nuint RotateRight(nuint value, int rotateAmount) =>
        Numerics.BitOperations.RotateRight(value, rotateAmount);
    public static (nuint Quotient, nuint Remainder) DivRem(nuint left, nuint right)
    {
        var quotient = left / right;
        return (quotient, left - quotient * right);
    }

    public int GetByteCount() => Size;
    public int GetShortestBitLength() => this == 0 ? 0 : Size * 8 - (int)LeadingZeroCount((nuint)this);
    private static nuint FromBits(ulong bits) => Size == 8 ? (nuint)bits : (nuint)(uint)bits;
    private static ulong ToBits(nuint value) => Size == 8 ? (ulong)value : (uint)value;

    public bool TryWriteBigEndian(Span<byte> destination, out int bytesWritten)
    {
        if (destination.Length < Size) { bytesWritten = 0; return false; }
        var bits = ToBits(this);
        for (var index = 0; index < Size; index++) destination[Size - 1 - index] = (byte)(bits >> (index * 8));
        bytesWritten = Size;
        return true;
    }
    public bool TryWriteLittleEndian(Span<byte> destination, out int bytesWritten)
    {
        if (destination.Length < Size) { bytesWritten = 0; return false; }
        var bits = ToBits(this);
        for (var index = 0; index < Size; index++) destination[index] = (byte)(bits >> (index * 8));
        bytesWritten = Size;
        return true;
    }
    public static bool TryReadBigEndian(ReadOnlySpan<byte> source, bool isUnsigned, out nuint value) => TryReadNative(source, isUnsigned, bigEndian: true, out value);
    public static bool TryReadLittleEndian(ReadOnlySpan<byte> source, bool isUnsigned, out nuint value) => TryReadNative(source, isUnsigned, bigEndian: false, out value);

    private static bool TryReadNative(ReadOnlySpan<byte> source, bool isUnsigned, bool bigEndian, out nuint value)
    {
        value = 0;
        if (source.IsEmpty) return true;
        var width = Size;
        var negative = !isUnsigned && (bigEndian ? (source[0] & 0x80) != 0 : (source[^1] & 0x80) != 0);
        if (negative) return false;
        var skip = source.Length > width ? source.Length - width : 0;
        for (var index = 0; index < skip; index++)
        {
            if (source[index] != 0) return false;
        }
        if (skip != 0) source = source[skip..];
        if (!isUnsigned && source.Length == width && (bigEndian ? (source[0] & 0x80) != 0 : (source[^1] & 0x80) != 0)) return false;
        ulong bits = 0;
        if (bigEndian)
        {
            for (var index = 0; index < source.Length; index++) bits = (bits << 8) | source[index];
        }
        else
        {
            for (var index = source.Length - 1; index >= 0; index--) bits = (bits << 8) | source[index];
        }
        value = FromBits(bits);
        return true;
    }

    public string ToString(string? format, IFormatProvider? provider) => Size == 8 ? ((ulong)_value).ToString(format, provider) : ((uint)_value).ToString(format, provider);
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Size == 8 ? ((ulong)_value).TryFormat(destination, out charsWritten, format, provider) : ((uint)_value).TryFormat(destination, out charsWritten, format, provider);
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Size == 8 ? ((ulong)_value).TryFormat(destination, out bytesWritten, format, provider) : ((uint)_value).TryFormat(destination, out bytesWritten, format, provider);
    public static nuint Parse(string value, Globalization.NumberStyles style, IFormatProvider? provider) => Parse(value, style);
    public static bool TryParse(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out nuint result) => TryParse(value, out result);
    public static nuint Parse(string value, IFormatProvider? provider) => Parse(value);
    public static bool TryParse(string? value, IFormatProvider? provider, out nuint result) => TryParse(value, out result);
    public static nuint Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => Parse(value.ToString());
    public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out nuint result) => TryParse(value.ToString(), out result);
    public static nuint Parse(ReadOnlySpan<char> value, Globalization.NumberStyles style = System.Globalization.NumberStyles.Integer, IFormatProvider? provider = null) => Parse(value.ToString(), style, provider);
    public static bool TryParse(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out nuint result) => TryParse(value.ToString(), style, provider, out result);

    static nuint Numerics.INumberBase<nuint>.One => One;
    static nuint Numerics.INumberBase<nuint>.Zero => Zero;
    static int Numerics.INumberBase<nuint>.Radix => Radix;
    static nuint Numerics.INumberBase<nuint>.Abs(nuint value) => Abs(value);
    static nuint Numerics.INumberBase<nuint>.MaxMagnitude(nuint left, nuint right) => MaxMagnitude(left, right);
    static nuint Numerics.INumberBase<nuint>.MaxMagnitudeNumber(nuint left, nuint right) => MaxMagnitudeNumber(left, right);
    static nuint Numerics.INumberBase<nuint>.MinMagnitude(nuint left, nuint right) => MinMagnitude(left, right);
    static nuint Numerics.INumberBase<nuint>.MinMagnitudeNumber(nuint left, nuint right) => MinMagnitudeNumber(left, right);
    static bool Numerics.INumberBase<nuint>.IsCanonical(nuint value) => IsCanonical(value);
    static bool Numerics.INumberBase<nuint>.IsComplexNumber(nuint value) => IsComplexNumber(value);
    static bool Numerics.INumberBase<nuint>.IsEvenInteger(nuint value) => IsEvenInteger(value);
    static bool Numerics.INumberBase<nuint>.IsFinite(nuint value) => IsFinite(value);
    static bool Numerics.INumberBase<nuint>.IsImaginaryNumber(nuint value) => IsImaginaryNumber(value);
    static bool Numerics.INumberBase<nuint>.IsInfinity(nuint value) => IsInfinity(value);
    static bool Numerics.INumberBase<nuint>.IsInteger(nuint value) => IsInteger(value);
    static bool Numerics.INumberBase<nuint>.IsNaN(nuint value) => IsNaN(value);
    static bool Numerics.INumberBase<nuint>.IsNegative(nuint value) => IsNegative(value);
    static bool Numerics.INumberBase<nuint>.IsNegativeInfinity(nuint value) => IsNegativeInfinity(value);
    static bool Numerics.INumberBase<nuint>.IsNormal(nuint value) => IsNormal(value);
    static bool Numerics.INumberBase<nuint>.IsOddInteger(nuint value) => IsOddInteger(value);
    static bool Numerics.INumberBase<nuint>.IsPositive(nuint value) => IsPositive(value);
    static bool Numerics.INumberBase<nuint>.IsPositiveInfinity(nuint value) => IsPositiveInfinity(value);
    static bool Numerics.INumberBase<nuint>.IsRealNumber(nuint value) => IsRealNumber(value);
    static bool Numerics.INumberBase<nuint>.IsSubnormal(nuint value) => IsSubnormal(value);
    static bool Numerics.INumberBase<nuint>.IsZero(nuint value) => IsZero(value);
    static bool Numerics.IBinaryNumber<nuint>.IsPow2(nuint value) => IsPow2(value);
    static nuint Numerics.IBinaryNumber<nuint>.Log2(nuint value) => Log2(value);
    static nuint Numerics.IBinaryInteger<nuint>.PopCount(nuint value) => PopCount(value);
    static nuint Numerics.IBinaryInteger<nuint>.TrailingZeroCount(nuint value) => TrailingZeroCount(value);
    static nuint Numerics.IMinMaxValue<nuint>.MinValue => MinValue;
    static nuint Numerics.IMinMaxValue<nuint>.MaxValue => MaxValue;
    static nuint Numerics.INumber<nuint>.MaxNumber(nuint left, nuint right) => Max(left, right);
    static nuint Numerics.INumber<nuint>.MinNumber(nuint left, nuint right) => Min(left, right);
    static nuint Numerics.INumberBase<nuint>.MultiplyAddEstimate(nuint left, nuint right, nuint addend) => left * right + addend;
    static nuint Numerics.IAdditionOperators<nuint, nuint, nuint>.operator +(nuint left, nuint right) => left + right;
    static nuint Numerics.IAdditionOperators<nuint, nuint, nuint>.operator checked +(nuint left, nuint right) => checked(left + right);
    static nuint Numerics.IAdditiveIdentity<nuint, nuint>.AdditiveIdentity => Zero;
    static nuint Numerics.IBitwiseOperators<nuint, nuint, nuint>.operator &(nuint left, nuint right) => left & right;
    static nuint Numerics.IBitwiseOperators<nuint, nuint, nuint>.operator |(nuint left, nuint right) => left | right;
    static nuint Numerics.IBitwiseOperators<nuint, nuint, nuint>.operator ^(nuint left, nuint right) => left ^ right;
    static nuint Numerics.IBitwiseOperators<nuint, nuint, nuint>.operator ~(nuint value) => ~value;
    static bool Numerics.IComparisonOperators<nuint, nuint, bool>.operator <(nuint left, nuint right) => left < right;
    static bool Numerics.IComparisonOperators<nuint, nuint, bool>.operator <=(nuint left, nuint right) => left <= right;
    static bool Numerics.IComparisonOperators<nuint, nuint, bool>.operator >(nuint left, nuint right) => left > right;
    static bool Numerics.IComparisonOperators<nuint, nuint, bool>.operator >=(nuint left, nuint right) => left >= right;
    static nuint Numerics.IDecrementOperators<nuint>.operator --(nuint value) => --value;
    static nuint Numerics.IDivisionOperators<nuint, nuint, nuint>.operator /(nuint left, nuint right) => left / right;
    static bool Numerics.IEqualityOperators<nuint, nuint, bool>.operator ==(nuint left, nuint right) => left == right;
    static bool Numerics.IEqualityOperators<nuint, nuint, bool>.operator !=(nuint left, nuint right) => left != right;
    static nuint Numerics.IIncrementOperators<nuint>.operator ++(nuint value) => ++value;
    static nuint Numerics.IMultiplicativeIdentity<nuint, nuint>.MultiplicativeIdentity => One;
    static nuint Numerics.IMultiplyOperators<nuint, nuint, nuint>.operator *(nuint left, nuint right) => left * right;
    static nuint Numerics.ISubtractionOperators<nuint, nuint, nuint>.operator -(nuint left, nuint right) => left - right;
    static nuint Numerics.IUnaryNegationOperators<nuint, nuint>.operator -(nuint value) => ~value + 1;
    static nuint Numerics.IUnaryPlusOperators<nuint, nuint>.operator +(nuint value) => value;
    static nuint Numerics.IModulusOperators<nuint, nuint, nuint>.operator %(nuint left, nuint right) => left % right;
    static nuint Numerics.IShiftOperators<nuint, int, nuint>.operator <<(nuint value, int amount) => value << amount;
    static nuint Numerics.IShiftOperators<nuint, int, nuint>.operator >>(nuint value, int amount) => value >> amount;
    static nuint Numerics.IShiftOperators<nuint, int, nuint>.operator >>>(nuint value, int amount) => value >> amount;

}

public partial struct Byte
{
    public TypeCode GetTypeCode() => TypeCode.Byte;
    public static byte Log10(byte value) => (byte)PrimitiveIntegralConversions.Log10(value);
    public static bool TryParse(ReadOnlySpan<char> value, out byte result) => TryParse(value, null, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, out byte result) => TryParse(value, null, out result);
    public static bool TryParsePartial(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out byte result, out int consumed)
    {
        return PrimitiveIntegralConversions.TryParsePartial(value, style, (string text, out byte parsed) => TryParse(text, style, provider, out parsed), out result, out consumed);
    }
    public static bool TryParsePartial(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out byte result, out int consumed) =>
        TryParsePartial(value.ToString(), style, provider, out result, out consumed);
    public static bool TryParsePartial(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out byte result, out int consumed) =>
        TryParsePartial(PrimitiveScalarContracts.Utf8ToString(value), style, provider, out result, out consumed);
    bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean(this);
    char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar(this);
    sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte(this);
    byte IConvertible.ToByte(IFormatProvider? provider) => this;
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
    object IConvertible.ToType(Type conversionType, IFormatProvider? provider) => PrimitiveIntegralConversions.ToType(this, conversionType, provider);
}

public partial struct SByte
{
    public TypeCode GetTypeCode() => TypeCode.SByte;
    public static sbyte Log10(sbyte value) => (sbyte)PrimitiveIntegralConversions.Log10Signed(value);
    public static bool TryParse(ReadOnlySpan<char> value, out sbyte result) => TryParse(value, null, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, out sbyte result) => TryParse(value, null, out result);
    public static bool TryParsePartial(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out sbyte result, out int consumed)
    {
        return PrimitiveIntegralConversions.TryParsePartial(value, style, (string text, out sbyte parsed) => TryParse(text, style, provider, out parsed), out result, out consumed);
    }
    public static bool TryParsePartial(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out sbyte result, out int consumed) =>
        TryParsePartial(value.ToString(), style, provider, out result, out consumed);
    public static bool TryParsePartial(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out sbyte result, out int consumed) =>
        TryParsePartial(PrimitiveScalarContracts.Utf8ToString(value), style, provider, out result, out consumed);
    bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean(this);
    char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar(this);
    sbyte IConvertible.ToSByte(IFormatProvider? provider) => this;
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
    object IConvertible.ToType(Type conversionType, IFormatProvider? provider) => PrimitiveIntegralConversions.ToType(this, conversionType, provider);
}

public partial struct Int16
{
    public TypeCode GetTypeCode() => TypeCode.Int16;
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Number.TryFormatInt32(this, 0x0000FFFF, format, provider, destination, out bytesWritten);
    public static short Log10(short value) => (short)PrimitiveIntegralConversions.Log10Signed(value);
    public static bool TryParse(ReadOnlySpan<char> value, out short result) => TryParse(value, null, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, out short result) => TryParse(value, null, out result);
    public static bool TryParsePartial(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out short result, out int consumed)
    {
        return PrimitiveIntegralConversions.TryParsePartial(value, style, (string text, out short parsed) => TryParse(text, style, provider, out parsed), out result, out consumed);
    }
    public static bool TryParsePartial(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out short result, out int consumed) => TryParsePartial(value.ToString(), style, provider, out result, out consumed);
    public static bool TryParsePartial(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out short result, out int consumed) => TryParsePartial(PrimitiveScalarContracts.Utf8ToString(value), style, provider, out result, out consumed);
    bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean(this);
    char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar(this);
    sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte(this);
    byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte(this);
    short IConvertible.ToInt16(IFormatProvider? provider) => this;
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
    object IConvertible.ToType(Type conversionType, IFormatProvider? provider) => PrimitiveIntegralConversions.ToType(this, conversionType, provider);
}

public partial struct UInt16
{
    public TypeCode GetTypeCode() => TypeCode.UInt16;
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Number.TryFormatUInt32(this, format, provider, destination, out bytesWritten);
    public static ushort Log10(ushort value) => (ushort)PrimitiveIntegralConversions.Log10(value);
    public static bool TryParse(ReadOnlySpan<char> value, out ushort result) => TryParse(value, null, out result);
    public static bool TryParse(ReadOnlySpan<byte> value, out ushort result) => TryParse(value, null, out result);
    public static bool TryParsePartial(string? value, Globalization.NumberStyles style, IFormatProvider? provider, out ushort result, out int consumed)
    {
        return PrimitiveIntegralConversions.TryParsePartial(value, style, (string text, out ushort parsed) => TryParse(text, style, provider, out parsed), out result, out consumed);
    }
    public static bool TryParsePartial(ReadOnlySpan<char> value, Globalization.NumberStyles style, IFormatProvider? provider, out ushort result, out int consumed) => TryParsePartial(value.ToString(), style, provider, out result, out consumed);
    public static bool TryParsePartial(ReadOnlySpan<byte> value, Globalization.NumberStyles style, IFormatProvider? provider, out ushort result, out int consumed) => TryParsePartial(PrimitiveScalarContracts.Utf8ToString(value), style, provider, out result, out consumed);
    bool IConvertible.ToBoolean(IFormatProvider? provider) => Convert.ToBoolean(this);
    char IConvertible.ToChar(IFormatProvider? provider) => Convert.ToChar(this);
    sbyte IConvertible.ToSByte(IFormatProvider? provider) => Convert.ToSByte(this);
    byte IConvertible.ToByte(IFormatProvider? provider) => Convert.ToByte(this);
    short IConvertible.ToInt16(IFormatProvider? provider) => Convert.ToInt16(this);
    ushort IConvertible.ToUInt16(IFormatProvider? provider) => this;
    int IConvertible.ToInt32(IFormatProvider? provider) => Convert.ToInt32(this);
    uint IConvertible.ToUInt32(IFormatProvider? provider) => Convert.ToUInt32(this);
    long IConvertible.ToInt64(IFormatProvider? provider) => Convert.ToInt64(this);
    ulong IConvertible.ToUInt64(IFormatProvider? provider) => Convert.ToUInt64(this);
    float IConvertible.ToSingle(IFormatProvider? provider) => Convert.ToSingle(this);
    double IConvertible.ToDouble(IFormatProvider? provider) => Convert.ToDouble(this);
    decimal IConvertible.ToDecimal(IFormatProvider? provider) => Convert.ToDecimal(this);
    DateTime IConvertible.ToDateTime(IFormatProvider? provider) => throw new InvalidCastException();
    string IConvertible.ToString(IFormatProvider? provider) => ToString(provider);
    object IConvertible.ToType(Type conversionType, IFormatProvider? provider) => PrimitiveIntegralConversions.ToType(this, conversionType, provider);
}
