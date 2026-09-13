// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Scalar adaptation: the NetWasm profile routes integer formatting through
// its invariant Number implementation rather than runtime-private emitters.

namespace System.Buffers.Text;

public static partial class Utf8Formatter
{
    public static bool TryFormat(byte value, Span<byte> destination, out int bytesWritten, StandardFormat format = default(System.Buffers.StandardFormat)) =>
        TryWrite(FormatUnsigned(value, 8, format), destination, out bytesWritten);

    public static bool TryFormat(sbyte value, Span<byte> destination, out int bytesWritten, StandardFormat format = default(System.Buffers.StandardFormat)) =>
        TryWrite(FormatSigned(value, 8, format), destination, out bytesWritten);

    public static bool TryFormat(ushort value, Span<byte> destination, out int bytesWritten, StandardFormat format = default(System.Buffers.StandardFormat)) =>
        TryWrite(FormatUnsigned(value, 16, format), destination, out bytesWritten);

    public static bool TryFormat(short value, Span<byte> destination, out int bytesWritten, StandardFormat format = default(System.Buffers.StandardFormat)) =>
        TryWrite(FormatSigned(value, 16, format), destination, out bytesWritten);

    public static bool TryFormat(uint value, Span<byte> destination, out int bytesWritten, StandardFormat format = default(System.Buffers.StandardFormat)) =>
        TryWrite(FormatUnsigned(value, 32, format), destination, out bytesWritten);

    public static bool TryFormat(int value, Span<byte> destination, out int bytesWritten, StandardFormat format = default(System.Buffers.StandardFormat)) =>
        TryWrite(FormatSigned(value, 32, format), destination, out bytesWritten);

    public static bool TryFormat(ulong value, Span<byte> destination, out int bytesWritten, StandardFormat format = default(System.Buffers.StandardFormat)) =>
        TryWrite(FormatUnsigned(value, 64, format), destination, out bytesWritten);

    public static bool TryFormat(long value, Span<byte> destination, out int bytesWritten, StandardFormat format = default(System.Buffers.StandardFormat)) =>
        TryWrite(FormatSigned(value, 64, format), destination, out bytesWritten);

    private static string FormatUnsigned(ulong value, int bits, StandardFormat format)
    {
        if (format.Symbol is 'n' or 'N')
        {
            return FormatNumber(Number.FormatUnsigned(value, bits, "D"), format);
        }

        return Number.FormatUnsigned(value, bits, FormatString(format));
    }

    private static string FormatSigned(long value, int bits, StandardFormat format)
    {
        if (format.Symbol is 'n' or 'N')
        {
            return FormatNumber(Number.FormatSigned(value, bits, "D"), format);
        }

        return Number.FormatSigned(value, bits, FormatString(format));
    }

    private static string? FormatString(StandardFormat format)
    {
        if (format.IsDefault)
        {
            return null;
        }

        var symbol = format.Symbol;
        if (symbol is 'g' or 'G' or 'r' or 'R')
        {
            if (format.HasPrecision)
            {
                throw new NotSupportedException();
            }

            return "D";
        }

        if (symbol is not ('d' or 'D' or 'x' or 'X'))
        {
            ThrowHelper.ThrowFormatException_BadFormatSpecifier();
        }

        return format.HasPrecision
            ? new StandardFormat(symbol, format.Precision).ToString()
            : new StandardFormat(symbol).ToString();
    }

    private static string FormatNumber(string integerText, StandardFormat format)
    {
        var decimalDigits = format.HasPrecision ? format.Precision : (byte)2;
        var negative = integerText.Length != 0 && integerText[0] == '-';
        var firstDigit = negative ? 1 : 0;
        var digitCount = integerText.Length - firstDigit;
        var separators = digitCount <= 3 ? 0 : (digitCount - 1) / 3;
        var length = (negative ? 1 : 0) + digitCount + separators + (decimalDigits == 0 ? 0 : 1 + decimalDigits);
        var chars = new char[length];
        var index = 0;
        if (negative)
        {
            chars[index++] = '-';
        }

        for (var digitIndex = 0; digitIndex < digitCount; digitIndex++)
        {
            if (digitIndex != 0 && (digitCount - digitIndex) % 3 == 0)
            {
                chars[index++] = ',';
            }

            chars[index++] = integerText[firstDigit + digitIndex];
        }

        if (decimalDigits != 0)
        {
            chars[index++] = '.';
            for (var decimalIndex = 0; decimalIndex < decimalDigits; decimalIndex++)
            {
                chars[index++] = '0';
            }
        }

        return string.Create(chars);
    }

    private static bool TryWrite(string value, Span<byte> destination, out int bytesWritten)
    {
        if (value.Length > destination.Length)
        {
            bytesWritten = 0;
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character > 0x7F)
            {
                bytesWritten = 0;
                return false;
            }

            destination[index] = (byte)character;
        }

        bytesWritten = value.Length;
        return true;
    }
}
