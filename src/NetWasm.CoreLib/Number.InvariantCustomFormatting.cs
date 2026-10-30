// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Invariant char specialization of dotnet/runtime Number.Formatting.Common.cs.
// Pinned upstream runtime commit: 474ed963699fc29809015392738a15231aaae2de.

using System.Collections.Generic;
using System.Text;

namespace System;

internal static partial class Number
{
    internal static char ParseInvariantFormatSpecifier(string? format, out int precision)
        => ParseInvariantFormatSpecifier(format.AsSpan(), out precision);

    internal static char ParseInvariantFormatSpecifier(ReadOnlySpan<char> format, out int precision)
    {
        precision = -1;
        if (format.Length == 0)
        {
            return 'G';
        }

        var symbol = format[0];
        if (!char.IsAsciiLetter(symbol))
        {
            return '\0';
        }

        if (format.Length == 1)
        {
            return symbol;
        }

        var value = 0;
        var index = 1;
        while (index < format.Length && char.IsAsciiDigit(format[index]))
        {
            if (value >= 100_000_000)
            {
                throw new FormatException();
            }
            value = value * 10 + format[index++] - '0';
        }

        if (index == format.Length || format[index] == '\0')
        {
            precision = value;
            return symbol;
        }
        return '\0';
    }

    internal static string CreateInvariantStandardPattern(char format, int precision)
    {
        precision = precision < 0 ? format is 'E' or 'e' ? 6 : 2 : precision;
        var grouping = format is 'C' or 'c' or 'N' or 'n' or 'P' or 'p';
        var currency = format is 'C' or 'c';
        var percent = format is 'P' or 'p';
        var scientific = format is 'E' or 'e';
        var integerPattern = grouping ? "#,##0" : "0";
        var fractionLength = precision == 0 ? 0 : precision + 1;
        var prefixLength = currency ? 1 : 0;
        var suffixLength = percent ? 1 : scientific ? 5 : 0;
        var length = checked(prefixLength + integerPattern.Length + fractionLength + suffixLength);
        var pattern = new char[length];
        var destination = 0;
        if (currency)
        {
            pattern[destination++] = '$';
        }
        for (var index = 0; index < integerPattern.Length; index++)
        {
            pattern[destination++] = integerPattern[index];
        }
        if (precision != 0)
        {
            pattern[destination++] = '.';
            for (var index = 0; index < precision; index++)
            {
                pattern[destination++] = '0';
            }
        }
        if (percent)
        {
            pattern[destination++] = '%';
        }
        else if (scientific)
        {
            pattern[destination++] = format;
            pattern[destination++] = '+';
            pattern[destination++] = '0';
            pattern[destination++] = '0';
            pattern[destination++] = '0';
        }
        return string.Create(pattern);
    }

    internal static string UInt64ToInvariantDigits(ulong value)
    {
        Span<char> buffer = stackalloc char[20];
        var index = buffer.Length;
        do
        {
            buffer[--index] = (char)('0' + value % 10);
            value /= 10;
        }
        while (value != 0);
        return buffer[index..].ToString();
    }

    internal static string FormatCustomInvariant(
        scoped ReadOnlySpan<char> digits,
        int scale,
        bool isNegative,
        NumberBufferKind kind,
        scoped ReadOnlySpan<char> format)
    {
        var builder = new ValueStringBuilder(32);
        AppendCustomInvariant(ref builder, digits, scale, isNegative, kind, format);
        return builder.ToString();
    }

    internal static void AppendCustomInvariant(
        scoped ref ValueStringBuilder builder,
        scoped ReadOnlySpan<char> digits,
        int scale,
        bool isNegative,
        NumberBufferKind kind,
        scoped ReadOnlySpan<char> format)
    {
        var isZero = digits.Length == 0 || digits.Length == 1 && digits[0] == '0';
        var digitCount = isZero ? 0 : digits.Length;
        var digitStorage = new byte[digitCount + 1];
        var number = new NumberBuffer(kind, digitStorage)
        {
            DigitsCount = digitCount,
            Scale = isZero ? 0 : scale,
            IsNegative = isNegative,
        };

        for (var index = 0; index < digitCount; index++)
        {
            digitStorage[index] = (byte)digits[index];
        }

        NumberToStringFormatInvariant(ref builder, ref number, format);
    }

    private static void NumberToStringFormatInvariant(
        scoped ref ValueStringBuilder builder,
        ref NumberBuffer number,
        scoped ReadOnlySpan<char> format)
    {
        number.CheckConsistency();

        var section = FindInvariantFormatSection(
            format,
            number.Digits[0] == 0 ? 2 : number.IsNegative ? 1 : 0);
        var digitCount = 0;
        var decimalPos = -1;
        var firstDigit = int.MaxValue;
        var lastDigit = 0;
        var scientific = false;
        var thousandSeps = false;
        var src = section;

        while (true)
        {
            digitCount = 0;
            decimalPos = -1;
            firstDigit = int.MaxValue;
            lastDigit = 0;
            scientific = false;
            var thousandPos = -1;
            var thousandCount = 0;
            thousandSeps = false;
            var scaleAdjust = 0;
            src = section;

            while (src < format.Length && format[src] != '\0' && format[src] != ';')
            {
                var ch = format[src++];
                switch (ch)
                {
                    case '#':
                        digitCount++;
                        break;
                    case '0':
                        if (firstDigit == int.MaxValue)
                        {
                            firstDigit = digitCount;
                        }
                        digitCount++;
                        lastDigit = digitCount;
                        break;
                    case '.':
                        if (decimalPos < 0)
                        {
                            decimalPos = digitCount;
                        }
                        break;
                    case ',':
                        if (digitCount > 0 && decimalPos < 0)
                        {
                            if (thousandPos >= 0)
                            {
                                if (thousandPos == digitCount)
                                {
                                    thousandCount++;
                                    break;
                                }
                                thousandSeps = true;
                            }
                            thousandPos = digitCount;
                            thousandCount = 1;
                        }
                        break;
                    case '%':
                        scaleAdjust += 2;
                        break;
                    case '\u2030':
                        scaleAdjust += 3;
                        break;
                    case '\'':
                    case '"':
                        while (src < format.Length && format[src] != '\0' && format[src++] != ch)
                        {
                        }
                        break;
                    case '\\':
                        if (src < format.Length && format[src] != '\0')
                        {
                            src++;
                        }
                        break;
                    case 'E':
                    case 'e':
                        if (src < format.Length && format[src] == '0' ||
                            src + 1 < format.Length && format[src] is '+' or '-' && format[src + 1] == '0')
                        {
                            while (++src < format.Length && format[src] == '0')
                            {
                            }
                            scientific = true;
                        }
                        break;
                }
            }

            if (decimalPos < 0)
            {
                decimalPos = digitCount;
            }

            if (thousandPos >= 0)
            {
                if (thousandPos == decimalPos)
                {
                    scaleAdjust -= thousandCount * 3;
                }
                else
                {
                    thousandSeps = true;
                }
            }

            if (number.Digits[0] != 0)
            {
                number.Scale += scaleAdjust;
                var position = scientific ? digitCount : number.Scale + digitCount - decimalPos;
                RoundInvariantNumber(ref number, position);
                if (number.Digits[0] == 0)
                {
                    src = FindInvariantFormatSection(format, 2);
                    if (src != section)
                    {
                        section = src;
                        continue;
                    }
                }
            }
            else
            {
                if (number.Kind != NumberBufferKind.FloatingPoint)
                {
                    number.IsNegative = false;
                }
                number.Scale = 0;
            }

            break;
        }

        firstDigit = firstDigit < decimalPos ? decimalPos - firstDigit : 0;
        lastDigit = lastDigit > decimalPos ? decimalPos - lastDigit : 0;
        var digPos = scientific ? decimalPos : Math.Max(number.Scale, decimalPos);
        var adjust = scientific ? 0 : number.Scale - decimalPos;
        src = section;

        Span<int> thousandsSepPos = stackalloc int[4];
        var thousandsSepCtr = -1;
        if (thousandSeps)
        {
            const int groupSize = 3;
            var totalDigits = digPos + (adjust < 0 ? adjust : 0);
            var numDigits = Math.Max(firstDigit, totalDigits);
            var groupTotalSizeCount = groupSize;
            while (numDigits > groupTotalSizeCount)
            {
                ++thousandsSepCtr;
                if (thousandsSepCtr >= thousandsSepPos.Length)
                {
                    var grown = new int[thousandsSepPos.Length * 2];
                    thousandsSepPos.CopyTo(grown);
                    thousandsSepPos = grown;
                }
                thousandsSepPos[thousandsSepCtr] = groupTotalSizeCount;
                groupTotalSizeCount += groupSize;
            }
        }

        if (number.IsNegative && section == 0 && number.Scale != 0 &&
            !HasInvariantNegativeSection(format))
        {
            builder.Append('-');
        }

        var decimalWritten = false;
        var cur = 0;
        while (src < format.Length && format[src] != '\0' && format[src] != ';')
        {
            var ch = format[src++];
            if (adjust > 0 && ch is '#' or '0' or '.')
            {
                while (adjust > 0)
                {
                    builder.Append(number.Digits[cur] != 0 ? (char)number.Digits[cur++] : '0');
                    AppendInvariantGroupSeparator(
                        ref builder,
                        thousandSeps,
                        digPos,
                        thousandsSepPos,
                        ref thousandsSepCtr);
                    digPos--;
                    adjust--;
                }
            }

            switch (ch)
            {
                case '#':
                case '0':
                    if (adjust < 0)
                    {
                        adjust++;
                        ch = digPos <= firstDigit ? '0' : '\0';
                    }
                    else
                    {
                        ch = number.Digits[cur] != 0
                            ? (char)number.Digits[cur++]
                            : digPos > lastDigit ? '0' : '\0';
                    }

                    if (ch != 0)
                    {
                        builder.Append(ch);
                        AppendInvariantGroupSeparator(
                            ref builder,
                            thousandSeps,
                            digPos,
                            thousandsSepPos,
                            ref thousandsSepCtr);
                    }
                    digPos--;
                    break;

                case '.':
                    if (digPos == 0 && !decimalWritten &&
                        (lastDigit < 0 || decimalPos < digitCount && number.Digits[cur] != 0))
                    {
                        builder.Append('.');
                        decimalWritten = true;
                    }
                    break;

                case '\u2030':
                    builder.Append('\u2030');
                    break;
                case '%':
                    builder.Append('%');
                    break;
                case ',':
                    break;
                case '\'':
                case '"':
                    while (src < format.Length && format[src] != '\0' && format[src] != ch)
                    {
                        builder.Append(format[src++]);
                    }
                    if (src < format.Length && format[src] != '\0')
                    {
                        src++;
                    }
                    break;
                case '\\':
                    if (src < format.Length && format[src] != '\0')
                    {
                        builder.Append(format[src++]);
                    }
                    break;
                case 'E':
                case 'e':
                    if (scientific)
                    {
                        var positiveSign = false;
                        var minimumDigits = 0;
                        if (src < format.Length && format[src] == '0')
                        {
                            minimumDigits++;
                        }
                        else if (src + 1 < format.Length && format[src] == '+' && format[src + 1] == '0')
                        {
                            positiveSign = true;
                        }
                        else if (src + 1 >= format.Length || format[src] != '-' || format[src + 1] != '0')
                        {
                            builder.Append(ch);
                            break;
                        }

                        while (++src < format.Length && format[src] == '0')
                        {
                            minimumDigits++;
                        }
                        minimumDigits = Math.Min(minimumDigits, 10);
                        var exponent = number.Digits[0] == 0 ? 0 : number.Scale - decimalPos;
                        FormatInvariantExponent(ref builder, exponent, ch, minimumDigits, positiveSign);
                        scientific = false;
                    }
                    else
                    {
                        builder.Append(ch);
                        if (src < format.Length && format[src] is '+' or '-')
                        {
                            builder.Append(format[src++]);
                        }
                        while (src < format.Length && format[src] == '0')
                        {
                            builder.Append(format[src++]);
                        }
                    }
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        if (number.IsNegative && section == 0 && number.Scale == 0 && builder.Length > 0 &&
            !HasInvariantNegativeSection(format))
        {
            builder.Insert(0, '-', 1);
        }
    }

    private static void AppendInvariantGroupSeparator(
        scoped ref ValueStringBuilder builder,
        bool enabled,
        int digitPosition,
        scoped Span<int> separatorPositions,
        ref int separatorIndex)
    {
        if (enabled && digitPosition > 1 && separatorIndex >= 0 &&
            digitPosition == separatorPositions[separatorIndex] + 1)
        {
            builder.Append(',');
            separatorIndex--;
        }
    }

    private static void FormatInvariantExponent(
        scoped ref ValueStringBuilder builder,
        int value,
        char exponentCharacter,
        int minimumDigits,
        bool positiveSign)
    {
        builder.Append(exponentCharacter);
        if (value < 0)
        {
            builder.Append('-');
            value = -value;
        }
        else if (positiveSign)
        {
            builder.Append('+');
        }

        Span<char> digits = stackalloc char[10];
        var index = digits.Length;
        do
        {
            digits[--index] = (char)('0' + value % 10);
            value /= 10;
        }
        while (value != 0);
        while (digits.Length - index < minimumDigits)
        {
            digits[--index] = '0';
        }
        builder.Append(digits[index..]);
    }

    private static void RoundInvariantNumber(ref NumberBuffer number, int position)
    {
        var digits = number.Digits;
        var index = 0;
        while (index < position && digits[index] != 0)
        {
            index++;
        }

        if (index == position && digits[index] >= '5')
        {
            while (index > 0 && digits[index - 1] == '9')
            {
                index--;
            }
            if (index > 0)
            {
                digits[index - 1]++;
            }
            else
            {
                number.Scale++;
                digits[0] = (byte)'1';
                index = 1;
            }
        }
        else
        {
            while (index > 0 && digits[index - 1] == '0')
            {
                index--;
            }
        }

        if (index == 0)
        {
            if (number.Kind != NumberBufferKind.FloatingPoint)
            {
                number.IsNegative = false;
            }
            number.Scale = 0;
        }
        digits[index] = 0;
        number.DigitsCount = index;
    }

    private static bool HasInvariantNegativeSection(ReadOnlySpan<char> format) =>
        FindInvariantFormatSection(format, 1) != 0;

    private static int FindInvariantFormatSection(ReadOnlySpan<char> format, int section)
    {
        if (section == 0)
        {
            return 0;
        }

        var source = 0;
        while (source < format.Length)
        {
            var ch = format[source++];
            switch (ch)
            {
                case '\'':
                case '"':
                    while (source < format.Length && format[source] != 0 && format[source++] != ch)
                    {
                    }
                    break;
                case '\\':
                    if (source < format.Length && format[source] != 0)
                    {
                        source++;
                    }
                    break;
                case ';':
                    if (--section != 0)
                    {
                        break;
                    }
                    if (source < format.Length && format[source] != 0 && format[source] != ';')
                    {
                        return source;
                    }
                    return 0;
                case '\0':
                    return 0;
            }
        }
        return 0;
    }
}
