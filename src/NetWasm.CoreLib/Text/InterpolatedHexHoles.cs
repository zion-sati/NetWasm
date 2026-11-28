// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace System.Text
{
    // What an interpolated hole such as {b:X2} becomes where the value is a primitive integer and its format a constant
    // hexadecimal one: AppendFormatted<T>(value, format) and AppendFormatted<T>(value, alignment, format) on a
    // DefaultInterpolatedStringHandler or a StringBuilder's handler built without a custom formatter, as the compiler
    // proves for the local the handler lives in. With no custom formatter the handler writes what the integer's
    // TryFormat writes for the format, which reads no culture for a hexadecimal one, then pads it to the alignment: the
    // same text these write through the handler's span overloads, without Number's parser of item formats.
    internal static class InterpolatedHexHoles
    {
        internal static void AppendDefault<T>(ref DefaultInterpolatedStringHandler handler, T value, string format)
        {
            var bits = HexadecimalDigits.Bits(value);
            Span<char> digits = stackalloc char[HexadecimalDigits.Count(bits, format)];
            HexadecimalDigits.Write(bits, format, digits);
            handler.AppendFormatted(digits);
        }

        internal static void AppendDefaultAligned<T>(ref DefaultInterpolatedStringHandler handler, T value, int alignment, string format)
        {
            var bits = HexadecimalDigits.Bits(value);
            Span<char> digits = stackalloc char[HexadecimalDigits.Count(bits, format)];
            HexadecimalDigits.Write(bits, format, digits);
            handler.AppendFormatted(digits, alignment);
        }

        internal static void AppendBuilder<T>(ref StringBuilder.AppendInterpolatedStringHandler handler, T value, string format)
        {
            var bits = HexadecimalDigits.Bits(value);
            Span<char> digits = stackalloc char[HexadecimalDigits.Count(bits, format)];
            HexadecimalDigits.Write(bits, format, digits);
            handler.AppendFormatted(digits);
        }

        internal static void AppendBuilderAligned<T>(ref StringBuilder.AppendInterpolatedStringHandler handler, T value, int alignment, string format)
        {
            var bits = HexadecimalDigits.Bits(value);
            Span<char> digits = stackalloc char[HexadecimalDigits.Count(bits, format)];
            HexadecimalDigits.Write(bits, format, digits);
            handler.AppendFormatted(digits, alignment);
        }
    }
}
