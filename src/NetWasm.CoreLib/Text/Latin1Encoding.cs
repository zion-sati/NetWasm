// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce (Latin1Encoding.cs and
// Latin1Encoding.Sealed.cs). NetWasm adaptation: written on NetWasm's Encoding, as ASCIIEncoding is, without
// upstream's vectorized Latin1Utility; the behavior is upstream's: code page 28591, every byte is the char of the
// same value, and a char above U+00FF goes to the encoder fallback (best fit by default).
namespace System.Text;

internal class Latin1Encoding : Encoding
{
    internal const int Latin1CodePage = 28591;

    // Encoding.Latin1: read-only, with the default fallbacks.
    internal static readonly Latin1Encoding s_default = ReadOnly(new Latin1Encoding());

    public Latin1Encoding()
        : base(Latin1CodePage, EncoderLatin1BestFitFallback.SingletonInstance, DecoderFallback.ReplacementFallback)
    {
    }

    public override bool IsSingleByte => true;
    public override string EncodingName => "Western European (ISO)";
    public override string WebName => "iso-8859-1";

    public override int GetByteCount(char[] chars, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(chars);
        ValidateRange(index, count, chars.Length);
        return Encode(chars.AsSpan(index, count), Span<byte>.Empty, countOnly: true);
    }

    public override int GetByteCount(ReadOnlySpan<char> chars) => Encode(chars, Span<byte>.Empty, countOnly: true);

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        ArgumentNullException.ThrowIfNull(chars);
        ArgumentNullException.ThrowIfNull(bytes);
        ValidateRange(charIndex, charCount, chars.Length);
        ValidateDestination(byteIndex, bytes.Length);
        var source = chars.AsSpan(charIndex, charCount);
        EnsureDestination(byteIndex, Encode(source, Span<byte>.Empty, countOnly: true), bytes.Length);
        return Encode(source, bytes.AsSpan(byteIndex), countOnly: false);
    }

    public override int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        if (Encode(chars, Span<byte>.Empty, countOnly: true) > bytes.Length)
        {
            throw new ArgumentException("The destination buffer is too small.", nameof(bytes));
        }

        return Encode(chars, bytes, countOnly: false);
    }

    public override int GetCharCount(byte[] bytes, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ValidateRange(index, count, bytes.Length);
        return count;
    }

    public override int GetCharCount(ReadOnlySpan<byte> bytes) => bytes.Length;

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(chars);
        ValidateRange(byteIndex, byteCount, bytes.Length);
        ValidateDestination(charIndex, chars.Length);
        EnsureDestination(charIndex, byteCount, chars.Length);
        for (var offset = 0; offset < byteCount; offset++)
        {
            chars[charIndex + offset] = (char)bytes[byteIndex + offset];
        }

        return byteCount;
    }

    public override int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars)
    {
        if (bytes.Length > chars.Length)
        {
            throw new ArgumentException("The destination buffer is too small.", nameof(chars));
        }

        for (var index = 0; index < bytes.Length; index++)
        {
            chars[index] = (char)bytes[index];
        }

        return bytes.Length;
    }

    public override int GetMaxByteCount(int charCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(charCount);
        var count = ((long)charCount + 1) * (EncoderFallback.MaxCharCount > 1 ? EncoderFallback.MaxCharCount : 1);
        if (count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(charCount));
        return (int)count;
    }

    public override int GetMaxCharCount(int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        return byteCount;
    }

    public override bool Equals(object? value) =>
        value is Latin1Encoding other && EncoderFallback.Equals(other.EncoderFallback) && DecoderFallback.Equals(other.DecoderFallback);

    public override int GetHashCode() => CodePage + EncoderFallback.GetHashCode() + DecoderFallback.GetHashCode();

    // Chars up to U+00FF are their own bytes; any other char, or surrogate pair, is replaced by what the encoder
    // fallback supplies, which must itself be Latin-1.
    private int Encode(ReadOnlySpan<char> chars, Span<byte> bytes, bool countOnly)
    {
        var written = 0;
        EncoderFallbackBuffer? fallback = null;
        for (var index = 0; index < chars.Length; index++)
        {
            var value = chars[index];
            if (value <= 0xFF)
            {
                if (!countOnly) bytes[written] = (byte)value;
                written++;
                continue;
            }

            fallback ??= EncoderFallback.CreateFallbackBuffer();
            bool replaced;
            if (char.IsHighSurrogate(value) && index + 1 < chars.Length && char.IsLowSurrogate(chars[index + 1]))
            {
                replaced = fallback.Fallback(value, chars[index + 1], index);
                index++;
            }
            else
            {
                replaced = fallback.Fallback(value, index);
            }

            if (!replaced)
            {
                continue;
            }

            while (fallback.Remaining > 0)
            {
                var replacement = fallback.GetNextChar();
                if (replacement > 0xFF)
                {
                    throw new ArgumentException("Recursive fallback not allowed for character \\u" + ((int)replacement).ToString("X4") + ".");
                }

                if (!countOnly) bytes[written] = (byte)replacement;
                written++;
            }
        }

        return written;
    }
}
