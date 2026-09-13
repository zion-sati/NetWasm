// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under the MIT license.
namespace System.Text;

public class UTF8Encoding : Encoding
{
    internal static readonly new UTF8Encoding Default = ReadOnly(new UTF8Encoding(true));
    private readonly bool _emitIdentifier;
    public UTF8Encoding() : this(false, false) { }
    public UTF8Encoding(bool encoderShouldEmitUTF8Identifier) : this(encoderShouldEmitUTF8Identifier, false) { }
    public UTF8Encoding(bool encoderShouldEmitUTF8Identifier, bool throwOnInvalidBytes) : base(
        65001,
        throwOnInvalidBytes ? EncoderFallback.ExceptionFallback : new EncoderReplacementFallback("\uFFFD"),
        throwOnInvalidBytes ? DecoderFallback.ExceptionFallback : new DecoderReplacementFallback("\uFFFD"))
    {
        _emitIdentifier = encoderShouldEmitUTF8Identifier;
    }
    public override string EncodingName => "Unicode (UTF-8)";
    public override string WebName => "utf-8";
    public override ReadOnlySpan<byte> Preamble { get => _emitIdentifier ? new byte[] { 0xEF, 0xBB, 0xBF } : ReadOnlySpan<byte>.Empty; }
    public override int GetByteCount(char[] chars, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(chars); ValidateRange(index, count, chars.Length); var total = 0;
        for (var offset = 0; offset < count; offset++)
        {
            var value = chars[index + offset];
            if (value <= 0x7F) total++;
            else if (value <= 0x7FF) total += 2;
            else if (value >= 0xD800 && value <= 0xDBFF && offset + 1 < count && chars[index + offset + 1] >= 0xDC00 && chars[index + offset + 1] <= 0xDFFF) { total += 4; offset++; }
            else if (value >= 0xD800 && value <= 0xDFFF) total += ReplacementByteCount(value, index + offset);
            else total += 3;
        }
        return total;
    }
    public unsafe override int GetByteCount(char* chars, int count) => base.GetByteCount(chars, count);
    public override int GetByteCount(ReadOnlySpan<char> chars) => base.GetByteCount(chars);
    public override int GetByteCount(string chars) => base.GetByteCount(chars);
    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        ArgumentNullException.ThrowIfNull(chars); ArgumentNullException.ThrowIfNull(bytes); ValidateRange(charIndex, charCount, chars.Length); ValidateDestination(byteIndex, bytes.Length);
        EnsureDestination(byteIndex, GetByteCount(chars, charIndex, charCount), bytes.Length);
        var written = 0;
        for (var offset = 0; offset < charCount; offset++)
        {
            var value = chars[charIndex + offset];
            if (value <= 0x7F) bytes[byteIndex + written++] = (byte)value;
            else if (value <= 0x7FF) { bytes[byteIndex + written++] = (byte)(0xC0 | value >> 6); bytes[byteIndex + written++] = (byte)(0x80 | (value & 0x3F)); }
            else if (value >= 0xD800 && value <= 0xDBFF && offset + 1 < charCount && chars[charIndex + offset + 1] >= 0xDC00 && chars[charIndex + offset + 1] <= 0xDFFF) { var scalar = ((value - 0xD800) << 10) + (chars[charIndex + ++offset] - 0xDC00) + 0x10000; bytes[byteIndex + written++] = (byte)(0xF0 | scalar >> 18); bytes[byteIndex + written++] = (byte)(0x80 | (scalar >> 12 & 0x3F)); bytes[byteIndex + written++] = (byte)(0x80 | (scalar >> 6 & 0x3F)); bytes[byteIndex + written++] = (byte)(0x80 | (scalar & 0x3F)); }
            else if (value >= 0xD800 && value <= 0xDFFF) written += WriteReplacement(bytes, byteIndex + written, value);
            else { bytes[byteIndex + written++] = (byte)(0xE0 | value >> 12); bytes[byteIndex + written++] = (byte)(0x80 | (value >> 6 & 0x3F)); bytes[byteIndex + written++] = (byte)(0x80 | (value & 0x3F)); }
        }
        return written;
    }
    public unsafe override int GetBytes(char* chars, int charCount, byte* bytes, int byteCount) => base.GetBytes(chars, charCount, bytes, byteCount);
    public override int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes) => base.GetBytes(chars, bytes);
    public override int GetBytes(string s, int charIndex, int charCount, byte[] bytes, int byteIndex) => base.GetBytes(s, charIndex, charCount, bytes, byteIndex);
    public override int GetCharCount(byte[] bytes, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes); ValidateRange(index, count, bytes.Length); var total = 0;
        for (var offset = 0; offset < count;)
        { var status = Rune.DecodeFromUtf8(new ReadOnlySpan<byte>(bytes, index + offset, count - offset), out var rune, out var consumed); if (status == System.Buffers.OperationStatus.Done) total += rune.Utf16SequenceLength; else { total += ReplacementCharCount(bytes[index + offset], index + offset); consumed = consumed == 0 ? 1 : consumed; } offset += consumed; }
        return total;
    }
    public unsafe override int GetCharCount(byte* bytes, int count) => base.GetCharCount(bytes, count);
    public override int GetCharCount(ReadOnlySpan<byte> bytes) => base.GetCharCount(bytes);
    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        ArgumentNullException.ThrowIfNull(bytes); ArgumentNullException.ThrowIfNull(chars); ValidateRange(byteIndex, byteCount, bytes.Length); ValidateDestination(charIndex, chars.Length);
        EnsureDestination(charIndex, GetCharCount(bytes, byteIndex, byteCount), chars.Length); var written = 0;
        for (var offset = 0; offset < byteCount;)
        { var status = Rune.DecodeFromUtf8(new ReadOnlySpan<byte>(bytes, byteIndex + offset, byteCount - offset), out var rune, out var consumed); if (status == System.Buffers.OperationStatus.Done) { written += rune.EncodeToUtf16(new Span<char>(chars, charIndex + written, chars.Length - charIndex - written)); } else { written += ReplacementCharCount(bytes[byteIndex + offset], byteIndex + offset); WriteReplacement(chars, charIndex + written - DecoderReplacementString.Length); consumed = consumed == 0 ? 1 : consumed; } offset += consumed; }
        return written;
    }
    public unsafe override int GetChars(byte* bytes, int byteCount, char* chars, int charCount) => base.GetChars(bytes, byteCount, chars, charCount);
    public override int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars) => base.GetChars(bytes, chars);
    public override Decoder GetDecoder() => base.GetDecoder();
    public override Encoder GetEncoder() => base.GetEncoder();
    public override byte[] GetPreamble() => base.GetPreamble();
    public override string GetString(byte[] bytes, int index, int count) => base.GetString(bytes, index, count);
    public override bool TryGetBytes(ReadOnlySpan<char> chars, Span<byte> bytes, out int bytesWritten) => base.TryGetBytes(chars, bytes, out bytesWritten);
    public override bool TryGetChars(ReadOnlySpan<byte> bytes, Span<char> chars, out int charsWritten) => base.TryGetChars(bytes, chars, out charsWritten);
    public override int GetMaxByteCount(int charCount)
    {
        if (charCount < 0) throw new ArgumentOutOfRangeException(nameof(charCount));
        var count = ((long)charCount + 1) * 3;
        if (EncoderFallback.MaxCharCount > 1) count *= EncoderFallback.MaxCharCount;
        if (count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(charCount));
        return (int)count;
    }

    public override int GetMaxCharCount(int byteCount)
    {
        if (byteCount < 0) throw new ArgumentOutOfRangeException(nameof(byteCount));
        var count = (long)byteCount + 1;
        if (DecoderFallback.MaxCharCount > 1) count *= DecoderFallback.MaxCharCount;
        if (count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(byteCount));
        return (int)count;
    }

    public override bool Equals(object? value) => value is UTF8Encoding other && _emitIdentifier == other._emitIdentifier && EncoderFallback.Equals(other.EncoderFallback) && DecoderFallback.Equals(other.DecoderFallback);
    public override int GetHashCode() => CodePage + (_emitIdentifier ? 1 : 0) + EncoderFallback.GetHashCode() + DecoderFallback.GetHashCode();
    private int ReplacementByteCount(char value, int index) { if (UsesEncoderExceptionFallback) throw new EncoderFallbackException("Unable to encode surrogate.", value, index); return GetByteCount(EncoderReplacementString.ToCharArray(), 0, EncoderReplacementString.Length); }
    private int ReplacementCharCount(byte value, int index) { if (UsesDecoderExceptionFallback) throw new DecoderFallbackException("Unable to decode bytes.", new[] { value }, index); return DecoderReplacementString.Length; }
    private int WriteReplacement(byte[] output, int index, char value) { var replacement = EncoderReplacementString.ToCharArray(); return GetBytes(replacement, 0, replacement.Length, output, index); }
    private int WriteReplacement(char[] output, int index) { var replacement = DecoderReplacementString; for (var offset = 0; offset < replacement.Length; offset++) output[index + offset] = replacement[offset]; return replacement.Length; }
}
