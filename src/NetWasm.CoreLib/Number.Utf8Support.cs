// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Derived from dotnet/runtime System.Number.NumberBuffer.cs and Number.Parsing.cs.

using System.Diagnostics;

namespace System;

internal static partial class Number
{

    internal static T NumberToFloat<T>(ref NumberBuffer number) where T : struct
    {
        var text = NumberBufferToString(ref number);
        if (typeof(T) == typeof(float))
        {
            TryParseSingle(text, out var value);
            return (T)(object)value;
        }

        TryParseDouble(text, out var doubleValue);
        return (T)(object)doubleValue;
    }

    internal static unsafe bool TryNumberToDecimal(ref NumberBuffer number, ref decimal value)
    {
        const int DecimalPrecision = 29;
        number.CheckConsistency();
        byte* p = number.DigitsPtr;
        int e = number.Scale;
        bool sign = number.IsNegative;
        uint c = *p;
        if (c == 0)
        {
            // Preserve the scale of zero unless the scale was positive.
            value = new decimal(0, 0, 0, sign, (byte)Math.Clamp(-e, 0, 28));
            return true;
        }
        if (e > DecimalPrecision)
            return false;
        ulong low64 = 0;
        while (e > -28)
        {
            e--;
            low64 *= 10;
            low64 += c - '0';
            c = *++p;
            if (low64 >= ulong.MaxValue / 10)
                break;
            if (c == 0)
            {
                while (e > 0)
                {
                    e--;
                    low64 *= 10;
                    if (low64 >= ulong.MaxValue / 10)
                        break;
                }
                break;
            }
        }
        uint high = 0;
        while ((e > 0 || (c != 0 && e > -28)) &&
          (high < uint.MaxValue / 10 || (high == uint.MaxValue / 10 && (low64 < 0x99999999_99999999 || (low64 == 0x99999999_99999999 && c <= '5')))))
        {
            ulong tmpLow = (uint)low64 * 10UL;
            ulong tmp64 = ((uint)(low64 >> 32) * 10UL) + (tmpLow >> 32);
            low64 = (uint)tmpLow + (tmp64 << 32);
            high = (uint)(tmp64 >> 32) + (high * 10);
            if (c != 0)
            {
                c -= '0';
                low64 += c;
                if (low64 < c)
                    high++;
                c = *++p;
            }
            e--;
        }

        if (c >= '5')
        {
            if ((c == '5') && ((low64 & 1) == 0))
            {
                c = *++p;
                bool hasZeroTail = !number.HasNonZeroTail;
                while ((c != 0) && hasZeroTail)
                {
                    hasZeroTail &= c == '0';
                    c = *++p;
                }

                Debug.Assert((c == 0) || !hasZeroTail);
                if (hasZeroTail)
                {
                    goto NoRounding;
                }
            }
            if (++low64 == 0 && ++high == 0)
            {
                low64 = 0x99999999_9999999A;
                high = uint.MaxValue / 10;
                e++;
            }
        }
    NoRounding:
        if (e > 0)
            return false;
        if (e <= -DecimalPrecision)
        {
            value = new decimal(0, 0, 0, sign, DecimalPrecision - 1);
        }
        else
        {
            value = new decimal((int)low64, (int)(low64 >> 32), (int)high, sign, (byte)-e);
        }
        return true;
    }

    private static string NumberBufferToString(ref NumberBuffer number)
    {
        if (number.DigitsCount == 0)
        {
            return number.IsNegative ? "-0" : "0";
        }

        var exponent = (long)number.Scale - number.DigitsCount;
        var length = number.DigitsCount + (number.IsNegative ? 1 : 0) + 2 + CountDigits(exponent);
        var chars = new char[length];
        var index = 0;
        if (number.IsNegative)
        {
            chars[index++] = '-';
        }

        for (var digitIndex = 0; digitIndex < number.DigitsCount; digitIndex++)
        {
            chars[index++] = (char)number.Digits[digitIndex];
        }

        chars[index++] = 'e';
        if (exponent >= 0)
        {
            chars[index++] = '+';
        }
        else
        {
            chars[index++] = '-';
            exponent = -exponent;
        }

        var exponentStart = index;
        do
        {
            chars[index++] = (char)('0' + exponent % 10);
            exponent /= 10;
        }
        while (exponent != 0);

        var exponentEnd = index;
        for (var left = exponentStart; left < exponentStart + (exponentEnd - exponentStart) / 2; left++)
        {
            var right = exponentEnd - 1 - (left - exponentStart);
            (chars[left], chars[right]) = (chars[right], chars[left]);
        }

        return new string(chars);
    }

    private static int CountDigits(long value)
    {
        var magnitude = value < 0 ? -value : value;
        var count = magnitude == 0 ? 1 : 0;
        while (magnitude != 0)
        {
            count++;
            magnitude /= 10;
        }
        return count;
    }
}
