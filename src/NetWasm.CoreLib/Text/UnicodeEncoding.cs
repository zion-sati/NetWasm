// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under the MIT license.
namespace System.Text;

public class UnicodeEncoding : Encoding
{
    internal static readonly UnicodeEncoding LittleEndianDefault = ReadOnly(new UnicodeEncoding(false, true));
    internal static readonly UnicodeEncoding BigEndianDefault = ReadOnly(new UnicodeEncoding(true, true));
    private readonly bool _bigEndian;
    private readonly bool _emitIdentifier;
    public const int CharSize = 2;
    public UnicodeEncoding() : this(false, true) { }
    public UnicodeEncoding(bool bigEndian, bool byteOrderMark) : this(bigEndian, byteOrderMark, false) { }
    public UnicodeEncoding(bool bigEndian, bool byteOrderMark, bool throwOnInvalidBytes) : base(
        bigEndian ? 1201 : 1200,
        throwOnInvalidBytes ? EncoderFallback.ExceptionFallback : new EncoderReplacementFallback("\uFFFD"),
        throwOnInvalidBytes ? DecoderFallback.ExceptionFallback : new DecoderReplacementFallback("\uFFFD"))
    {
        _bigEndian = bigEndian; _emitIdentifier = byteOrderMark;
    }
    public override string EncodingName => _bigEndian ? "Unicode (Big-Endian)" : "Unicode";
    public override string WebName => _bigEndian ? "utf-16BE" : "utf-16";
    public override ReadOnlySpan<byte> Preamble { get => _emitIdentifier ? (_bigEndian ? new byte[] { 0xFE, 0xFF } : new byte[] { 0xFF, 0xFE }) : ReadOnlySpan<byte>.Empty; }
    public override int GetByteCount(char[] chars, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(chars);
        ValidateRange(index, count, chars.Length);
        var total = 0;
        for (var offset = 0; offset < count; offset++)
        {
            var value = chars[index + offset];
            if (value >= 0xD800 && value <= 0xDBFF &&
                offset + 1 < count &&
                chars[index + offset + 1] >= 0xDC00 &&
                chars[index + offset + 1] <= 0xDFFF)
            {
                offset++;
                total += 4;
            }
            else if (value >= 0xD800 && value <= 0xDFFF)
            {
                if (UsesEncoderExceptionFallback) throw new EncoderFallbackException("Unable to encode surrogate.", value, index + offset);

                total += EncoderReplacementString.Length * 2;
            }
            else
            {
                total += 2;
            }
        }

        return total;
    }
    public unsafe override int GetByteCount(char* chars, int count) => base.GetByteCount(chars, count);
    public override int GetByteCount(string s) => base.GetByteCount(s);
    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        ArgumentNullException.ThrowIfNull(chars); ArgumentNullException.ThrowIfNull(bytes); ValidateRange(charIndex, charCount, chars.Length); ValidateDestination(byteIndex, bytes.Length);
        EnsureDestination(byteIndex, GetByteCount(chars, charIndex, charCount), bytes.Length); var written = 0;
        for (var offset = 0; offset < charCount; offset++) { var value = chars[charIndex + offset]; if (value >= 0xD800 && value <= 0xDBFF && offset + 1 < charCount && chars[charIndex + offset + 1] >= 0xDC00 && chars[charIndex + offset + 1] <= 0xDFFF) { WriteUnit(bytes, byteIndex + written, value); WriteUnit(bytes, byteIndex + written + 2, chars[charIndex + ++offset]); written += 4; } else if (value >= 0xD800 && value <= 0xDFFF) { if (UsesEncoderExceptionFallback) throw new EncoderFallbackException("Unable to encode surrogate.", value, charIndex + offset); var replacement = EncoderReplacementString.ToCharArray(); written += GetBytes(replacement, 0, replacement.Length, bytes, byteIndex + written); } else { WriteUnit(bytes, byteIndex + written, value); written += 2; } }
        return written;
    }
    public unsafe override int GetBytes(char* chars, int charCount, byte* bytes, int byteCount) => base.GetBytes(chars, charCount, bytes, byteCount);
    public override int GetBytes(string s, int charIndex, int charCount, byte[] bytes, int byteIndex) => base.GetBytes(s, charIndex, charCount, bytes, byteIndex);
    public override int GetCharCount(byte[] bytes, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes); ValidateRange(index, count, bytes.Length); var total = 0; for (var offset = 0; offset + 1 < count; offset += 2) { var value = ReadUnit(bytes, index + offset); if (value >= 0xD800 && value <= 0xDBFF) { if (offset + 3 < count && IsLow(ReadUnit(bytes, index + offset + 2))) { total += 2; offset += 2; } else total += ReplacementChars(bytes, index + offset); } else if (value >= 0xDC00 && value <= 0xDFFF) total += ReplacementChars(bytes, index + offset); else total++; }
        if ((count & 1) != 0) total += ReplacementChars(bytes, index + count - 1); return total;
    }
    public unsafe override int GetCharCount(byte* bytes, int count) => base.GetCharCount(bytes, count);
    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        ArgumentNullException.ThrowIfNull(bytes); ArgumentNullException.ThrowIfNull(chars); ValidateRange(byteIndex, byteCount, bytes.Length); ValidateDestination(charIndex, chars.Length);
        EnsureDestination(charIndex, GetCharCount(bytes, byteIndex, byteCount), chars.Length); var written = 0;
        for (var offset = 0; offset + 1 < byteCount; offset += 2) { var value = ReadUnit(bytes, byteIndex + offset); if (value >= 0xD800 && value <= 0xDBFF) { if (offset + 3 < byteCount && IsLow(ReadUnit(bytes, byteIndex + offset + 2))) { chars[charIndex + written++] = (char)value; chars[charIndex + written++] = (char)ReadUnit(bytes, byteIndex + offset + 2); offset += 2; } else written += WriteReplacement(chars, charIndex + written, bytes, byteIndex + offset); } else if (value >= 0xDC00 && value <= 0xDFFF) written += WriteReplacement(chars, charIndex + written, bytes, byteIndex + offset); else chars[charIndex + written++] = (char)value; }
        if ((byteCount & 1) != 0) written += WriteReplacement(chars, charIndex + written, bytes, byteIndex + byteCount - 1); return written;
    }
    public unsafe override int GetChars(byte* bytes, int byteCount, char* chars, int charCount) => base.GetChars(bytes, byteCount, chars, charCount);
    public override Decoder GetDecoder() => base.GetDecoder();
    public override Encoder GetEncoder() => base.GetEncoder();
    public override byte[] GetPreamble() => base.GetPreamble();
    public override string GetString(byte[] bytes, int index, int count) => base.GetString(bytes, index, count);
    public override int GetMaxByteCount(int charCount) { if (charCount < 0) throw new ArgumentOutOfRangeException(nameof(charCount)); var count = ((long)charCount + 1) * 2; if (EncoderFallback.MaxCharCount > 1) count *= EncoderFallback.MaxCharCount; if (count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(charCount)); return (int)count; }
    public override int GetMaxCharCount(int byteCount) { if (byteCount < 0) throw new ArgumentOutOfRangeException(nameof(byteCount)); var count = (long)(byteCount / 2) + (byteCount & 1) + 1; if (DecoderFallback.MaxCharCount > 1) count *= DecoderFallback.MaxCharCount; if (count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(byteCount)); return (int)count; }
    public override bool Equals(object? value) => value is UnicodeEncoding other && _bigEndian == other._bigEndian && _emitIdentifier == other._emitIdentifier && EncoderFallback.Equals(other.EncoderFallback) && DecoderFallback.Equals(other.DecoderFallback);
    public override int GetHashCode() => CodePage + (_bigEndian ? 1 : 0) + (_emitIdentifier ? 2 : 0) + EncoderFallback.GetHashCode() + DecoderFallback.GetHashCode();
    private int ReadUnit(byte[] bytes, int index) => _bigEndian ? bytes[index] << 8 | bytes[index + 1] : bytes[index] | bytes[index + 1] << 8;
    private void WriteUnit(byte[] bytes, int index, int value) { if (_bigEndian) { bytes[index] = (byte)(value >> 8); bytes[index + 1] = (byte)value; } else { bytes[index] = (byte)value; bytes[index + 1] = (byte)(value >> 8); } }
    private bool IsLow(int value) => value >= 0xDC00 && value <= 0xDFFF;
    private int ReplacementChars(byte[] bytes, int index) { if (UsesDecoderExceptionFallback) throw new DecoderFallbackException("Unable to decode bytes.", bytes, index); return DecoderReplacementString.Length; }
    private int WriteReplacement(char[] chars, int index, byte[] bytes, int byteIndex) { var replacement = DecoderReplacementString; if (UsesDecoderExceptionFallback) throw new DecoderFallbackException("Unable to decode bytes.", bytes, byteIndex); for (var offset = 0; offset < replacement.Length; offset++) chars[index + offset] = replacement[offset]; return replacement.Length; }
}
