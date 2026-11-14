// Adapted from dotnet/runtime Number parsing and formatting contracts.
// The upstream implementation is licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.

using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace System
{
    internal static partial class Number
    {

        internal static void ThrowDecimalOverflowException() => throw new OverflowException(SR.Overflow_Decimal);

        // ToString() with no format: the shortest round-trip form, without the
        // format string machinery.
        internal static string FormatFloat<TNumber>(TNumber value)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
            => FormatShortestFloating<TNumber>(TNumber.FloatToBits(value));

        // A format takes upstream's layout with the provider's NumberFormatInfo; without one, the
        // shortest round-trip form is written with the provider's symbols, or the current culture's,
        // and the format-string machinery stays unreachable.
        internal static string FormatFloat<TNumber>(TNumber value, string? format, object? provider)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
            => string.IsNullOrEmpty(format)
                ? FormatShortestFloating<TNumber>(TNumber.FloatToBits(value),
                    provider is null ? null : NumberFormatInfo.GetInstance((IFormatProvider?)provider))
                : FormatFloat(value, format, NumberFormatInfo.GetInstance((IFormatProvider?)provider));

        // TryFormat with no format, as FormatFloat(value, null, provider) writes it.
        internal static bool TryFormatShortestFloat<TNumber>(TNumber value, Span<char> destination, out int charsWritten, NumberFormatInfo? info = null)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            var builder = new ValueStringBuilder(destination);
            AppendShortestFloating<TNumber>(ref builder, TNumber.FloatToBits(value), info);
            return builder.TryCopyTo(destination, out charsWritten);
        }

        internal static bool TryFormatFloat<TNumber>(TNumber value, ReadOnlySpan<char> format, object? provider, Span<char> destination, out int charsWritten)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
            => format.IsEmpty
                ? TryFormatShortestFloat(value, destination, out charsWritten,
                    provider is null ? null : NumberFormatInfo.GetInstance((IFormatProvider?)provider))
                : TryFormatFloat(value, format, NumberFormatInfo.GetInstance((IFormatProvider?)provider), destination, out charsWritten);

        internal static bool TryFormatFloat<TNumber>(TNumber value, ReadOnlySpan<char> format, object? provider, Span<byte> destination, out int bytesWritten)
            where TNumber : unmanaged, IBinaryFloatParseAndFormatInfo<TNumber>
        {
            if (!format.IsEmpty)
            {
                return TryFormatFloat(value, format, NumberFormatInfo.GetInstance((IFormatProvider?)provider), destination, out bytesWritten);
            }
            Span<char> buffer = stackalloc char[64];
            var builder = new ValueStringBuilder(buffer);
            AppendShortestFloating<TNumber>(ref builder, TNumber.FloatToBits(value),
                provider is null ? null : NumberFormatInfo.GetInstance((IFormatProvider?)provider));
            var success = TryCopyUtf8(builder.AsSpan(), destination, out bytesWritten);
            builder.Dispose();
            return success;
        }

        private static bool MatchWord<TChar>(ReadOnlySpan<TChar> source, ref int index, string word)
            where TChar : unmanaged
        {
            if (index + word.Length > source.Length) return false;
            for (var wordIndex = 0; wordIndex < word.Length; wordIndex++)
            {
                var character = ReadSourceChar(source, index + wordIndex);
                var expected = word[wordIndex];
                if (character is >= 'a' and <= 'z') character = (char)(character - ('a' - 'A'));
                if (expected is >= 'a' and <= 'z') expected = (char)(expected - ('a' - 'A'));
                if (character != expected) return false;
            }
            index += word.Length;
            return true;
        }

        private static char ReadSourceChar<TChar>(ReadOnlySpan<TChar> source, int index)
            where TChar : unmanaged =>
            typeof(TChar) == typeof(char)
                ? MemoryMarshal.Cast<TChar, char>(source)[index]
                : (char)MemoryMarshal.Cast<TChar, byte>(source)[index];

        private static bool TryCopyUtf8(string value, Span<byte> destination, out int written)
            => TryCopyUtf8(value.AsSpan(), destination, out written);

        internal static bool TryCopyUtf8(ReadOnlySpan<char> value, Span<byte> destination, out int written)
        {
            var required = Encoding.UTF8.GetByteCount(value);
            if (destination.Length < required)
            {
                written = 0;
                return false;
            }
            written = Encoding.UTF8.GetBytes(value, destination);
            return true;
        }
    }
}
