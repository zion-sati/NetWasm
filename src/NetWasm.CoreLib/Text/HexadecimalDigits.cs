// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Numerics;

namespace System.Text
{
    // What a primitive integer's ToString(format) writes for a hexadecimal format (X or x and a precision of decimal
    // digits, which the compiler has checked): the digits of its bits at its own width, two's complement for a negative
    // value, in the case of the format's letter, at least as many as the precision and at least one. Holes the compiler
    // types write them this way without reading the format through Number's parser.
    internal static class HexadecimalDigits
    {
        // The value's bits at its own width; a native integer's width is the target's.
        internal static ulong Bits<T>(T value) =>
            typeof(T) == typeof(byte) ? (byte)(object)value! :
            typeof(T) == typeof(sbyte) ? (byte)(sbyte)(object)value! :
            typeof(T) == typeof(short) ? (ushort)(short)(object)value! :
            typeof(T) == typeof(ushort) ? (ushort)(object)value! :
            typeof(T) == typeof(int) ? (uint)(int)(object)value! :
            typeof(T) == typeof(uint) ? (uint)(object)value! :
            typeof(T) == typeof(long) ? (ulong)(long)(object)value! :
            typeof(T) == typeof(ulong) ? (ulong)(object)value! :
            typeof(T) == typeof(nint) ? (IntPtr.Size == 8 ? (ulong)(long)(nint)(object)value! : (uint)(int)(nint)(object)value!) :
            typeof(T) == typeof(nuint) ? (ulong)(nuint)(object)value! :
            throw new FormatException();

        // How many digits the format writes for the bits.
        internal static int Count(ulong bits, string format)
        {
            var precision = 0;
            for (var index = 1; index < format.Length; index++)
            {
                precision = precision * 10 + format[index] - '0';
            }
            return Math.Max(Math.Max(precision, 1), (64 - BitOperations.LeadingZeroCount(bits) + 3) >> 2);
        }

        // The bits' digits, filling the span Count sized, most significant first.
        internal static void Write(ulong bits, string format, Span<char> digits)
        {
            for (var index = digits.Length - 1; index >= 0; index--)
            {
                var digit = (int)(bits & 0xF);
                digits[index] = format[0] == 'X' ? HexConverter.ToCharUpper(digit) : HexConverter.ToCharLower(digit);
                bits >>= 4;
            }
        }
    }
}
