// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Small scalar generic-math contracts are adapted from the pinned
// dotnet/runtime System.Private.CoreLib sources (commit
// 811225a482702af7ecc35d817966bc70b88a3a23).

using System.Globalization;
using System.Numerics;

namespace System;

internal static class PrimitiveGenericMathSmall
{
    // The reduced CoreLib String surface does not expose the runtime's
    // UTF-8/string constructors. Numeric UTF-8 parsers only need the
    // invariant ASCII projection used by these contracts.
    internal static string CreateStringFromUtf8(ReadOnlySpan<byte> value)
    {
        var characters = new char[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            characters[index] = (char)value[index];
        }

        return string.Create(characters);
    }

    internal static char ParseUtf8(ReadOnlySpan<byte> value)
    {
        if (Text.Rune.DecodeFromUtf8(value, out var rune, out var consumed) != Buffers.OperationStatus.Done ||
            consumed != value.Length ||
            !rune.IsBmp)
        {
            throw new FormatException();
        }

        return (char)rune.Value;
    }

    internal static bool TryCopy(string value, Span<char> destination, out int charsWritten)
    {
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

    internal static bool TryCopyUtf8(string value, Span<byte> destination, out int bytesWritten)
    {
        if (destination.Length < value.Length)
        {
            bytesWritten = 0;
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            destination[index] = (byte)value[index];
        }

        bytesWritten = value.Length;
        return true;
    }

    internal static void ValidateIntegerStyle(NumberStyles style)
    {
        const NumberStyles allowed = NumberStyles.AllowLeadingWhite |
            NumberStyles.AllowTrailingWhite |
            NumberStyles.AllowLeadingSign |
            NumberStyles.AllowTrailingSign |
            NumberStyles.AllowParentheses |
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowThousands |
            NumberStyles.AllowExponent |
            NumberStyles.AllowCurrencySymbol |
            NumberStyles.AllowHexSpecifier |
            NumberStyles.AllowBinarySpecifier;

        if ((style & ~allowed) != 0)
        {
            throw new ArgumentException(nameof(style));
        }

        var radixFlags = style & (NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier);
        if (radixFlags != 0 &&
            (radixFlags == (NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier) ||
             (style & ~(NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
                        NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier)) != 0))
        {
            throw new ArgumentException(nameof(style));
        }
    }

    internal static bool TryParseUnsigned(ReadOnlySpan<char> text, NumberStyles style, ulong maximum, out ulong value)
    {
        ValidateIntegerStyle(style);
        value = 0;

        var start = 0;
        var end = text.Length;
        if (!Trim(text, style, ref start, ref end))
        {
            return false;
        }

        var radixFlags = style & (NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier);
        var radix = radixFlags == NumberStyles.AllowHexSpecifier ? 16 :
            radixFlags == NumberStyles.AllowBinarySpecifier ? 2 : 10;
        var allowSign = radixFlags == 0 && (style & NumberStyles.AllowLeadingSign) != 0;
        var negative = false;

        if ((style & NumberStyles.AllowCurrencySymbol) != 0 && start < end && text[start] == '$')
        {
            start++;
        }

        if (allowSign && start < end && (text[start] == '+' || text[start] == '-'))
        {
            negative = text[start] == '-';
            start++;
        }

        if ((style & NumberStyles.AllowCurrencySymbol) != 0 && start < end && text[start] == '$')
        {
            start++;
        }

        if ((style & NumberStyles.AllowParentheses) != 0 && start < end && text[start] == '(')
        {
            if (end <= start + 1 || text[end - 1] != ')')
            {
                return false;
            }

            if (negative)
            {
                return false;
            }

            negative = true;
            start++;
            end--;
        }

        if ((style & NumberStyles.AllowTrailingSign) != 0 && start < end && (text[end - 1] == '+' || text[end - 1] == '-'))
        {
            if (text[end - 1] == '-')
            {
                return false;
            }

            end--;
        }

        if ((style & NumberStyles.AllowCurrencySymbol) != 0 && end > start && text[end - 1] == '$')
        {
            end--;
        }

        if (negative)
        {
            return false;
        }

        var sawDigit = false;
        var decimalPoint = false;
        var fractionalDigits = 0L;
        var exponent = 0;
        var exponentSign = 1;
        var overflow = false;
        var index = start;

        for (; index < end; index++)
        {
            var current = text[index];
            if ((style & NumberStyles.AllowThousands) != 0 && current == ',')
            {
                if (radix != 10 || decimalPoint || index == start ||
                    !IsDecimalDigit(text[index - 1]) || index + 1 >= end || !IsDecimalDigit(text[index + 1]))
                {
                    return false;
                }

                continue;
            }

            if (radix == 10 && (style & NumberStyles.AllowDecimalPoint) != 0 && current == '.' && !decimalPoint)
            {
                decimalPoint = true;
                continue;
            }

            if (radix == 10 && (style & NumberStyles.AllowExponent) != 0 && (current == 'e' || current == 'E'))
            {
                index++;
                if (index < end && (text[index] == '+' || text[index] == '-'))
                {
                    exponentSign = text[index] == '-' ? -1 : 1;
                    index++;
                }

                var exponentDigits = 0;
                while (index < end && text[index] >= '0' && text[index] <= '9')
                {
                    exponent = exponent > 1_000_000 ? 1_000_001 : exponent * 10 + text[index] - '0';
                    exponentDigits++;
                    index++;
                }

                if (exponentDigits == 0 || index != end)
                {
                    return false;
                }

                break;
            }

            var digit = Digit(current, radix);
            if (digit < 0)
            {
                return false;
            }

            sawDigit = true;
            if (decimalPoint)
            {
                fractionalDigits++;
            }

            if (!overflow)
            {
                if (value > (ulong.MaxValue - (ulong)digit) / (ulong)radix)
                {
                    overflow = true;
                }
                else
                {
                    value = value * (ulong)radix + (ulong)digit;
                }
            }
        }

        if (!sawDigit)
        {
            value = 0;
            return false;
        }

        if (overflow)
        {
            value = 0;
            return false;
        }

        var scale = exponentSign < 0 ? -(long)exponent : (long)exponent;
        scale -= fractionalDigits;
        if (value == 0)
        {
            return true;
        }

        if (scale < 0)
        {
            while (scale++ < 0)
            {
                if (value % 10 != 0)
                {
                    value = 0;
                    return false;
                }

                value /= 10;
            }
        }
        else if (value != 0)
        {
            while (scale-- > 0)
            {
                if (value > maximum / 10)
                {
                    value = 0;
                    return false;
                }

                value *= 10;
            }
        }

        return true;
    }

    internal static bool TryParseSigned(ReadOnlySpan<char> text, NumberStyles style, int bits, out long value)
    {
        ValidateIntegerStyle(style);
        value = 0;

        var start = 0;
        var end = text.Length;
        if (!Trim(text, style, ref start, ref end))
        {
            return false;
        }

        var radixFlags = style & (NumberStyles.AllowHexSpecifier | NumberStyles.AllowBinarySpecifier);
        var radix = radixFlags == NumberStyles.AllowHexSpecifier ? 16 :
            radixFlags == NumberStyles.AllowBinarySpecifier ? 2 : 10;

        if (radixFlags != 0)
        {
            if (!TryParseUnsigned(text.Slice(start, end - start), style & ~(NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite), ulong.MaxValue, out var raw))
            {
                return false;
            }

            var mask = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;
            if (raw > mask)
            {
                return false;
            }

            var signBit = 1UL << (bits - 1);
            value = (raw & signBit) == 0 ? (long)raw : (long)(raw | ~mask);
            return true;
        }

        var parenthesized = false;
        if ((style & NumberStyles.AllowParentheses) != 0 && start < end && text[start] == '(')
        {
            if (end <= start + 1 || text[end - 1] != ')')
            {
                return false;
            }

            parenthesized = true;
            start++;
            end--;
        }

        if ((style & NumberStyles.AllowCurrencySymbol) != 0 && start < end && text[start] == '$')
        {
            start++;
        }

        var negative = false;
        if (start < end && (text[start] == '+' || text[start] == '-'))
        {
            if (parenthesized || (style & NumberStyles.AllowLeadingSign) == 0)
            {
                return false;
            }

            negative = text[start] == '-';
            start++;
        }

        if ((style & NumberStyles.AllowCurrencySymbol) != 0 && start < end && text[start] == '$')
        {
            start++;
        }

        if ((style & NumberStyles.AllowTrailingSign) != 0 && start < end && (text[end - 1] == '+' || text[end - 1] == '-'))
        {
            var trailingNegative = text[end - 1] == '-';
            if (parenthesized || negative)
            {
                return false;
            }

            negative = trailingNegative;
            end--;
        }

        if ((style & NumberStyles.AllowCurrencySymbol) != 0 && end > start && text[end - 1] == '$')
        {
            end--;
        }

        if (parenthesized)
        {
            negative = true;
        }

        var maximum = negative ? (1UL << (bits - 1)) : (1UL << (bits - 1)) - 1;
        if (!TryParseUnsigned(text.Slice(start, end - start), style & ~(NumberStyles.AllowLeadingSign | NumberStyles.AllowTrailingSign | NumberStyles.AllowParentheses), maximum, out var magnitude))
        {
            return false;
        }

        if (negative)
        {
            value = magnitude == (1UL << (bits - 1)) ? -(1L << (bits - 1)) : -(long)magnitude;
        }
        else
        {
            value = (long)magnitude;
        }

        return true;
    }

    internal static bool TryParseUtf8(ReadOnlySpan<byte> text, NumberStyles style, out ulong value, ulong maximum)
    {
        ValidateIntegerStyle(style);
        var chars = new char[text.Length];
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] > 0x7F)
            {
                value = 0;
                return false;
            }

            chars[index] = (char)text[index];
        }

        return TryParseUnsigned(chars, style, maximum, out value);
    }

    internal static byte ParseByte(ReadOnlySpan<char> text, NumberStyles style)
    {
        if (TryParseUnsigned(text, style, byte.MaxValue, out var value))
        {
            return (byte)value;
        }

        // Preserve the Parse/TryParse distinction made by the BCL: a syntactically
        // valid value outside the target range is an overflow, not a format error.
        if (TryParseUnsigned(text, style, ulong.MaxValue, out _) ||
            TryParseSigned(text, style, 64, out _))
        {
            throw new OverflowException();
        }

        throw new FormatException();
    }

    internal static sbyte ParseSByte(ReadOnlySpan<char> text, NumberStyles style)
    {
        if (TryParseSigned(text, style, 8, out var value))
        {
            return (sbyte)value;
        }

        if (TryParseUnsigned(text, style, ulong.MaxValue, out _) ||
            TryParseSigned(text, style, 64, out _))
        {
            throw new OverflowException();
        }

        throw new FormatException();
    }

    internal static byte ParseByteUtf8(ReadOnlySpan<byte> text, NumberStyles style)
    {
        ValidateIntegerStyle(style);
        var chars = new char[text.Length];
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] > 0x7F)
            {
                throw new FormatException();
            }

            chars[index] = (char)text[index];
        }

        return ParseByte(chars, style);
    }

    internal static sbyte ParseSByteUtf8(ReadOnlySpan<byte> text, NumberStyles style)
    {
        ValidateIntegerStyle(style);
        var chars = new char[text.Length];
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] > 0x7F)
            {
                throw new FormatException();
            }

            chars[index] = (char)text[index];
        }

        return ParseSByte(chars, style);
    }

    private static bool Trim(ReadOnlySpan<char> text, NumberStyles style, ref int start, ref int end)
    {
        if ((style & NumberStyles.AllowLeadingWhite) != 0)
        {
            while (start < end && IsWhiteSpace(text[start]))
            {
                start++;
            }
        }
        else if (start < end && IsWhiteSpace(text[start]))
        {
            return false;
        }

        if ((style & NumberStyles.AllowTrailingWhite) != 0)
        {
            while (end > start && IsWhiteSpace(text[end - 1]))
            {
                end--;
            }
        }
        else if (end > start && IsWhiteSpace(text[end - 1]))
        {
            return false;
        }

        return start != end;
    }

    private static int Digit(char value, int radix)
    {
        var digit = value is >= '0' and <= '9' ? value - '0' :
            value is >= 'a' and <= 'f' ? value - 'a' + 10 :
            value is >= 'A' and <= 'F' ? value - 'A' + 10 : -1;
        return digit < radix ? digit : -1;
    }

    private static bool IsDecimalDigit(char value) => value is >= '0' and <= '9';

    private static bool IsWhiteSpace(char value) => value is
        '\u0009' or '\u000A' or '\u000B' or '\u000C' or '\u000D' or
        '\u0020' or '\u0085' or '\u00A0' or '\u1680' or '\u2000' or
        '\u2001' or '\u2002' or '\u2003' or '\u2004' or '\u2005' or
        '\u2006' or '\u2007' or '\u2008' or '\u2009' or '\u200A' or
        '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000';
}

public partial struct Byte
{
    public string ToString(IFormatProvider? provider) => Number.FormatUnsigned(this, 8, null);

    public string ToString(string? format, IFormatProvider? provider) => Number.FormatUnsigned(this, 8, format);

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        PrimitiveGenericMathSmall.TryCopy(Number.FormatUnsigned(this, 8, format.ToString()), destination, out charsWritten);

    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        PrimitiveGenericMathSmall.TryCopyUtf8(Number.FormatUnsigned(this, 8, format.ToString()), destination, out bytesWritten);

    public static byte Parse(string value, NumberStyles style, IFormatProvider? provider)
    {
        PrimitiveGenericMathSmall.ValidateIntegerStyle(style);
        if (value is null)
        {
            throw new ArgumentNullException();
        }

        return PrimitiveGenericMathSmall.ParseByte(new ReadOnlySpan<char>(value.ToCharArray()), style);
    }

    public static byte Parse(string value, IFormatProvider? provider) => Parse(value, NumberStyles.Integer, provider);

    public static bool TryParse(string? value, NumberStyles style, IFormatProvider? provider, out byte result)
    {
        if (value is null)
        {
            result = 0;
            PrimitiveGenericMathSmall.ValidateIntegerStyle(style);
            return false;
        }

        var valid = PrimitiveGenericMathSmall.TryParseUnsigned(new ReadOnlySpan<char>(value.ToCharArray()), style, MaxValue, out var parsed);
        result = (byte)parsed;
        return valid;
    }

    public static bool TryParse(string? value, IFormatProvider? provider, out byte result) => TryParse(value, NumberStyles.Integer, provider, out result);

    public static byte Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => PrimitiveGenericMathSmall.ParseByte(value, NumberStyles.Integer);

    public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out byte result)
    {
        var valid = PrimitiveGenericMathSmall.TryParseUnsigned(value, NumberStyles.Integer, byte.MaxValue, out var parsed);
        result = (byte)parsed;
        return valid;
    }

    public static byte Parse(ReadOnlySpan<byte> value, IFormatProvider? provider) => PrimitiveGenericMathSmall.ParseByteUtf8(value, NumberStyles.Integer);

    public static bool TryParse(ReadOnlySpan<byte> value, IFormatProvider? provider, out byte result)
    {
        var valid = PrimitiveGenericMathSmall.TryParseUtf8(value, NumberStyles.Integer, out var parsed, byte.MaxValue);
        result = (byte)parsed;
        return valid;
    }

    static byte IAdditionOperators<byte, byte, byte>.operator +(byte left, byte right) => unchecked((byte)(left + right));
    static byte IAdditionOperators<byte, byte, byte>.operator checked +(byte left, byte right) => checked((byte)(left + right));
    static byte IBitwiseOperators<byte, byte, byte>.operator &(byte left, byte right) => (byte)(left & right);
    static byte IBitwiseOperators<byte, byte, byte>.operator |(byte left, byte right) => (byte)(left | right);
    static byte IBitwiseOperators<byte, byte, byte>.operator ^(byte left, byte right) => (byte)(left ^ right);
    static byte IBitwiseOperators<byte, byte, byte>.operator ~(byte value) => (byte)~value;
    static bool IComparisonOperators<byte, byte, bool>.operator <(byte left, byte right) => left < right;
    static bool IComparisonOperators<byte, byte, bool>.operator <=(byte left, byte right) => left <= right;
    static bool IComparisonOperators<byte, byte, bool>.operator >(byte left, byte right) => left > right;
    static bool IComparisonOperators<byte, byte, bool>.operator >=(byte left, byte right) => left >= right;
    static byte IDecrementOperators<byte>.operator --(byte value) => unchecked((byte)(value - 1));
    static byte IDecrementOperators<byte>.operator checked --(byte value) => checked((byte)(value - 1));
    static byte IDivisionOperators<byte, byte, byte>.operator /(byte left, byte right) => (byte)(left / right);
    static bool IEqualityOperators<byte, byte, bool>.operator ==(byte left, byte right) => left == right;
    static bool IEqualityOperators<byte, byte, bool>.operator !=(byte left, byte right) => left != right;
    static byte IIncrementOperators<byte>.operator ++(byte value) => unchecked((byte)(value + 1));
    static byte IIncrementOperators<byte>.operator checked ++(byte value) => checked((byte)(value + 1));
    static byte IModulusOperators<byte, byte, byte>.operator %(byte left, byte right) => (byte)(left % right);
    static byte IMultiplyOperators<byte, byte, byte>.operator *(byte left, byte right) => unchecked((byte)(left * right));
    static byte IMultiplyOperators<byte, byte, byte>.operator checked *(byte left, byte right) => checked((byte)(left * right));
    static byte IShiftOperators<byte, int, byte>.operator <<(byte value, int amount) => (byte)(value << (amount & 7));
    static byte IShiftOperators<byte, int, byte>.operator >>(byte value, int amount) => (byte)(value >> (amount & 7));
    static byte IShiftOperators<byte, int, byte>.operator >>>(byte value, int amount) => (byte)(value >>> (amount & 7));
    static byte ISubtractionOperators<byte, byte, byte>.operator -(byte left, byte right) => unchecked((byte)(left - right));
    static byte ISubtractionOperators<byte, byte, byte>.operator checked -(byte left, byte right) => checked((byte)(left - right));
    static byte IUnaryNegationOperators<byte, byte>.operator -(byte value) => unchecked((byte)-value);
    static byte IUnaryNegationOperators<byte, byte>.operator checked -(byte value) => checked((byte)-value);
    static byte IUnaryPlusOperators<byte, byte>.operator +(byte value) => value;
}

public partial struct SByte
{
    public string ToString(IFormatProvider? provider) => Number.FormatSigned(this, 8, null);

    public string ToString(string? format, IFormatProvider? provider) => Number.FormatSigned(this, 8, format);

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        PrimitiveGenericMathSmall.TryCopy(Number.FormatSigned(this, 8, format.ToString()), destination, out charsWritten);

    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
        PrimitiveGenericMathSmall.TryCopyUtf8(Number.FormatSigned(this, 8, format.ToString()), destination, out bytesWritten);

    public static sbyte Parse(string value, NumberStyles style, IFormatProvider? provider)
    {
        PrimitiveGenericMathSmall.ValidateIntegerStyle(style);
        if (value is null)
        {
            throw new ArgumentNullException();
        }

        return PrimitiveGenericMathSmall.ParseSByte(new ReadOnlySpan<char>(value.ToCharArray()), style);
    }

    public static sbyte Parse(string value, IFormatProvider? provider) => Parse(value, NumberStyles.Integer, provider);

    public static bool TryParse(string? value, NumberStyles style, IFormatProvider? provider, out sbyte result)
    {
        if (value is null)
        {
            result = 0;
            PrimitiveGenericMathSmall.ValidateIntegerStyle(style);
            return false;
        }

        var valid = PrimitiveGenericMathSmall.TryParseSigned(new ReadOnlySpan<char>(value.ToCharArray()), style, 8, out var parsed);
        result = (sbyte)parsed;
        return valid;
    }

    public static bool TryParse(string? value, IFormatProvider? provider, out sbyte result) => TryParse(value, NumberStyles.Integer, provider, out result);

    public static sbyte Parse(ReadOnlySpan<char> value, IFormatProvider? provider) => PrimitiveGenericMathSmall.ParseSByte(value, NumberStyles.Integer);

    public static bool TryParse(ReadOnlySpan<char> value, IFormatProvider? provider, out sbyte result)
    {
        var valid = PrimitiveGenericMathSmall.TryParseSigned(value, NumberStyles.Integer, 8, out var parsed);
        result = (sbyte)parsed;
        return valid;
    }

    public static sbyte Parse(ReadOnlySpan<byte> value, IFormatProvider? provider) => PrimitiveGenericMathSmall.ParseSByteUtf8(value, NumberStyles.Integer);

    public static bool TryParse(ReadOnlySpan<byte> value, IFormatProvider? provider, out sbyte result)
    {
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
        valid = valid && PrimitiveGenericMathSmall.TryParseSigned(chars, NumberStyles.Integer, 8, out parsed);
        result = (sbyte)parsed;
        return valid;
    }

    static sbyte IAdditionOperators<sbyte, sbyte, sbyte>.operator +(sbyte left, sbyte right) => unchecked((sbyte)(left + right));
    static sbyte IAdditionOperators<sbyte, sbyte, sbyte>.operator checked +(sbyte left, sbyte right) => checked((sbyte)(left + right));
    static sbyte IBitwiseOperators<sbyte, sbyte, sbyte>.operator &(sbyte left, sbyte right) => (sbyte)(left & right);
    static sbyte IBitwiseOperators<sbyte, sbyte, sbyte>.operator |(sbyte left, sbyte right) => (sbyte)(left | right);
    static sbyte IBitwiseOperators<sbyte, sbyte, sbyte>.operator ^(sbyte left, sbyte right) => (sbyte)(left ^ right);
    static sbyte IBitwiseOperators<sbyte, sbyte, sbyte>.operator ~(sbyte value) => (sbyte)~value;
    static bool IComparisonOperators<sbyte, sbyte, bool>.operator <(sbyte left, sbyte right) => left < right;
    static bool IComparisonOperators<sbyte, sbyte, bool>.operator <=(sbyte left, sbyte right) => left <= right;
    static bool IComparisonOperators<sbyte, sbyte, bool>.operator >(sbyte left, sbyte right) => left > right;
    static bool IComparisonOperators<sbyte, sbyte, bool>.operator >=(sbyte left, sbyte right) => left >= right;
    static sbyte IDecrementOperators<sbyte>.operator --(sbyte value) => unchecked((sbyte)(value - 1));
    static sbyte IDecrementOperators<sbyte>.operator checked --(sbyte value) => checked((sbyte)(value - 1));
    static sbyte IDivisionOperators<sbyte, sbyte, sbyte>.operator /(sbyte left, sbyte right) => (sbyte)(left / right);
    static bool IEqualityOperators<sbyte, sbyte, bool>.operator ==(sbyte left, sbyte right) => left == right;
    static bool IEqualityOperators<sbyte, sbyte, bool>.operator !=(sbyte left, sbyte right) => left != right;
    static sbyte IIncrementOperators<sbyte>.operator ++(sbyte value) => unchecked((sbyte)(value + 1));
    static sbyte IIncrementOperators<sbyte>.operator checked ++(sbyte value) => checked((sbyte)(value + 1));
    static sbyte IModulusOperators<sbyte, sbyte, sbyte>.operator %(sbyte left, sbyte right) => (sbyte)(left % right);
    static sbyte IMultiplyOperators<sbyte, sbyte, sbyte>.operator *(sbyte left, sbyte right) => unchecked((sbyte)(left * right));
    static sbyte IMultiplyOperators<sbyte, sbyte, sbyte>.operator checked *(sbyte left, sbyte right) => checked((sbyte)(left * right));
    static sbyte IShiftOperators<sbyte, int, sbyte>.operator <<(sbyte value, int amount) => (sbyte)(value << (amount & 7));
    static sbyte IShiftOperators<sbyte, int, sbyte>.operator >>(sbyte value, int amount) => (sbyte)(value >> (amount & 7));
    static sbyte IShiftOperators<sbyte, int, sbyte>.operator >>>(sbyte value, int amount) => (sbyte)((byte)value >>> (amount & 7));
    static sbyte ISubtractionOperators<sbyte, sbyte, sbyte>.operator -(sbyte left, sbyte right) => unchecked((sbyte)(left - right));
    static sbyte ISubtractionOperators<sbyte, sbyte, sbyte>.operator checked -(sbyte left, sbyte right) => checked((sbyte)(left - right));
    static sbyte IUnaryNegationOperators<sbyte, sbyte>.operator -(sbyte value) => unchecked((sbyte)-value);
    static sbyte IUnaryNegationOperators<sbyte, sbyte>.operator checked -(sbyte value) => checked((sbyte)-value);
    static sbyte IUnaryPlusOperators<sbyte, sbyte>.operator +(sbyte value) => value;
}

public partial struct Char
{
    public string ToString(IFormatProvider? provider) => ToString();

    public string ToString(string? format, IFormatProvider? provider) => ToString();

    public static char Parse(string value, IFormatProvider? provider) => Parse(value);

    public static bool TryParse(string? value, IFormatProvider? provider, out char result) => TryParse(value, out result);

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null)
    {
        if (destination.IsEmpty)
        {
            charsWritten = 0;
            return false;
        }

        destination[0] = this;
        charsWritten = 1;
        return true;
    }

    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format = default, IFormatProvider? provider = null) =>
        new Text.Rune(this).TryEncodeToUtf8(destination, out bytesWritten);

    internal static char ParseUtf8(ReadOnlySpan<byte> value)
    {
        if (Text.Rune.DecodeFromUtf8(value, out var rune, out var consumed) != Buffers.OperationStatus.Done || consumed != value.Length)
        {
            throw new FormatException();
        }

        if (!rune.IsBmp)
        {
            throw new OverflowException();
        }

        return (char)rune.Value;
    }

    public bool Equals(char other, StringComparison comparisonType) => comparisonType switch
    {
        StringComparison.Ordinal => this == other,
        StringComparison.OrdinalIgnoreCase => ToUpperOrdinal(this) == ToUpperOrdinal(other),
        _ => throw new ArgumentException(nameof(comparisonType)),
    };

    public TypeCode GetTypeCode() => TypeCode.Char;

    public static string ConvertFromUtf32(int utf32)
    {
        if (!Text.Rune.IsValid(utf32))
        {
            throw new ArgumentOutOfRangeException(nameof(utf32));
        }

        return new Text.Rune(utf32).ToString();
    }

    public static int ConvertToUtf32(char highSurrogate, char lowSurrogate)
    {
        if (!IsHighSurrogate(highSurrogate) || !IsLowSurrogate(lowSurrogate))
        {
            throw new ArgumentException();
        }

        return ((highSurrogate - 0xD800) << 10) + (lowSurrogate - 0xDC00) + 0x10000;
    }

    public static int ConvertToUtf32(string s, int index)
    {
        ArgumentNullException.ThrowIfNull(s);
        if ((uint)index >= (uint)s.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var value = s[index];
        return IsHighSurrogate(value) && index + 1 < s.Length && IsLowSurrogate(s[index + 1])
            ? ConvertToUtf32(value, s[index + 1])
            : value;
    }

    public static double GetNumericValue(char c) => IsAsciiDigit(c) ? c - '0' : -1;

    public static double GetNumericValue(string s, int index) => GetNumericValue(GetIndexedChar(s, index));

    public static bool IsAscii(char c) => c <= '\u007F';
    public static bool IsAsciiDigit(char c) => IsBetween(c, '0', '9');
    public static bool IsAsciiHexDigit(char c) => IsAsciiHexDigitLower(c) || IsAsciiHexDigitUpper(c) || IsAsciiDigit(c);
    public static bool IsAsciiHexDigitLower(char c) => IsBetween(c, 'a', 'f');
    public static bool IsAsciiHexDigitUpper(char c) => IsBetween(c, 'A', 'F');
    public static bool IsAsciiLetter(char c) => IsAsciiLetterLower(c) || IsAsciiLetterUpper(c);
    public static bool IsAsciiLetterLower(char c) => IsBetween(c, 'a', 'z');
    public static bool IsAsciiLetterOrDigit(char c) => IsAsciiLetter(c) || IsAsciiDigit(c);
    public static bool IsAsciiLetterUpper(char c) => IsBetween(c, 'A', 'Z');
    public static bool IsBetween(char value, char minInclusive, char maxInclusive) =>
        value >= minInclusive && value <= maxInclusive;

    public static bool IsControl(char c) => c <= '\u001F' || IsBetween(c, '\u007F', '\u009F');
    public static bool IsControl(string s, int index) => IsControl(GetIndexedChar(s, index));
    public static bool IsDigit(char c) => GetUnicodeCategory(c) is Globalization.UnicodeCategory.DecimalDigitNumber;
    public static bool IsDigit(string s, int index) => IsDigit(GetIndexedChar(s, index));
    public static bool IsHighSurrogate(char c) => IsBetween(c, '\uD800', '\uDBFF');
    public static bool IsHighSurrogate(string s, int index) => IsHighSurrogate(GetIndexedChar(s, index));
    public static bool IsLetter(char c) => GetUnicodeCategory(c) is
        Globalization.UnicodeCategory.UppercaseLetter or
        Globalization.UnicodeCategory.LowercaseLetter or
        Globalization.UnicodeCategory.TitlecaseLetter or
        Globalization.UnicodeCategory.ModifierLetter or
        Globalization.UnicodeCategory.OtherLetter;
    public static bool IsLetter(string s, int index) => IsLetter(GetIndexedChar(s, index));
    public static bool IsLetterOrDigit(char c) => IsLetter(c) || IsDigit(c);
    public static bool IsLetterOrDigit(string s, int index) => IsLetterOrDigit(GetIndexedChar(s, index));
    public static bool IsLowSurrogate(char c) => IsBetween(c, '\uDC00', '\uDFFF');
    public static bool IsLowSurrogate(string s, int index) => IsLowSurrogate(GetIndexedChar(s, index));
    public static bool IsLower(char c) => GetUnicodeCategory(c) is Globalization.UnicodeCategory.LowercaseLetter;
    public static bool IsLower(string s, int index) => IsLower(GetIndexedChar(s, index));
    public static bool IsNumber(char c) => GetUnicodeCategory(c) is
        Globalization.UnicodeCategory.DecimalDigitNumber or
        Globalization.UnicodeCategory.LetterNumber or
        Globalization.UnicodeCategory.OtherNumber;
    public static bool IsNumber(string s, int index) => IsNumber(GetIndexedChar(s, index));
    public static bool IsPunctuation(char c) => GetUnicodeCategory(c) is
        Globalization.UnicodeCategory.ConnectorPunctuation or
        Globalization.UnicodeCategory.DashPunctuation or
        Globalization.UnicodeCategory.OpenPunctuation or
        Globalization.UnicodeCategory.ClosePunctuation or
        Globalization.UnicodeCategory.InitialQuotePunctuation or
        Globalization.UnicodeCategory.FinalQuotePunctuation or
        Globalization.UnicodeCategory.OtherPunctuation;
    public static bool IsPunctuation(string s, int index) => IsPunctuation(GetIndexedChar(s, index));
    public static bool IsSeparator(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r' or
        '\u0085' or '\u00A0' or '\u1680' or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' ||
        IsBetween(c, '\u2000', '\u200A');
    public static bool IsSeparator(string s, int index) => IsSeparator(GetIndexedChar(s, index));
    public static bool IsSurrogate(char c) => IsHighSurrogate(c) || IsLowSurrogate(c);
    public static bool IsSurrogate(string s, int index) => IsSurrogate(GetIndexedChar(s, index));
    public static bool IsSurrogatePair(char highSurrogate, char lowSurrogate) =>
        IsHighSurrogate(highSurrogate) && IsLowSurrogate(lowSurrogate);
    public static bool IsSurrogatePair(string s, int index)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (index < 0 || index >= s.Length - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return IsSurrogatePair(s[index], s[index + 1]);
    }
    public static bool IsSymbol(char c) => GetUnicodeCategory(c) is
        Globalization.UnicodeCategory.MathSymbol or
        Globalization.UnicodeCategory.CurrencySymbol or
        Globalization.UnicodeCategory.ModifierSymbol or
        Globalization.UnicodeCategory.OtherSymbol;
    public static bool IsSymbol(string s, int index) => IsSymbol(GetIndexedChar(s, index));
    public static bool IsUpper(char c) => GetUnicodeCategory(c) is Globalization.UnicodeCategory.UppercaseLetter;
    public static bool IsUpper(string s, int index) => IsUpper(GetIndexedChar(s, index));
    public static bool IsWhiteSpace(char c) => IsSeparator(c);
    public static bool IsWhiteSpace(string s, int index) => IsWhiteSpace(GetIndexedChar(s, index));

    public static char ToLower(char c) => ToLowerInvariant(c);
    public static char ToLowerInvariant(char c) => Globalization.CharUnicodeInfo.ToLower(c);
    public static char ToLowerOrdinal(char c) => ToLowerInvariant(c);
    public static string ToString(char c) => new(c, 1);
    public static char ToUpper(char c) => ToUpperInvariant(c);
    public static char ToUpperInvariant(char c) => Globalization.CharUnicodeInfo.ToUpper(c);
    public static char ToUpperOrdinal(char c) => ToUpperInvariant(c);

    public static Globalization.UnicodeCategory GetUnicodeCategory(char c) =>
        Globalization.CharUnicodeInfo.GetUnicodeCategory(c);

    char IConvertible.ToChar(IFormatProvider? provider) => this;
    bool IConvertible.ToBoolean(IFormatProvider? provider) => throw new InvalidCastException();
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
        conversionType == typeof(char) ? this :
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

    private static char GetIndexedChar(string s, int index)
    {
        ArgumentNullException.ThrowIfNull(s);
        if ((uint)index >= (uint)s.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return s[index];
    }
}
