// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under the MIT license.
namespace System.Text;

public abstract class Decoder
{
    internal Encoding? _encoding;
    private DecoderFallback? _fallback;
    private DecoderFallbackBuffer? _fallbackBuffer;
    private byte[]? _pendingBytes;

    protected Decoder() { }

    public DecoderFallback? Fallback
    {
        get => _fallback;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_fallbackBuffer is not null && _fallbackBuffer.Remaining != 0) throw new ArgumentException("The fallback buffer is not empty.", nameof(value));
            _fallback = value;
            _fallbackBuffer = null;
        }
    }

    public DecoderFallbackBuffer FallbackBuffer { get => _fallbackBuffer ??= (_fallback ?? DecoderFallback.ReplacementFallback).CreateFallbackBuffer(); }

    internal void Initialize(Encoding encoding) => _encoding = encoding;

    public abstract int GetCharCount(byte[] bytes, int index, int count);

    public unsafe virtual int GetCharCount(byte* bytes, int count, bool flush) => GetCharCount(new ReadOnlySpan<byte>(bytes, count), flush);

    public virtual int GetCharCount(byte[] bytes, int index, int count, bool flush)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Encoding.ValidateRange(index, count, bytes.Length);
        return GetCharCountCore(new ReadOnlySpan<byte>(bytes, index, count), flush);
    }

    public virtual int GetCharCount(ReadOnlySpan<byte> bytes, bool flush) => CountWithoutConsuming(bytes, flush);

    public virtual int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex, bool flush)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(chars);
        Encoding.ValidateRange(byteIndex, byteCount, bytes.Length);
        Encoding.ValidateDestination(charIndex, chars.Length);
        var required = CountWithoutConsuming(new ReadOnlySpan<byte>(bytes, byteIndex, byteCount), flush);
        Encoding.EnsureDestination(charIndex, required, chars.Length);
        return GetCharsCore(new ReadOnlySpan<byte>(bytes, byteIndex, byteCount), new Span<char>(chars, charIndex, chars.Length - charIndex), flush);
    }

    public abstract int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex);

    public unsafe virtual int GetChars(byte* bytes, int byteCount, char* chars, int charCount, bool flush) => GetChars(new ReadOnlySpan<byte>(bytes, byteCount), new Span<char>(chars, charCount), flush);

    public unsafe virtual void Convert(byte* bytes, int byteCount, char* chars, int charCount, bool flush, out int bytesUsed, out int charsUsed, out bool completed) =>
        Convert(new ReadOnlySpan<byte>(bytes, byteCount), new Span<char>(chars, charCount), flush, out bytesUsed, out charsUsed, out completed);

    public virtual int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars, bool flush)
    {
        var required = CountWithoutConsuming(bytes, flush);
        if (required > chars.Length) throw new ArgumentException("The destination buffer is too small.", nameof(chars));
        return GetCharsCore(bytes, chars, flush);
    }

    public virtual void Convert(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex, int charCount, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(chars);
        Encoding.ValidateRange(byteIndex, byteCount, bytes.Length);
        Encoding.ValidateDestination(charIndex, chars.Length);
        if (charCount < 0 || charCount > chars.Length - charIndex) throw new ArgumentOutOfRangeException(nameof(charCount));

        var incoming = new ReadOnlySpan<byte>(bytes, byteIndex, byteCount).ToArray();
        var pending = _pendingBytes;
        var combined = pending is null ? incoming : Combine(pending, incoming);
        var prefixLength = pending?.Length ?? 0;
        _pendingBytes = null;
        var held = !flush ? GetIncompleteTail(combined) : 0;
        var decodableLength = combined.Length - held;
        var encoding = GetEffectiveEncoding();
        var take = FindDecodablePrefix(encoding, combined, decodableLength, charCount);
        if (take == 0 && decodableLength != 0) throw new ArgumentException("The destination buffer is too small.", nameof(chars));

        var output = new Span<char>(chars, charIndex, charCount);
        charsUsed = take == 0 ? 0 : encoding.GetChars(new ReadOnlySpan<byte>(combined, 0, take), output);
        var consumed = take;
        if (held != 0 && take == decodableLength)
        {
            _pendingBytes = new byte[held];
            for (var index = 0; index < held; index++) _pendingBytes[index] = combined[decodableLength + index];
            consumed += held;
        }
        else if (take == 0)
        {
            _pendingBytes = pending;
        }
        bytesUsed = consumed > prefixLength ? consumed - prefixLength : 0;
        if (bytesUsed > byteCount) bytesUsed = byteCount;
        completed = bytesUsed == byteCount && (!flush || _pendingBytes is null) && (_fallbackBuffer is null || _fallbackBuffer.Remaining == 0);
    }

    public virtual void Convert(ReadOnlySpan<byte> bytes, Span<char> chars, bool flush, out int bytesUsed, out int charsUsed, out bool completed)
    {
        var input = bytes.ToArray();
        var output = new char[chars.Length];
        Convert(input, 0, input.Length, output, 0, output.Length, flush, out bytesUsed, out charsUsed, out completed);
        for (var index = 0; index < charsUsed; index++) chars[index] = output[index];
    }

    public virtual void Reset()
    {
        _fallbackBuffer?.Reset();
        _pendingBytes = null;
    }

    private int GetCharCountCore(ReadOnlySpan<byte> bytes, bool flush)
    {
        var combined = _pendingBytes is null ? bytes.ToArray() : Combine(_pendingBytes, bytes.ToArray());
        var hold = !flush ? GetIncompleteTail(combined) : 0;
        var result = GetEffectiveEncoding().GetCharCount(new ReadOnlySpan<byte>(combined, 0, combined.Length - hold));
        _pendingBytes = hold == 0 ? null : Tail(combined, hold);
        return result;
    }

    private int GetCharsCore(ReadOnlySpan<byte> bytes, Span<char> chars, bool flush)
    {
        var combined = _pendingBytes is null ? bytes.ToArray() : Combine(_pendingBytes, bytes.ToArray());
        var hold = !flush ? GetIncompleteTail(combined) : 0;
        var result = GetEffectiveEncoding().GetChars(new ReadOnlySpan<byte>(combined, 0, combined.Length - hold), chars);
        _pendingBytes = hold == 0 ? null : Tail(combined, hold);
        return result;
    }

    private int CountWithoutConsuming(ReadOnlySpan<byte> bytes, bool flush)
    {
        var saved = _pendingBytes;
        try { return GetCharCountCore(bytes, flush); }
        finally { _pendingBytes = saved; }
    }

    private int GetIncompleteTail(byte[] bytes)
    {
        if (_encoding is UTF8Encoding)
        {
            var start = bytes.Length;
            while (start > 0 && (bytes[start - 1] & 0xC0) == 0x80) start--;
            // Include the lead byte before the final continuation run. A tail
            // containing only continuation bytes is invalid, not incomplete.
            if (start > 0) start--;
            if (start >= 0)
            {
                var status = Rune.DecodeFromUtf8(new ReadOnlySpan<byte>(bytes, start, bytes.Length - start), out _, out _);
                if (status == System.Buffers.OperationStatus.NeedMoreData) return bytes.Length - start;
            }
        }
        else if (_encoding is UnicodeEncoding)
        {
            var oddTail = bytes.Length & 1;
            var completeLength = bytes.Length - oddTail;
            if (completeLength >= 2 && IsHighSurrogate(ReadUtf16Unit(bytes, completeLength - 2, _encoding.CodePage == 1201))) return 2 + oddTail;
            if (oddTail != 0) return 1;
        }
        else if (_encoding is UTF32Encoding && (bytes.Length & 3) != 0) return bytes.Length & 3;
        return 0;
    }

    private static int FindDecodablePrefix(Encoding encoding, byte[] bytes, int length, int charCapacity)
    {
        var accepted = 0;
        for (var index = 0; index < length;)
        {
            var next = NextBoundary(encoding, bytes, index, length);
            if (encoding.GetCharCount(new ReadOnlySpan<byte>(bytes, 0, next)) > charCapacity) break;
            accepted = next;
            index = next;
        }
        return accepted;
    }

    private static int NextBoundary(Encoding encoding, byte[] bytes, int index, int length)
    {
        if (encoding is UTF8Encoding)
        {
            var status = Rune.DecodeFromUtf8(new ReadOnlySpan<byte>(bytes, index, length - index), out _, out var consumed);
            return status == System.Buffers.OperationStatus.Done
                ? index + consumed
                : index + (consumed == 0 ? 1 : consumed);
        }
        if (encoding is UnicodeEncoding)
        {
            if (length - index < 2) return length;
            var next = index + 2;
            var value = ReadUtf16Unit(bytes, index, encoding.CodePage == 1201);
            if (IsHighSurrogate(value) && length - next >= 2 && IsLowSurrogate(ReadUtf16Unit(bytes, next, encoding.CodePage == 1201))) next += 2;
            return next;
        }
        if (encoding is UTF32Encoding) return Math.Min(index + 4, length);
        return index + 1;
    }

    private static char ReadUtf16Unit(byte[] bytes, int index, bool bigEndian) =>
        bigEndian
            ? (char)(bytes[index] << 8 | bytes[index + 1])
            : (char)(bytes[index] | bytes[index + 1] << 8);

    private static bool IsHighSurrogate(char value) => value >= 0xD800 && value <= 0xDBFF;
    private static bool IsLowSurrogate(char value) => value >= 0xDC00 && value <= 0xDFFF;

    private Encoding GetEffectiveEncoding()
    {
        if (_encoding is null) throw new InvalidOperationException("The decoder is not associated with an encoding.");
        if (_fallback is null || ReferenceEquals(_fallback, _encoding.DecoderFallback)) return _encoding;
        var clone = (Encoding)_encoding.Clone();
        clone.DecoderFallback = _fallback;
        return clone;
    }

    private static byte[] Combine(byte[] first, byte[] second)
    {
        var result = new byte[first.Length + second.Length];
        for (var index = 0; index < first.Length; index++) result[index] = first[index];
        for (var index = 0; index < second.Length; index++) result[first.Length + index] = second[index];
        return result;
    }

    private static byte[] Tail(byte[] value, int count)
    {
        var result = new byte[count];
        for (var index = 0; index < count; index++) result[index] = value[value.Length - count + index];
        return result;
    }
}

internal sealed class EncodingDecoder : Decoder
{
    internal EncodingDecoder(Encoding encoding) => Initialize(encoding);

    public override int GetCharCount(byte[] bytes, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Encoding.ValidateRange(index, count, bytes.Length);
        return GetCharCount(new ReadOnlySpan<byte>(bytes, index, count), flush: false);
    }

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(chars);
        Encoding.ValidateRange(byteIndex, byteCount, bytes.Length);
        Encoding.ValidateDestination(charIndex, chars.Length);
        return GetChars(
            new ReadOnlySpan<byte>(bytes, byteIndex, byteCount),
            new Span<char>(chars, charIndex, chars.Length - charIndex),
            flush: false);
    }
}
