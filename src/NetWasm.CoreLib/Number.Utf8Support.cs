// Derived from dotnet/runtime System.Number.NumberBuffer.cs.
// The .NET Foundation licenses the upstream implementation under the MIT license.

using System.Diagnostics;

namespace System;

internal static partial class Number
{
    internal const int DecimalNumberBufferLength = 31;
    internal const int SingleNumberBufferLength = 114;
    internal const int DoubleNumberBufferLength = 769;

    internal ref struct NumberBuffer
    {
        public int DigitsCount;
        public int Scale;
        public bool IsNegative;
        public bool HasNonZeroTail;
        public NumberBufferKind Kind;
        public Span<byte> Digits;

        public NumberBuffer(NumberBufferKind kind, Span<byte> digits)
        {
            Debug.Assert(!digits.IsEmpty);
            DigitsCount = 0;
            Scale = 0;
            IsNegative = false;
            HasNonZeroTail = false;
            Kind = kind;
            Digits = digits;
            Digits.Clear();
        }

        public void CheckConsistency()
        {
        }
    }

    internal enum NumberBufferKind : byte
    {
        Unknown,
        Integer,
        Decimal,
        FloatingPoint,
    }

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

    internal static bool TryNumberToDecimal(ref NumberBuffer number, ref decimal value)
    {
        var text = NumberBufferToString(ref number);
        return Decimal.TryParse(text, out value);
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

        return string.Create(chars);
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
