// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under the MIT license.
namespace System.Text;

public sealed class UTF32Encoding : Encoding
{
    internal static readonly new UTF32Encoding Default = ReadOnly(new UTF32Encoding(false, true));
    private readonly bool _bigEndian;
    private readonly bool _emitIdentifier;
    public UTF32Encoding() : this(false, true) { }
    public UTF32Encoding(bool bigEndian, bool byteOrderMark) : this(bigEndian, byteOrderMark, false) { }
    public UTF32Encoding(bool bigEndian, bool byteOrderMark, bool throwOnInvalidCharacters) : base(
        bigEndian ? 12001 : 12000,
        throwOnInvalidCharacters ? EncoderFallback.ExceptionFallback : new EncoderReplacementFallback("\uFFFD"),
        throwOnInvalidCharacters ? DecoderFallback.ExceptionFallback : new DecoderReplacementFallback("\uFFFD"))
    {
        _bigEndian = bigEndian; _emitIdentifier = byteOrderMark;
    }
    public override string EncodingName => _bigEndian ? "Unicode (UTF-32 Big-Endian)" : "Unicode (UTF-32)";
    public override string WebName => _bigEndian ? "utf-32BE" : "utf-32";
    public override ReadOnlySpan<byte> Preamble { get => _emitIdentifier ? (_bigEndian ? new byte[] { 0, 0, 0xFE, 0xFF } : new byte[] { 0xFF, 0xFE, 0, 0 }) : ReadOnlySpan<byte>.Empty; }
    public override int GetByteCount(char[] chars, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(chars); ValidateRange(index, count, chars.Length); var total = 0; for (var offset = 0; offset < count; offset++) { var value = chars[index + offset]; if (value >= 0xD800 && value <= 0xDBFF && offset + 1 < count && chars[index + offset + 1] >= 0xDC00 && chars[index + offset + 1] <= 0xDFFF) offset++; else if (value >= 0xD800 && value <= 0xDFFF) { if (UsesEncoderExceptionFallback) throw new EncoderFallbackException("Unable to encode surrogate.", value, index + offset); total += GetByteCount(EncoderReplacementString.ToCharArray(), 0, EncoderReplacementString.Length); continue; } total += 4; }
        return total;
    }
    public unsafe override int GetByteCount(char* chars, int count) => base.GetByteCount(chars, count);
    public override int GetByteCount(ReadOnlySpan<char> chars)
    {
        var total = 0;
        for (var offset = 0; offset < chars.Length; offset++)
        {
            var value = chars[offset];
            if (value is >= '\uD800' and <= '\uDBFF' && offset + 1 < chars.Length && chars[offset + 1] is >= '\uDC00' and <= '\uDFFF') offset++;
            else if (value is >= '\uD800' and <= '\uDFFF') { if (UsesEncoderExceptionFallback) throw new EncoderFallbackException("Unable to encode surrogate.", value, offset); total += GetByteCount(EncoderReplacementString.AsSpan()); continue; }
            total += 4;
        }
        return total;
    }
    public override int GetByteCount(string s) => base.GetByteCount(s);
    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        ArgumentNullException.ThrowIfNull(chars); ArgumentNullException.ThrowIfNull(bytes); ValidateRange(charIndex, charCount, chars.Length); ValidateDestination(byteIndex, bytes.Length); EnsureDestination(byteIndex, GetByteCount(chars, charIndex, charCount), bytes.Length); var written = 0; for (var offset = 0; offset < charCount; offset++) { var value = (int)chars[charIndex + offset]; if (value >= 0xD800 && value <= 0xDBFF && offset + 1 < charCount && chars[charIndex + offset + 1] >= 0xDC00 && chars[charIndex + offset + 1] <= 0xDFFF) { value = ((value - 0xD800) << 10) + (chars[charIndex + ++offset] - 0xDC00) + 0x10000; } else if (value >= 0xD800 && value <= 0xDFFF) { if (UsesEncoderExceptionFallback) throw new EncoderFallbackException("Unable to encode surrogate.", (char)value, charIndex + offset); var replacement = EncoderReplacementString.ToCharArray(); written += GetBytes(replacement, 0, replacement.Length, bytes, byteIndex + written); continue; } WriteUnit(bytes, byteIndex + written, value); written += 4; }
        return written;
    }
    public unsafe override int GetBytes(char* chars, int charCount, byte* bytes, int byteCount) => base.GetBytes(chars, charCount, bytes, byteCount);
    public override int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        var required = GetByteCount(chars);
        if (required > bytes.Length) throw new ArgumentException("The destination buffer is too small.", nameof(bytes));
        var written = 0;
        for (var offset = 0; offset < chars.Length; offset++)
        {
            var value = (int)chars[offset];
            if (value is >= 0xD800 and <= 0xDBFF && offset + 1 < chars.Length && chars[offset + 1] is >= '\uDC00' and <= '\uDFFF') value = ((value - 0xD800) << 10) + (chars[++offset] - 0xDC00) + 0x10000;
            else if (value is >= 0xD800 and <= 0xDFFF) { if (UsesEncoderExceptionFallback) throw new EncoderFallbackException("Unable to encode surrogate.", (char)value, offset); written += GetBytes(EncoderReplacementString.AsSpan(), bytes.Slice(written)); continue; }
            WriteUnit(bytes, written, value); written += 4;
        }
        return written;
    }
    public override int GetBytes(string s, int charIndex, int charCount, byte[] bytes, int byteIndex) => base.GetBytes(s, charIndex, charCount, bytes, byteIndex);
    public override int GetCharCount(byte[] bytes, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes); ValidateRange(index, count, bytes.Length); var total = 0; for (var offset = 0; offset + 3 < count; offset += 4) { var value = ReadUnit(bytes, index + offset); total += Rune.IsValid(value) ? (value > 0xFFFF ? 2 : 1) : ReplacementChars(bytes, index + offset, 4); }
        if ((count & 3) != 0) total += ReplacementChars(bytes, index + count - (count & 3), count & 3); return total;
    }
    public unsafe override int GetCharCount(byte* bytes, int count) => base.GetCharCount(bytes, count);
    public override int GetCharCount(ReadOnlySpan<byte> bytes)
    {
        var total = 0;
        for (var offset = 0; offset + 3 < bytes.Length; offset += 4) { var value = ReadUnit(bytes, offset); total += Rune.IsValid(value) ? value > 0xFFFF ? 2 : 1 : ReplacementChars(bytes, offset, 4); }
        if ((bytes.Length & 3) != 0) total += ReplacementChars(bytes, bytes.Length - (bytes.Length & 3), bytes.Length & 3);
        return total;
    }
    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        ArgumentNullException.ThrowIfNull(bytes); ArgumentNullException.ThrowIfNull(chars); ValidateRange(byteIndex, byteCount, bytes.Length); ValidateDestination(charIndex, chars.Length); EnsureDestination(charIndex, GetCharCount(bytes, byteIndex, byteCount), chars.Length); var written = 0; for (var offset = 0; offset + 3 < byteCount; offset += 4) { var value = ReadUnit(bytes, byteIndex + offset); if (!Rune.IsValid(value)) written += WriteReplacement(chars, charIndex + written, bytes, byteIndex + offset, 4); else { var rune = new Rune(value); written += rune.EncodeToUtf16(new Span<char>(chars, charIndex + written, chars.Length - charIndex - written)); } }
        if ((byteCount & 3) != 0) written += WriteReplacement(chars, charIndex + written, bytes, byteIndex + byteCount - (byteCount & 3), byteCount & 3); return written;
    }
    public unsafe override int GetChars(byte* bytes, int byteCount, char* chars, int charCount) => base.GetChars(bytes, byteCount, chars, charCount);
    public override int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars)
    {
        var required = GetCharCount(bytes);
        if (required > chars.Length) throw new ArgumentException("The destination buffer is too small.", nameof(chars));
        var written = 0;
        for (var offset = 0; offset + 3 < bytes.Length; offset += 4)
        {
            var value = ReadUnit(bytes, offset);
            if (!Rune.IsValid(value)) written += WriteReplacement(chars, written, bytes, offset, 4);
            else written += new Rune(value).EncodeToUtf16(chars.Slice(written));
        }
        if ((bytes.Length & 3) != 0) written += WriteReplacement(chars, written, bytes, bytes.Length - (bytes.Length & 3), bytes.Length & 3);
        return written;
    }
    public override Decoder GetDecoder() => base.GetDecoder();
    public override Encoder GetEncoder() => base.GetEncoder();
    public override int GetHashCode() => CodePage + (_bigEndian ? 1 : 0) + (_emitIdentifier ? 2 : 0) + EncoderFallback.GetHashCode() + DecoderFallback.GetHashCode();
    public override byte[] GetPreamble() => base.GetPreamble();
    public override string GetString(byte[] bytes, int index, int count) => base.GetString(bytes, index, count);
    public override bool Equals(object? value) => value is UTF32Encoding other && _bigEndian == other._bigEndian && _emitIdentifier == other._emitIdentifier && EncoderFallback.Equals(other.EncoderFallback) && DecoderFallback.Equals(other.DecoderFallback);
    public override int GetMaxByteCount(int charCount) { if (charCount < 0) throw new ArgumentOutOfRangeException(nameof(charCount)); var count = ((long)charCount + 1) * 4; if (EncoderFallback.MaxCharCount > 1) count *= EncoderFallback.MaxCharCount; if (count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(charCount)); return (int)count; }
    public override int GetMaxCharCount(int byteCount) { if (byteCount < 0) throw new ArgumentOutOfRangeException(nameof(byteCount)); var count = (long)(byteCount / 2) + 2; if (DecoderFallback.MaxCharCount > 2) count = count * DecoderFallback.MaxCharCount / 2; if (count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(byteCount)); return (int)count; }
    private int ReadUnit(byte[] bytes, int index) => _bigEndian ? bytes[index] << 24 | bytes[index + 1] << 16 | bytes[index + 2] << 8 | bytes[index + 3] : bytes[index] | bytes[index + 1] << 8 | bytes[index + 2] << 16 | bytes[index + 3] << 24;
    private int ReadUnit(ReadOnlySpan<byte> bytes, int index) => _bigEndian ? bytes[index] << 24 | bytes[index + 1] << 16 | bytes[index + 2] << 8 | bytes[index + 3] : bytes[index] | bytes[index + 1] << 8 | bytes[index + 2] << 16 | bytes[index + 3] << 24;
    private void WriteUnit(byte[] bytes, int index, int value) { if (_bigEndian) { bytes[index] = (byte)(value >> 24); bytes[index + 1] = (byte)(value >> 16); bytes[index + 2] = (byte)(value >> 8); bytes[index + 3] = (byte)value; } else { bytes[index] = (byte)value; bytes[index + 1] = (byte)(value >> 8); bytes[index + 2] = (byte)(value >> 16); bytes[index + 3] = (byte)(value >> 24); } }
    private void WriteUnit(Span<byte> bytes, int index, int value) { if (_bigEndian) { bytes[index] = (byte)(value >> 24); bytes[index + 1] = (byte)(value >> 16); bytes[index + 2] = (byte)(value >> 8); bytes[index + 3] = (byte)value; } else { bytes[index] = (byte)value; bytes[index + 1] = (byte)(value >> 8); bytes[index + 2] = (byte)(value >> 16); bytes[index + 3] = (byte)(value >> 24); } }
    private int ReplacementChars(byte[] bytes, int index, int count) { if (UsesDecoderExceptionFallback) throw new DecoderFallbackException("Unable to decode bytes.", CopyBytes(bytes, index, count), index); return DecoderReplacementString.Length; }
    private int ReplacementChars(ReadOnlySpan<byte> bytes, int index, int count) { if (UsesDecoderExceptionFallback) throw new DecoderFallbackException("Unable to decode bytes.", bytes.Slice(index, count).ToArray(), index); return DecoderReplacementString.Length; }
    private int WriteReplacement(char[] chars, int index, byte[] bytes, int byteIndex, int byteCount) { var replacement = DecoderReplacementString; if (UsesDecoderExceptionFallback) throw new DecoderFallbackException("Unable to decode bytes.", CopyBytes(bytes, byteIndex, byteCount), byteIndex); for (var offset = 0; offset < replacement.Length; offset++) chars[index + offset] = replacement[offset]; return replacement.Length; }
    private int WriteReplacement(Span<char> chars, int index, ReadOnlySpan<byte> bytes, int byteIndex, int byteCount) { var replacement = DecoderReplacementString; if (UsesDecoderExceptionFallback) throw new DecoderFallbackException("Unable to decode bytes.", bytes.Slice(byteIndex, byteCount).ToArray(), byteIndex); for (var offset = 0; offset < replacement.Length; offset++) chars[index + offset] = replacement[offset]; return replacement.Length; }
    private static byte[] CopyBytes(byte[] bytes, int index, int count) { var result = new byte[count]; for (var offset = 0; offset < count; offset++) result[offset] = bytes[index + offset]; return result; }
}
