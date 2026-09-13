// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
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
        ArgumentNullException.ThrowIfNull(chars); ValidateRange(index, count, chars.Length); var total = 0; for (var offset = 0; offset < count; offset++) { var value = chars[index + offset]; if (value >= 0xD800 && value <= 0xDBFF && offset + 1 < count && chars[index + offset + 1] >= 0xDC00 && chars[index + offset + 1] <= 0xDFFF) offset++; else if (value >= 0xD800 && value <= 0xDFFF) { if (UsesEncoderExceptionFallback) throw new EncoderFallbackException("Unable to encode surrogate.", value, index + offset); total += EncoderReplacementString.Length * 4; continue; } total += 4; }
        return total;
    }
    public unsafe override int GetByteCount(char* chars, int count) => base.GetByteCount(chars, count);
    public override int GetByteCount(string s) => base.GetByteCount(s);
    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        ArgumentNullException.ThrowIfNull(chars); ArgumentNullException.ThrowIfNull(bytes); ValidateRange(charIndex, charCount, chars.Length); ValidateDestination(byteIndex, bytes.Length); EnsureDestination(byteIndex, GetByteCount(chars, charIndex, charCount), bytes.Length); var written = 0; for (var offset = 0; offset < charCount; offset++) { var value = (int)chars[charIndex + offset]; if (value >= 0xD800 && value <= 0xDBFF && offset + 1 < charCount && chars[charIndex + offset + 1] >= 0xDC00 && chars[charIndex + offset + 1] <= 0xDFFF) { value = ((value - 0xD800) << 10) + (chars[charIndex + ++offset] - 0xDC00) + 0x10000; } else if (value >= 0xD800 && value <= 0xDFFF) { if (UsesEncoderExceptionFallback) throw new EncoderFallbackException("Unable to encode surrogate.", (char)value, charIndex + offset); var replacement = EncoderReplacementString.ToCharArray(); written += GetBytes(replacement, 0, replacement.Length, bytes, byteIndex + written); continue; } WriteUnit(bytes, byteIndex + written, value); written += 4; }
        return written;
    }
    public unsafe override int GetBytes(char* chars, int charCount, byte* bytes, int byteCount) => base.GetBytes(chars, charCount, bytes, byteCount);
    public override int GetBytes(string s, int charIndex, int charCount, byte[] bytes, int byteIndex) => base.GetBytes(s, charIndex, charCount, bytes, byteIndex);
    public override int GetCharCount(byte[] bytes, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes); ValidateRange(index, count, bytes.Length); var total = 0; for (var offset = 0; offset + 3 < count; offset += 4) { var value = ReadUnit(bytes, index + offset); total += Rune.IsValid(value) ? (value > 0xFFFF ? 2 : 1) : ReplacementChars(bytes, index + offset); }
        if ((count & 3) != 0) total += ReplacementChars(bytes, index + count - (count & 3)); return total;
    }
    public unsafe override int GetCharCount(byte* bytes, int count) => base.GetCharCount(bytes, count);
    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        ArgumentNullException.ThrowIfNull(bytes); ArgumentNullException.ThrowIfNull(chars); ValidateRange(byteIndex, byteCount, bytes.Length); ValidateDestination(charIndex, chars.Length); EnsureDestination(charIndex, GetCharCount(bytes, byteIndex, byteCount), chars.Length); var written = 0; for (var offset = 0; offset + 3 < byteCount; offset += 4) { var value = ReadUnit(bytes, byteIndex + offset); if (!Rune.IsValid(value)) written += WriteReplacement(chars, charIndex + written, bytes, byteIndex + offset); else { var rune = new Rune(value); written += rune.EncodeToUtf16(new Span<char>(chars, charIndex + written, chars.Length - charIndex - written)); } }
        if ((byteCount & 3) != 0) written += WriteReplacement(chars, charIndex + written, bytes, byteIndex + byteCount - (byteCount & 3)); return written;
    }
    public unsafe override int GetChars(byte* bytes, int byteCount, char* chars, int charCount) => base.GetChars(bytes, byteCount, chars, charCount);
    public override Decoder GetDecoder() => base.GetDecoder();
    public override Encoder GetEncoder() => base.GetEncoder();
    public override int GetHashCode() => CodePage + (_bigEndian ? 1 : 0) + (_emitIdentifier ? 2 : 0) + EncoderFallback.GetHashCode() + DecoderFallback.GetHashCode();
    public override byte[] GetPreamble() => base.GetPreamble();
    public override string GetString(byte[] bytes, int index, int count) => base.GetString(bytes, index, count);
    public override bool Equals(object? value) => value is UTF32Encoding other && _bigEndian == other._bigEndian && _emitIdentifier == other._emitIdentifier && EncoderFallback.Equals(other.EncoderFallback) && DecoderFallback.Equals(other.DecoderFallback);
    public override int GetMaxByteCount(int charCount) { if (charCount < 0) throw new ArgumentOutOfRangeException(nameof(charCount)); var count = ((long)charCount + 1) * 4; if (EncoderFallback.MaxCharCount > 1) count *= EncoderFallback.MaxCharCount; if (count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(charCount)); return (int)count; }
    public override int GetMaxCharCount(int byteCount) { if (byteCount < 0) throw new ArgumentOutOfRangeException(nameof(byteCount)); var count = (long)(byteCount / 2) + 2; if (DecoderFallback.MaxCharCount > 2) count = count * DecoderFallback.MaxCharCount / 2; if (count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(byteCount)); return (int)count; }
    private int ReadUnit(byte[] bytes, int index) => _bigEndian ? bytes[index] << 24 | bytes[index + 1] << 16 | bytes[index + 2] << 8 | bytes[index + 3] : bytes[index] | bytes[index + 1] << 8 | bytes[index + 2] << 16 | bytes[index + 3] << 24;
    private void WriteUnit(byte[] bytes, int index, int value) { if (_bigEndian) { bytes[index] = (byte)(value >> 24); bytes[index + 1] = (byte)(value >> 16); bytes[index + 2] = (byte)(value >> 8); bytes[index + 3] = (byte)value; } else { bytes[index] = (byte)value; bytes[index + 1] = (byte)(value >> 8); bytes[index + 2] = (byte)(value >> 16); bytes[index + 3] = (byte)(value >> 24); } }
    private int ReplacementChars(byte[] bytes, int index) { if (UsesDecoderExceptionFallback) throw new DecoderFallbackException("Unable to decode bytes.", bytes, index); return DecoderReplacementString.Length; }
    private int WriteReplacement(char[] chars, int index, byte[] bytes, int byteIndex) { var replacement = DecoderReplacementString; if (UsesDecoderExceptionFallback) throw new DecoderFallbackException("Unable to decode bytes.", bytes, byteIndex); for (var offset = 0; offset < replacement.Length; offset++) chars[index + offset] = replacement[offset]; return replacement.Length; }
}
