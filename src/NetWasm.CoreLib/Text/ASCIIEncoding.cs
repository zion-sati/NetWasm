// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under the MIT license.
namespace System.Text;

public class ASCIIEncoding : Encoding
{
    internal static readonly new ASCIIEncoding Default = ReadOnly(new ASCIIEncoding());
    public ASCIIEncoding() : base(20127) { }
    public override bool IsSingleByte { get => true; }
    public override string EncodingName => "US-ASCII";
    public override string WebName => "us-ascii";
    public override int GetByteCount(char[] chars, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(chars); ValidateRange(index, count, chars.Length); var result = 0;
        for (var offset = 0; offset < count; offset++) result += chars[index + offset] <= 0x7F ? 1 : ReplacementBytes(chars[index + offset], index + offset);
        return result;
    }
    public unsafe override int GetByteCount(char* chars, int count) => base.GetByteCount(chars, count);
    public override int GetByteCount(ReadOnlySpan<char> chars)
    {
        var result = 0;
        for (var index = 0; index < chars.Length; index++)
            result += chars[index] <= 0x7F ? 1 : ReplacementBytes(chars[index], index);
        return result;
    }
    public override int GetByteCount(string chars) => base.GetByteCount(chars);
    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        ArgumentNullException.ThrowIfNull(chars); ArgumentNullException.ThrowIfNull(bytes); ValidateRange(charIndex, charCount, chars.Length); ValidateDestination(byteIndex, bytes.Length);
        EnsureDestination(byteIndex, GetByteCount(chars, charIndex, charCount), bytes.Length);
        var written = 0; for (var offset = 0; offset < charCount; offset++) { var value = chars[charIndex + offset]; if (value <= 0x7F) bytes[byteIndex + written++] = (byte)value; else written += WriteReplacement(bytes, byteIndex + written, value); }
        return written;
    }
    public unsafe override int GetBytes(char* chars, int charCount, byte* bytes, int byteCount) => base.GetBytes(chars, charCount, bytes, byteCount);
    public override int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes)
    {
        var required = GetByteCount(chars);
        if (required > bytes.Length) throw new ArgumentException("The destination buffer is too small.", nameof(bytes));
        var written = 0;
        for (var index = 0; index < chars.Length; index++)
        {
            var value = chars[index];
            if (value <= 0x7F) bytes[written++] = (byte)value;
            else written += WriteReplacement(bytes, written, value);
        }
        return written;
    }
    public override int GetBytes(string chars, int charIndex, int charCount, byte[] bytes, int byteIndex) => base.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
    public override int GetCharCount(byte[] bytes, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes); ValidateRange(index, count, bytes.Length); var result = 0; for (var offset = 0; offset < count; offset++) result += bytes[index + offset] <= 0x7F ? 1 : ReplacementChars(bytes[index + offset], index + offset); return result;
    }
    public unsafe override int GetCharCount(byte* bytes, int count) => base.GetCharCount(bytes, count);
    public override int GetCharCount(ReadOnlySpan<byte> bytes)
    {
        var result = 0;
        for (var index = 0; index < bytes.Length; index++)
            result += bytes[index] <= 0x7F ? 1 : ReplacementChars(bytes[index], index);
        return result;
    }
    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        ArgumentNullException.ThrowIfNull(bytes); ArgumentNullException.ThrowIfNull(chars); ValidateRange(byteIndex, byteCount, bytes.Length); ValidateDestination(charIndex, chars.Length);
        EnsureDestination(charIndex, GetCharCount(bytes, byteIndex, byteCount), chars.Length);
        var written = 0; for (var offset = 0; offset < byteCount; offset++) { var value = bytes[byteIndex + offset]; if (value <= 0x7F) chars[charIndex + written++] = (char)value; else written += WriteReplacement(chars, charIndex + written, value); }
        return written;
    }
    public unsafe override int GetChars(byte* bytes, int byteCount, char* chars, int charCount) => base.GetChars(bytes, byteCount, chars, charCount);
    public override int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars)
    {
        var required = GetCharCount(bytes);
        if (required > chars.Length) throw new ArgumentException("The destination buffer is too small.", nameof(chars));
        var written = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            if (value <= 0x7F) chars[written++] = (char)value;
            else written += WriteReplacement(chars, written);
        }
        return written;
    }
    public override Decoder GetDecoder() => base.GetDecoder();
    public override Encoder GetEncoder() => base.GetEncoder();
    public override string GetString(byte[] bytes, int byteIndex, int byteCount) => base.GetString(bytes, byteIndex, byteCount);
    public override bool TryGetBytes(ReadOnlySpan<char> chars, Span<byte> bytes, out int bytesWritten) => base.TryGetBytes(chars, bytes, out bytesWritten);
    public override bool TryGetChars(ReadOnlySpan<byte> bytes, Span<char> chars, out int charsWritten) => base.TryGetChars(bytes, chars, out charsWritten);
    public override int GetMaxByteCount(int charCount)
    {
        if (charCount < 0) throw new ArgumentOutOfRangeException(nameof(charCount));
        var count = ((long)charCount + 1) * (EncoderFallback.MaxCharCount > 1 ? EncoderFallback.MaxCharCount : 1);
        if (count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(charCount));
        return (int)count;
    }

    public override int GetMaxCharCount(int byteCount)
    {
        if (byteCount < 0) throw new ArgumentOutOfRangeException(nameof(byteCount));
        var count = (long)byteCount * (DecoderFallback.MaxCharCount > 1 ? DecoderFallback.MaxCharCount : 1);
        if (count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(byteCount));
        return (int)count;
    }

    public override bool Equals(object? value) => value is ASCIIEncoding other && EncoderFallback.Equals(other.EncoderFallback) && DecoderFallback.Equals(other.DecoderFallback);
    public override int GetHashCode() => CodePage + EncoderFallback.GetHashCode() + DecoderFallback.GetHashCode();
    private int ReplacementBytes(char value, int index) { if (UsesEncoderExceptionFallback) throw new EncoderFallbackException("Unable to encode character.", value, index); return GetReplacementByteCount(); }
    private int ReplacementChars(byte value, int index) { if (UsesDecoderExceptionFallback) throw new DecoderFallbackException("Unable to decode byte.", new[] { value }, index); return DecoderReplacementString.Length; }
    private int WriteReplacement(byte[] output, int index, char value) { var replacement = EncoderReplacementString; for (var offset = 0; offset < replacement.Length; offset++) { if (replacement[offset] > 0x7F) throw new ArgumentException("Recursive fallback is not allowed."); output[index + offset] = (byte)replacement[offset]; } return replacement.Length; }
    private int WriteReplacement(char[] output, int index, byte value) { var replacement = DecoderReplacementString; for (var offset = 0; offset < replacement.Length; offset++) output[index + offset] = replacement[offset]; return replacement.Length; }
    private int WriteReplacement(Span<byte> output, int index, char value) { var replacement = EncoderReplacementString; for (var offset = 0; offset < replacement.Length; offset++) { if (replacement[offset] > 0x7F) throw new ArgumentException("Recursive fallback is not allowed."); output[index + offset] = (byte)replacement[offset]; } return replacement.Length; }
    private int WriteReplacement(Span<char> output, int index) { var replacement = DecoderReplacementString; for (var offset = 0; offset < replacement.Length; offset++) output[index + offset] = replacement[offset]; return replacement.Length; }
    private int GetReplacementByteCount() { var replacement = EncoderReplacementString; for (var offset = 0; offset < replacement.Length; offset++) if (replacement[offset] > 0x7F) throw new ArgumentException("Recursive fallback is not allowed."); return replacement.Length; }
}
