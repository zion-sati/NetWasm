// Scalar generic-math and formatting contracts adapted from the corresponding
// dotnet/runtime System.Private.CoreLib primitive sources.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.
// Pinned upstream runtime commit: deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace System;

internal static class PrimitiveIntegralConversions
{
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

}

public readonly partial struct Int16 : IBinaryIntegerParseAndFormatInfo<short>
{
    // Parsing is dotnet/runtime 474ed96's Int16.cs, over Number.Parsing's integer parser.
    public static short Parse(string s) => Parse(s, Globalization.NumberStyles.Integer, provider: null);

    public static short Parse(string s, Globalization.NumberStyles style) => Parse(s, style, provider: null);

    public static short Parse(string s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static short Parse(string s, Globalization.NumberStyles style, IFormatProvider? provider)
    {
        if (s is null) { throw new ArgumentNullException(nameof(s)); }
        return Parse(s.AsSpan(), style, provider);
    }

    public static short Parse(ReadOnlySpan<char> s, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<char, short>(s, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, out short result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out short result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out short result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out short result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParse(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out short result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out short result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out short result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out short result, out int bytesConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out bytesConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out short result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static short Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out short result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static short Parse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<byte, short>(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out short result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static short Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out short result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider, out result);

    static bool IBinaryIntegerParseAndFormatInfo<short>.IsSigned => true;

    static int IBinaryIntegerParseAndFormatInfo<short>.MaxDigitCount => 5; // 32_767

    static int IBinaryIntegerParseAndFormatInfo<short>.MaxHexDigitCount => 4; // 0x7FFF

    static short IBinaryIntegerParseAndFormatInfo<short>.MaxValueDiv10 => MaxValue / 10;

    static string IBinaryIntegerParseAndFormatInfo<short>.OverflowMessage => SR.Overflow_Int16;

    static bool IBinaryIntegerParseAndFormatInfo<short>.IsGreaterThanAsUnsigned(short left, short right) => (ushort)(left) > (ushort)(right);

    static short IBinaryIntegerParseAndFormatInfo<short>.MultiplyBy10(short value) => (short)(value * 10);

    static short IBinaryIntegerParseAndFormatInfo<short>.MultiplyBy16(short value) => (short)(value * 16);

    // Number's format-free entry point: what the default format writes, without the format string parser.
    public string ToString(IFormatProvider? provider) => Number.FormatInt32WithoutFormat(this, provider);

    public string ToString(string? format, IFormatProvider? provider) =>
        Number.FormatInt32(this, 0x0000FFFF, format, provider);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
        IFormatProvider? provider = null) =>
        Number.TryFormatInt32(this, 0x0000FFFF, format, provider, destination, out charsWritten);

    static short Numerics.IMinMaxValue<short>.MinValue => MinValue;
    static short Numerics.IMinMaxValue<short>.MaxValue => MaxValue;
}

public readonly partial struct UInt16 : IBinaryIntegerParseAndFormatInfo<ushort>
{
    // Parsing is dotnet/runtime 474ed96's UInt16.cs, over Number.Parsing's integer parser.
    public static ushort Parse(string s) => Parse(s, Globalization.NumberStyles.Integer, provider: null);

    public static ushort Parse(string s, Globalization.NumberStyles style) => Parse(s, style, provider: null);

    public static ushort Parse(string s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static ushort Parse(string s, Globalization.NumberStyles style, IFormatProvider? provider)
    {
        if (s is null) { throw new ArgumentNullException(nameof(s)); }
        return Parse(s.AsSpan(), style, provider);
    }

    public static ushort Parse(ReadOnlySpan<char> s, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<char, ushort>(s, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, out ushort result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ushort result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out ushort result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out ushort result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParse(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out ushort result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out ushort result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out ushort result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out ushort result, out int bytesConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out bytesConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out ushort result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static ushort Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ushort result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static ushort Parse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<byte, ushort>(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out ushort result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static ushort Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out ushort result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider, out result);

    static bool IBinaryIntegerParseAndFormatInfo<ushort>.IsSigned => false;

    static int IBinaryIntegerParseAndFormatInfo<ushort>.MaxDigitCount => 5; // 65_535

    static int IBinaryIntegerParseAndFormatInfo<ushort>.MaxHexDigitCount => 4; // 0xFFFF

    static ushort IBinaryIntegerParseAndFormatInfo<ushort>.MaxValueDiv10 => MaxValue / 10;

    static string IBinaryIntegerParseAndFormatInfo<ushort>.OverflowMessage => SR.Overflow_UInt16;

    static bool IBinaryIntegerParseAndFormatInfo<ushort>.IsGreaterThanAsUnsigned(ushort left, ushort right) => left > right;

    static ushort IBinaryIntegerParseAndFormatInfo<ushort>.MultiplyBy10(ushort value) => (ushort)(value * 10);

    static ushort IBinaryIntegerParseAndFormatInfo<ushort>.MultiplyBy16(ushort value) => (ushort)(value * 16);

    public string ToString(IFormatProvider? provider) => Number.UInt32ToDecStr(this);

    public string ToString(string? format, IFormatProvider? provider) =>
        Number.FormatUInt32(this, format, provider);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
        IFormatProvider? provider = null) =>
        Number.TryFormatUInt32(this, format, provider, destination, out charsWritten);

    static ushort Numerics.IMinMaxValue<ushort>.MinValue => MinValue;
    static ushort Numerics.IMinMaxValue<ushort>.MaxValue => MaxValue;
}

public readonly partial struct Int32 : IBinaryIntegerParseAndFormatInfo<int>
{
    // Parsing is dotnet/runtime 474ed96's Int32.cs, over Number.Parsing's integer parser.
    public static int Parse(string s) => Parse(s, Globalization.NumberStyles.Integer, provider: null);

    public static int Parse(string s, Globalization.NumberStyles style) => Parse(s, style, provider: null);

    public static int Parse(string s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static int Parse(string s, Globalization.NumberStyles style, IFormatProvider? provider)
    {
        if (s is null) { throw new ArgumentNullException(nameof(s)); }
        return Parse(s.AsSpan(), style, provider);
    }

    public static int Parse(ReadOnlySpan<char> s, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<char, int>(s, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, out int result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out int result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out int result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out int result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParse(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out int result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out int result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out int result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out int result, out int bytesConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out bytesConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out int result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static int Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out int result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static int Parse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<byte, int>(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out int result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static int Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out int result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider, out result);

    static bool IBinaryIntegerParseAndFormatInfo<int>.IsSigned => true;

    static int IBinaryIntegerParseAndFormatInfo<int>.MaxDigitCount => 10; // 2_147_483_647

    static int IBinaryIntegerParseAndFormatInfo<int>.MaxHexDigitCount => 8; // 0x7FFF_FFFF

    static int IBinaryIntegerParseAndFormatInfo<int>.MaxValueDiv10 => MaxValue / 10;

    static string IBinaryIntegerParseAndFormatInfo<int>.OverflowMessage => SR.Overflow_Int32;

    static bool IBinaryIntegerParseAndFormatInfo<int>.IsGreaterThanAsUnsigned(int left, int right) => (uint)(left) > (uint)(right);

    static int IBinaryIntegerParseAndFormatInfo<int>.MultiplyBy10(int value) => value * 10;

    static int IBinaryIntegerParseAndFormatInfo<int>.MultiplyBy16(int value) => value * 16;

    public TypeCode GetTypeCode() => TypeCode.Int32;
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Number.TryFormatInt32(this, ~0, format, provider, destination, out bytesWritten);
    public static long BigMul(int left, int right) => (long)left * right;

    public static int Log10(int value) => PrimitiveIntegralConversions.Log10Signed(value);
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
    // Parsing follows dotnet/runtime 474ed96's IntPtr.cs, which forwards to the integer of the
    // target's width; NetWasm compiles one CoreLib for both widths, so it forwards on IntPtr.Size.
    public static nint Parse(string s) =>
        IntPtr.Size == 8 ? (nint)long.Parse(s) : (nint)int.Parse(s);

    public static nint Parse(string s, Globalization.NumberStyles style) =>
        IntPtr.Size == 8 ? (nint)long.Parse(s, style) : (nint)int.Parse(s, style);

    public static nint Parse(string s, IFormatProvider? provider) =>
        IntPtr.Size == 8 ? (nint)long.Parse(s, provider) : (nint)int.Parse(s, provider);

    public static nint Parse(string s, Globalization.NumberStyles style, IFormatProvider? provider) =>
        IntPtr.Size == 8 ? (nint)long.Parse(s, style, provider) : (nint)int.Parse(s, style, provider);

    public static nint Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        IntPtr.Size == 8 ? (nint)long.Parse(s, provider) : (nint)int.Parse(s, provider);

    public static nint Parse(ReadOnlySpan<char> s, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null) =>
        IntPtr.Size == 8 ? (nint)long.Parse(s, style, provider) : (nint)int.Parse(s, style, provider);

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, out nint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = long.TryParse(s, out long wideValue);
            result = (nint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = int.TryParse(s, out int narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out nint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = long.TryParse(s, provider, out long wideValue);
            result = (nint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = int.TryParse(s, provider, out int narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out nint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = long.TryParse(s, style, provider, out long wideValue);
            result = (nint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = int.TryParse(s, style, provider, out int narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParse(ReadOnlySpan<char> s, out nint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = long.TryParse(s, out long wideValue);
            result = (nint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = int.TryParse(s, out int narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out nint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = long.TryParse(utf8Text, out long wideValue);
            result = (nint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = int.TryParse(utf8Text, out int narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out nint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = long.TryParse(s, provider, out long wideValue);
            result = (nint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = int.TryParse(s, provider, out int narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParse(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out nint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = long.TryParse(s, style, provider, out long wideValue);
            result = (nint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = int.TryParse(s, style, provider, out int narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParsePartial([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out nint result, out int charsConsumed)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = long.TryParsePartial(s, style, provider, out long wideValue, out charsConsumed);
            result = (nint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = int.TryParsePartial(s, style, provider, out int narrowValue, out charsConsumed);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParsePartial(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out nint result, out int charsConsumed)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = long.TryParsePartial(s, style, provider, out long wideValue, out charsConsumed);
            result = (nint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = int.TryParsePartial(s, style, provider, out int narrowValue, out charsConsumed);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParsePartial(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out nint result, out int bytesConsumed)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = long.TryParsePartial(utf8Text, style, provider, out long wideValue, out bytesConsumed);
            result = (nint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = int.TryParsePartial(utf8Text, style, provider, out int narrowValue, out bytesConsumed);
        result = narrowValue;
        return parsedNarrow;
    }

    public static nint Parse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null) =>
        IntPtr.Size == 8 ? (nint)long.Parse(utf8Text, style, provider) : (nint)int.Parse(utf8Text, style, provider);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out nint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = long.TryParse(utf8Text, style, provider, out long wideValue);
            result = (nint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = int.TryParse(utf8Text, style, provider, out int narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static nint Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) =>
        IntPtr.Size == 8 ? (nint)long.Parse(utf8Text, provider) : (nint)int.Parse(utf8Text, provider);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out nint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = long.TryParse(utf8Text, provider, out long wideValue);
            result = (nint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = int.TryParse(utf8Text, provider, out int narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public unsafe IntPtr(void* value) => _value = (nint)value;
    public static unsafe explicit operator IntPtr(void* value) => new(value);
    public static unsafe explicit operator void*(IntPtr value) => (void*)value._value;
    public unsafe void* ToPointer() => (void*)_value;
    public string ToString(IFormatProvider? provider) => Size == 8 ? ((long)_value).ToString(provider) : ((int)_value).ToString(provider);
    public static nint Add(nint value, int offset) => new IntPtr(value._value + offset)._value;
    public static nint Subtract(nint value, int offset) => new IntPtr(value._value - offset)._value;
    // Upstream picks the width with TARGET_64BIT; one CoreLib serves both
    // targets here, and each folds nint.Size.
    public static nint BigMul(nint left, nint right, out nint low)
    {
        if (nint.Size == 8)
        {
            long high = Math.BigMul((long)left, (long)right, out long lower);
            low = (nint)lower;
            return (nint)high;
        }
        long result = Math.BigMul((int)left, (int)right);
        low = (int)result;
        return (int)(result >>> 32);
    }

    public static nint Log10(nint value) => (nint)PrimitiveIntegralConversions.Log10Signed((long)value._value);
    public static bool operator ==(nint left, nint right) => left._value == right._value;
    public static bool operator !=(nint left, nint right) => left._value != right._value;
    public static nint operator +(nint value, int offset) => value._value + offset;
    public static nint operator -(nint value, int offset) => value._value - offset;
}

public readonly partial struct UIntPtr
{
    // Parsing follows dotnet/runtime 474ed96's UIntPtr.cs, which forwards to the integer of the
    // target's width; NetWasm compiles one CoreLib for both widths, so it forwards on IntPtr.Size.
    public static nuint Parse(string s) =>
        IntPtr.Size == 8 ? (nuint)ulong.Parse(s) : (nuint)uint.Parse(s);

    public static nuint Parse(string s, Globalization.NumberStyles style) =>
        IntPtr.Size == 8 ? (nuint)ulong.Parse(s, style) : (nuint)uint.Parse(s, style);

    public static nuint Parse(string s, IFormatProvider? provider) =>
        IntPtr.Size == 8 ? (nuint)ulong.Parse(s, provider) : (nuint)uint.Parse(s, provider);

    public static nuint Parse(string s, Globalization.NumberStyles style, IFormatProvider? provider) =>
        IntPtr.Size == 8 ? (nuint)ulong.Parse(s, style, provider) : (nuint)uint.Parse(s, style, provider);

    public static nuint Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        IntPtr.Size == 8 ? (nuint)ulong.Parse(s, provider) : (nuint)uint.Parse(s, provider);

    public static nuint Parse(ReadOnlySpan<char> s, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null) =>
        IntPtr.Size == 8 ? (nuint)ulong.Parse(s, style, provider) : (nuint)uint.Parse(s, style, provider);

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, out nuint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = ulong.TryParse(s, out ulong wideValue);
            result = (nuint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = uint.TryParse(s, out uint narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out nuint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = ulong.TryParse(s, provider, out ulong wideValue);
            result = (nuint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = uint.TryParse(s, provider, out uint narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out nuint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = ulong.TryParse(s, style, provider, out ulong wideValue);
            result = (nuint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = uint.TryParse(s, style, provider, out uint narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParse(ReadOnlySpan<char> s, out nuint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = ulong.TryParse(s, out ulong wideValue);
            result = (nuint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = uint.TryParse(s, out uint narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out nuint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = ulong.TryParse(utf8Text, out ulong wideValue);
            result = (nuint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = uint.TryParse(utf8Text, out uint narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out nuint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = ulong.TryParse(s, provider, out ulong wideValue);
            result = (nuint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = uint.TryParse(s, provider, out uint narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParse(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out nuint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = ulong.TryParse(s, style, provider, out ulong wideValue);
            result = (nuint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = uint.TryParse(s, style, provider, out uint narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParsePartial([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out nuint result, out int charsConsumed)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = ulong.TryParsePartial(s, style, provider, out ulong wideValue, out charsConsumed);
            result = (nuint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = uint.TryParsePartial(s, style, provider, out uint narrowValue, out charsConsumed);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParsePartial(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out nuint result, out int charsConsumed)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = ulong.TryParsePartial(s, style, provider, out ulong wideValue, out charsConsumed);
            result = (nuint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = uint.TryParsePartial(s, style, provider, out uint narrowValue, out charsConsumed);
        result = narrowValue;
        return parsedNarrow;
    }

    public static bool TryParsePartial(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out nuint result, out int bytesConsumed)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = ulong.TryParsePartial(utf8Text, style, provider, out ulong wideValue, out bytesConsumed);
            result = (nuint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = uint.TryParsePartial(utf8Text, style, provider, out uint narrowValue, out bytesConsumed);
        result = narrowValue;
        return parsedNarrow;
    }

    public static nuint Parse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null) =>
        IntPtr.Size == 8 ? (nuint)ulong.Parse(utf8Text, style, provider) : (nuint)uint.Parse(utf8Text, style, provider);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out nuint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = ulong.TryParse(utf8Text, style, provider, out ulong wideValue);
            result = (nuint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = uint.TryParse(utf8Text, style, provider, out uint narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public static nuint Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) =>
        IntPtr.Size == 8 ? (nuint)ulong.Parse(utf8Text, provider) : (nuint)uint.Parse(utf8Text, provider);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out nuint result)
    {
        if (IntPtr.Size == 8)
        {
            bool parsedWide = ulong.TryParse(utf8Text, provider, out ulong wideValue);
            result = (nuint)wideValue;
            return parsedWide;
        }
        bool parsedNarrow = uint.TryParse(utf8Text, provider, out uint narrowValue);
        result = narrowValue;
        return parsedNarrow;
    }

    public unsafe UIntPtr(void* value) => _value = (nuint)value;
    public static unsafe explicit operator UIntPtr(void* value) => new(value);
    public static unsafe explicit operator void*(UIntPtr value) => (void*)value._value;
    public unsafe void* ToPointer() => (void*)_value;
    public string ToString(IFormatProvider? provider) => Size == 8 ? ((ulong)_value).ToString(provider) : ((uint)_value).ToString(provider);
    public static nuint Add(nuint value, int offset) => new UIntPtr(value._value + (nuint)offset)._value;
    public static nuint Subtract(nuint value, int offset) => new UIntPtr(value._value - (nuint)offset)._value;
    public static nuint BigMul(nuint left, nuint right, out nuint low)
    {
        if (nint.Size == 8)
        {
            ulong high = Math.BigMul((ulong)left, (ulong)right, out ulong lower);
            low = (nuint)lower;
            return (nuint)high;
        }
        ulong result = Math.BigMul((uint)left, (uint)right);
        low = (uint)result;
        return (uint)(result >>> 32);
    }

    public static nuint Log10(nuint value) => (nuint)PrimitiveIntegralConversions.Log10((ulong)value._value);
    public static bool operator ==(nuint left, nuint right) => left._value == right._value;
    public static bool operator !=(nuint left, nuint right) => left._value != right._value;
    public static nuint operator +(nuint value, int offset) => value._value + (nuint)offset;
    public static nuint operator -(nuint value, int offset) => value._value - (nuint)offset;
}

public readonly partial struct UInt32 : IBinaryIntegerParseAndFormatInfo<uint>
{
    // Parsing is dotnet/runtime 474ed96's UInt32.cs, over Number.Parsing's integer parser.
    public static uint Parse(string s) => Parse(s, Globalization.NumberStyles.Integer, provider: null);

    public static uint Parse(string s, Globalization.NumberStyles style) => Parse(s, style, provider: null);

    public static uint Parse(string s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static uint Parse(string s, Globalization.NumberStyles style, IFormatProvider? provider)
    {
        if (s is null) { throw new ArgumentNullException(nameof(s)); }
        return Parse(s.AsSpan(), style, provider);
    }

    public static uint Parse(ReadOnlySpan<char> s, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<char, uint>(s, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, out uint result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out uint result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out uint result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out uint result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParse(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out uint result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out uint result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out uint result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out uint result, out int bytesConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out bytesConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out uint result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static uint Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out uint result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static uint Parse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<byte, uint>(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out uint result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static uint Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out uint result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider, out result);

    static bool IBinaryIntegerParseAndFormatInfo<uint>.IsSigned => false;

    static int IBinaryIntegerParseAndFormatInfo<uint>.MaxDigitCount => 10; // 4_294_967_295

    static int IBinaryIntegerParseAndFormatInfo<uint>.MaxHexDigitCount => 8; // 0xFFFF_FFFF

    static uint IBinaryIntegerParseAndFormatInfo<uint>.MaxValueDiv10 => MaxValue / 10;

    static string IBinaryIntegerParseAndFormatInfo<uint>.OverflowMessage => SR.Overflow_UInt32;

    static bool IBinaryIntegerParseAndFormatInfo<uint>.IsGreaterThanAsUnsigned(uint left, uint right) => left > right;

    static uint IBinaryIntegerParseAndFormatInfo<uint>.MultiplyBy10(uint value) => value * 10;

    static uint IBinaryIntegerParseAndFormatInfo<uint>.MultiplyBy16(uint value) => value * 16;

    public TypeCode GetTypeCode() => TypeCode.UInt32;
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Number.TryFormatUInt32(this, format, provider, destination, out bytesWritten);
    public static ulong BigMul(uint left, uint right) => (ulong)left * right;

    public static uint Log10(uint value) => (uint)PrimitiveIntegralConversions.Log10(value);
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

public readonly partial struct Int64 : IBinaryIntegerParseAndFormatInfo<long>
{
    // Parsing is dotnet/runtime 474ed96's Int64.cs, over Number.Parsing's integer parser.
    public static long Parse(string s) => Parse(s, Globalization.NumberStyles.Integer, provider: null);

    public static long Parse(string s, Globalization.NumberStyles style) => Parse(s, style, provider: null);

    public static long Parse(string s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static long Parse(string s, Globalization.NumberStyles style, IFormatProvider? provider)
    {
        if (s is null) { throw new ArgumentNullException(nameof(s)); }
        return Parse(s.AsSpan(), style, provider);
    }

    public static long Parse(ReadOnlySpan<char> s, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<char, long>(s, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, out long result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out long result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out long result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out long result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParse(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out long result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out long result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out long result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out long result, out int bytesConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out bytesConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out long result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static long Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out long result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static long Parse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<byte, long>(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out long result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static long Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out long result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider, out result);

    static bool IBinaryIntegerParseAndFormatInfo<long>.IsSigned => true;

    static int IBinaryIntegerParseAndFormatInfo<long>.MaxDigitCount => 19; // 9_223_372_036_854_775_807

    static int IBinaryIntegerParseAndFormatInfo<long>.MaxHexDigitCount => 16; // 0x7FFF_FFFF_FFFF_FFFF

    static long IBinaryIntegerParseAndFormatInfo<long>.MaxValueDiv10 => MaxValue / 10;

    static string IBinaryIntegerParseAndFormatInfo<long>.OverflowMessage => SR.Overflow_Int64;

    static bool IBinaryIntegerParseAndFormatInfo<long>.IsGreaterThanAsUnsigned(long left, long right) => (ulong)(left) > (ulong)(right);

    static long IBinaryIntegerParseAndFormatInfo<long>.MultiplyBy10(long value) => value * 10;

    static long IBinaryIntegerParseAndFormatInfo<long>.MultiplyBy16(long value) => value * 16;

    public TypeCode GetTypeCode() => TypeCode.Int64;
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Number.TryFormatInt64(this, format, provider, destination, out bytesWritten);
    public static Int128 BigMul(long left, long right) => (Int128)left * right;

    public static long Log10(long value) => PrimitiveIntegralConversions.Log10Signed(value);
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

public readonly partial struct UInt64 : IBinaryIntegerParseAndFormatInfo<ulong>
{
    // Parsing is dotnet/runtime 474ed96's UInt64.cs, over Number.Parsing's integer parser.
    public static ulong Parse(string s) => Parse(s, Globalization.NumberStyles.Integer, provider: null);

    public static ulong Parse(string s, Globalization.NumberStyles style) => Parse(s, style, provider: null);

    public static ulong Parse(string s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static ulong Parse(string s, Globalization.NumberStyles style, IFormatProvider? provider)
    {
        if (s is null) { throw new ArgumentNullException(nameof(s)); }
        return Parse(s.AsSpan(), style, provider);
    }

    public static ulong Parse(ReadOnlySpan<char> s, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<char, ulong>(s, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, out ulong result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ulong result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out ulong result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out ulong result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParse(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out ulong result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out ulong result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out ulong result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out ulong result, out int bytesConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out bytesConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out ulong result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static ulong Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ulong result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static ulong Parse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<byte, ulong>(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out ulong result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static ulong Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out ulong result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider, out result);

    static bool IBinaryIntegerParseAndFormatInfo<ulong>.IsSigned => false;

    static int IBinaryIntegerParseAndFormatInfo<ulong>.MaxDigitCount => 20; // 18_446_744_073_709_551_615

    static int IBinaryIntegerParseAndFormatInfo<ulong>.MaxHexDigitCount => 16; // 0xFFFF_FFFF_FFFF_FFFF

    static ulong IBinaryIntegerParseAndFormatInfo<ulong>.MaxValueDiv10 => MaxValue / 10;

    static string IBinaryIntegerParseAndFormatInfo<ulong>.OverflowMessage => SR.Overflow_UInt64;

    static bool IBinaryIntegerParseAndFormatInfo<ulong>.IsGreaterThanAsUnsigned(ulong left, ulong right) => left > right;

    static ulong IBinaryIntegerParseAndFormatInfo<ulong>.MultiplyBy10(ulong value) => value * 10;

    static ulong IBinaryIntegerParseAndFormatInfo<ulong>.MultiplyBy16(ulong value) => value * 16;

    public TypeCode GetTypeCode() => TypeCode.UInt64;
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Number.TryFormatUInt64(this, format, provider, destination, out bytesWritten);
    public static UInt128 BigMul(ulong left, ulong right) => (UInt128)left * right;

    public static ulong Log10(ulong value) => (ulong)PrimitiveIntegralConversions.Log10(value);
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

public readonly partial struct Int32
{

    // Number's format-free entry point: what the default format writes, without the format string parser.
    public string ToString(IFormatProvider? provider) => Number.FormatInt32WithoutFormat(this, provider);

    public string ToString(string? format, IFormatProvider? provider) =>
        Number.FormatInt32(this, ~0, format, provider);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
        IFormatProvider? provider = null) =>
        Number.TryFormatInt32(this, ~0, format, provider, destination, out charsWritten);

}

public readonly partial struct UInt32
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

}

public readonly partial struct Int64
{
    // Number's format-free entry point: what the default format writes, without the format string parser.
    public string ToString(IFormatProvider? provider) => Number.FormatInt64WithoutFormat(this, provider);

    public string ToString(string? format, IFormatProvider? provider) =>
        Number.FormatInt64(this, format, provider);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
        IFormatProvider? provider = null) =>
        Number.TryFormatInt64(this, format, provider, destination, out charsWritten);

}

public readonly partial struct UInt32
{
    // Lookup table for power-of-10 boundaries corrections
    internal static ReadOnlySpan<uint> PowersOf10 =>
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
    ];
}

public readonly partial struct UInt64
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

}

public readonly partial struct Single
{
    // Parsing is dotnet/runtime 474ed96's Single.cs, over Number.Parsing's floating-point parser.
    public static float Parse(string s) => Parse(s, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider: null);

    public static float Parse(string s, Globalization.NumberStyles style) => Parse(s, style, provider: null);

    public static float Parse(string s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider);

    public static float Parse(string s, Globalization.NumberStyles style, IFormatProvider? provider)
    {
        if (s is null)
        {
            throw new ArgumentNullException(nameof(s));
        }
        return Parse(s.AsSpan(), style, provider);
    }

    public static float Parse(ReadOnlySpan<char> s, Globalization.NumberStyles style = Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.ParseFloat<char, float>(s, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, out float result) => TryParse(s, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out float result) => TryParse(s, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out float result) => TryParse(utf8Text, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider: null, out result);

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out float result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.TryParseFloat(s.AsSpan(), style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _);
    }

    public static bool TryParse(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out float result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.TryParseFloat(s, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _);
    }

    public static bool TryParsePartial([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out float result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.TryParseFloat(s.AsSpan(), style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed);
    }

    public static bool TryParsePartial(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out float result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.TryParseFloat(s, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed);
    }

    public static bool TryParsePartial(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out float result, out int bytesConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.TryParseFloat(utf8Text, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out bytesConsumed);
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out float result) => TryParse(s, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider, out result);

    public static float Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out float result) => TryParse(s, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider, out result);

    public static float Parse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style = Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.ParseFloat<byte, float>(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out float result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.TryParseFloat(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _);
    }

    public static float Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out float result) => TryParse(utf8Text, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider, out result);

    public string ToString(IFormatProvider? provider) => provider is null ? ToString() : Number.FormatFloat(this, null, provider);

    public string ToString(string? format, IFormatProvider? provider) =>
        Number.FormatFloat(this, format, provider);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
        IFormatProvider? provider = null) =>
        Number.TryFormatFloat(this, format, provider, destination, out charsWritten);

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

    public static TInteger ConvertToInteger<TInteger>(float value) where TInteger : Numerics.IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);
    public static TInteger ConvertToIntegerNative<TInteger>(float value) where TInteger : Numerics.IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

    public static float ClampNative(float value, float min, float max) => min > max ? throw new ArgumentException() : MinNative(MaxNative(value, min), max);
    public static float MaxNative(float left, float right) => left > right ? left : right;
    public static float MinNative(float left, float right) => left < right ? left : right;
    public static float MaxNumber(float x, float y)
    {
        // This matches the IEEE 754:2019 `maximumNumber` function
        //
        // It does not propagate NaN inputs back to the caller and
        // otherwise returns the larger of the inputs. It
        // treats +0 as larger than -0 as per the specification.

        if (x != y)
        {
            if (!IsNaN(y))
            {
                return y < x ? x : y;
            }

            return x;
        }

        return IsNegative(y) ? x : y;
    }
    public static float MinNumber(float x, float y)
    {
        // This matches the IEEE 754:2019 `minimumNumber` function
        //
        // It does not propagate NaN inputs back to the caller and
        // otherwise returns the larger of the inputs. It
        // treats +0 as larger than -0 as per the specification.

        if (x != y)
        {
            if (!IsNaN(y))
            {
                return x < y ? x : y;
            }

            return x;
        }

        return IsNegative(x) ? x : y;
    }

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

public readonly partial struct Double
{
    // Parsing is dotnet/runtime 474ed96's Double.cs, over Number.Parsing's floating-point parser.
    public static double Parse(string s) => Parse(s, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider: null);

    public static double Parse(string s, Globalization.NumberStyles style) => Parse(s, style, provider: null);

    public static double Parse(string s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider);

    public static double Parse(string s, Globalization.NumberStyles style, IFormatProvider? provider)
    {
        if (s is null)
        {
            throw new ArgumentNullException(nameof(s));
        }
        return Parse(s.AsSpan(), style, provider);
    }

    public static double Parse(ReadOnlySpan<char> s, Globalization.NumberStyles style = Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.ParseFloat<char, double>(s, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, out double result) => TryParse(s, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out double result) => TryParse(s, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out double result) => TryParse(utf8Text, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider: null, out result);

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out double result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.TryParseFloat(s.AsSpan(), style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _);
    }

    public static bool TryParse(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out double result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.TryParseFloat(s, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _);
    }

    public static bool TryParsePartial([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out double result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.TryParseFloat(s.AsSpan(), style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed);
    }

    public static bool TryParsePartial(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out double result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.TryParseFloat(s, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed);
    }

    public static bool TryParsePartial(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out double result, out int bytesConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.TryParseFloat(utf8Text, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out bytesConsumed);
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out double result) => TryParse(s, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider, out result);

    public static double Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out double result) => TryParse(s, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider, out result);

    public static double Parse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style = Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.ParseFloat<byte, double>(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out double result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleFloatingPoint(style);
        return Number.TryParseFloat(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _);
    }

    public static double Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out double result) => TryParse(utf8Text, Globalization.NumberStyles.Float | Globalization.NumberStyles.AllowThousands, provider, out result);

    public string ToString(IFormatProvider? provider) => provider is null ? ToString() : Number.FormatFloat(this, null, provider);

    public string ToString(string? format, IFormatProvider? provider) =>
        Number.FormatFloat(this, format, provider);

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>),
        IFormatProvider? provider = null) =>
        Number.TryFormatFloat(this, format, provider, destination, out charsWritten);

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

    public static TInteger ConvertToInteger<TInteger>(double value) where TInteger : Numerics.IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);
    public static TInteger ConvertToIntegerNative<TInteger>(double value) where TInteger : Numerics.IBinaryInteger<TInteger> => TInteger.CreateSaturating(value);

    public static double ClampNative(double value, double min, double max) => min > max ? throw new ArgumentException() : MinNative(MaxNative(value, min), max);
    public static double MaxNative(double left, double right) => left > right ? left : right;
    public static double MinNative(double left, double right) => left < right ? left : right;
    public static double MaxNumber(double x, double y)
    {
        // This matches the IEEE 754:2019 `maximumNumber` function
        //
        // It does not propagate NaN inputs back to the caller and
        // otherwise returns the larger of the inputs. It
        // treats +0 as larger than -0 as per the specification.

        if (x != y)
        {
            if (!IsNaN(y))
            {
                return y < x ? x : y;
            }

            return x;
        }

        return IsNegative(y) ? x : y;
    }
    public static double MinNumber(double x, double y)
    {
        // This matches the IEEE 754:2019 `minimumNumber` function
        //
        // It does not propagate NaN inputs back to the caller and
        // otherwise returns the larger of the inputs. It
        // treats +0 as larger than -0 as per the specification.

        if (x != y)
        {
            if (!IsNaN(y))
            {
                return x < y ? x : y;
            }

            return x;
        }

        return IsNegative(x) ? x : y;
    }
    public static double Lerp(double value1, double value2, double amount) => MultiplyAddEstimate(value1, 1D - amount, value2 * amount);
    public static double LogP1(double value) => Math.Log(1D + value);
    public static double Log2P1(double value) => Math.Log2(1D + value);
    public static double Log10P1(double value) => Math.Log10(1D + value);
    // Upstream's Mono build: the product rounds before the addend is added, as
    // the vector estimates do.
    public static double MultiplyAddEstimate(double left, double right, double addend) => (left * right) + addend;
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

public readonly partial struct Single
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

    internal byte BiasedExponent
    {
        get
        {
            uint bits = BitConverter.SingleToUInt32Bits(this);
            return ExtractBiasedExponentFromBits(bits);
        }
    }

    internal sbyte Exponent
    {
        get
        {
            return (sbyte)(BiasedExponent - ExponentBias);
        }
    }

    internal uint Significand
    {
        get
        {
            return TrailingSignificand | ((BiasedExponent != 0) ? (1U << BiasedExponentShift) : 0U);
        }
    }

    internal uint TrailingSignificand
    {
        get
        {
            uint bits = BitConverter.SingleToUInt32Bits(this);
            return ExtractTrailingSignificandFromBits(bits);
        }
    }

    internal static byte ExtractBiasedExponentFromBits(uint bits)
    {
        return (byte)((bits >> BiasedExponentShift) & ShiftedBiasedExponentMask);
    }

    internal static uint ExtractTrailingSignificandFromBits(uint bits)
    {
        return bits & TrailingSignificandMask;
    }

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
    public static float MultiplyAddEstimate(float left, float right, float addend) => (left * right) + addend;
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

public readonly partial struct Double
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

    internal ushort BiasedExponent
    {
        get
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(this);
            return ExtractBiasedExponentFromBits(bits);
        }
    }

    internal short Exponent
    {
        get
        {
            return (short)(BiasedExponent - ExponentBias);
        }
    }

    internal ulong Significand
    {
        get
        {
            return TrailingSignificand | ((BiasedExponent != 0) ? (1UL << BiasedExponentShift) : 0UL);
        }
    }

    internal ulong TrailingSignificand
    {
        get
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(this);
            return ExtractTrailingSignificandFromBits(bits);
        }
    }

    internal static ushort ExtractBiasedExponentFromBits(ulong bits)
    {
        return (ushort)((bits >> BiasedExponentShift) & ShiftedBiasedExponentMask);
    }

    internal static ulong ExtractTrailingSignificandFromBits(ulong bits)
    {
        return bits & TrailingSignificandMask;
    }

    internal static double CreateDouble(bool sign, ushort exponent, ulong significand) =>
        BitConverter.UInt64BitsToDouble((sign ? SignMask : 0UL) +
            ((ulong)exponent << BiasedExponentShift) + significand);

    public static double ExpM1(double value) => Math.Exp(value) - 1D;
    public static double Exp2M1(double value) => Math.Pow(2D, value) - 1D;
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

public readonly partial struct Byte : IBinaryIntegerParseAndFormatInfo<byte>
{
    // Parsing is dotnet/runtime 474ed96's Byte.cs, over Number.Parsing's integer parser.
    public static byte Parse(string s) => Parse(s, Globalization.NumberStyles.Integer, provider: null);

    public static byte Parse(string s, Globalization.NumberStyles style) => Parse(s, style, provider: null);

    public static byte Parse(string s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static byte Parse(string s, Globalization.NumberStyles style, IFormatProvider? provider)
    {
        if (s is null) { throw new ArgumentNullException(nameof(s)); }
        return Parse(s.AsSpan(), style, provider);
    }

    public static byte Parse(ReadOnlySpan<char> s, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<char, byte>(s, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, out byte result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out byte result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out byte result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out byte result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParse(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out byte result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out byte result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out byte result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out byte result, out int bytesConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out bytesConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out byte result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static byte Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out byte result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static byte Parse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<byte, byte>(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out byte result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static byte Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out byte result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider, out result);

    static bool IBinaryIntegerParseAndFormatInfo<byte>.IsSigned => false;

    static int IBinaryIntegerParseAndFormatInfo<byte>.MaxDigitCount => 3; // 255

    static int IBinaryIntegerParseAndFormatInfo<byte>.MaxHexDigitCount => 2; // 0xFF

    static byte IBinaryIntegerParseAndFormatInfo<byte>.MaxValueDiv10 => MaxValue / 10;

    static string IBinaryIntegerParseAndFormatInfo<byte>.OverflowMessage => SR.Overflow_Byte;

    static bool IBinaryIntegerParseAndFormatInfo<byte>.IsGreaterThanAsUnsigned(byte left, byte right) => left > right;

    static byte IBinaryIntegerParseAndFormatInfo<byte>.MultiplyBy10(byte value) => (byte)(value * 10);

    static byte IBinaryIntegerParseAndFormatInfo<byte>.MultiplyBy16(byte value) => (byte)(value * 16);

    public TypeCode GetTypeCode() => TypeCode.Byte;
    public static byte Log10(byte value) => (byte)PrimitiveIntegralConversions.Log10(value);
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

public readonly partial struct SByte : IBinaryIntegerParseAndFormatInfo<sbyte>
{
    // Parsing is dotnet/runtime 474ed96's SByte.cs, over Number.Parsing's integer parser.
    public static sbyte Parse(string s) => Parse(s, Globalization.NumberStyles.Integer, provider: null);

    public static sbyte Parse(string s, Globalization.NumberStyles style) => Parse(s, style, provider: null);

    public static sbyte Parse(string s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static sbyte Parse(string s, Globalization.NumberStyles style, IFormatProvider? provider)
    {
        if (s is null) { throw new ArgumentNullException(nameof(s)); }
        return Parse(s.AsSpan(), style, provider);
    }

    public static sbyte Parse(ReadOnlySpan<char> s, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<char, sbyte>(s, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, out sbyte result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out sbyte result) => TryParse(s, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out sbyte result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider: null, out result);

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out sbyte result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParse(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out sbyte result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, Globalization.NumberStyles style, IFormatProvider? provider, out sbyte result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s.AsSpan(), style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<char> s, Globalization.NumberStyles style, IFormatProvider? provider, out sbyte result, out int charsConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(s, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out charsConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParsePartial(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out sbyte result, out int bytesConsumed)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style | Number.AllowTrailingInvalidCharacters, Globalization.NumberFormatInfo.GetInstance(provider), out result, out bytesConsumed) == Number.ParsingStatus.OK;
    }

    public static bool TryParse([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? s, IFormatProvider? provider, out sbyte result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static sbyte Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out sbyte result) => TryParse(s, Globalization.NumberStyles.Integer, provider, out result);

    public static sbyte Parse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style = Globalization.NumberStyles.Integer, IFormatProvider? provider = null)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.ParseBinaryInteger<byte, sbyte>(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider));
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, Globalization.NumberStyles style, IFormatProvider? provider, out sbyte result)
    {
        Globalization.NumberFormatInfo.ValidateParseStyleInteger(style);
        return Number.TryParseBinaryInteger(utf8Text, style, Globalization.NumberFormatInfo.GetInstance(provider), out result, out _) == Number.ParsingStatus.OK;
    }

    public static sbyte Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text, Globalization.NumberStyles.Integer, provider);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out sbyte result) => TryParse(utf8Text, Globalization.NumberStyles.Integer, provider, out result);

    static bool IBinaryIntegerParseAndFormatInfo<sbyte>.IsSigned => true;

    static int IBinaryIntegerParseAndFormatInfo<sbyte>.MaxDigitCount => 3; // 127

    static int IBinaryIntegerParseAndFormatInfo<sbyte>.MaxHexDigitCount => 2; // 0x7F

    static sbyte IBinaryIntegerParseAndFormatInfo<sbyte>.MaxValueDiv10 => MaxValue / 10;

    static string IBinaryIntegerParseAndFormatInfo<sbyte>.OverflowMessage => SR.Overflow_SByte;

    static bool IBinaryIntegerParseAndFormatInfo<sbyte>.IsGreaterThanAsUnsigned(sbyte left, sbyte right) => (byte)(left) > (byte)(right);

    static sbyte IBinaryIntegerParseAndFormatInfo<sbyte>.MultiplyBy10(sbyte value) => (sbyte)(value * 10);

    static sbyte IBinaryIntegerParseAndFormatInfo<sbyte>.MultiplyBy16(sbyte value) => (sbyte)(value * 16);

    public TypeCode GetTypeCode() => TypeCode.SByte;
    public static sbyte Log10(sbyte value) => (sbyte)PrimitiveIntegralConversions.Log10Signed(value);
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

public readonly partial struct Int16
{
    public TypeCode GetTypeCode() => TypeCode.Int16;
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Number.TryFormatInt32(this, 0x0000FFFF, format, provider, destination, out bytesWritten);

    public static short Log10(short value) => (short)PrimitiveIntegralConversions.Log10Signed(value);
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

public readonly partial struct UInt16
{
    public TypeCode GetTypeCode() => TypeCode.UInt16;
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        Number.TryFormatUInt32(this, format, provider, destination, out bytesWritten);

    public static ushort Log10(ushort value) => (ushort)PrimitiveIntegralConversions.Log10(value);
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
