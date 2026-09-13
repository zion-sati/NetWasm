// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under the MIT license.
namespace System.Text;

public abstract class Encoder
{
    internal Encoding? _encoding;
    private EncoderFallback? _fallback;
    private EncoderFallbackBuffer? _fallbackBuffer;
    private char _pendingHighSurrogate;

    protected Encoder() { }

    public EncoderFallback? Fallback
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

    public EncoderFallbackBuffer FallbackBuffer { get => _fallbackBuffer ??= (_fallback ?? EncoderFallback.ReplacementFallback).CreateFallbackBuffer(); }

    internal void Initialize(Encoding encoding) => _encoding = encoding;

    public abstract int GetByteCount(char[] chars, int index, int count, bool flush);

    public unsafe virtual int GetByteCount(char* chars, int count, bool flush) => GetByteCount(new ReadOnlySpan<char>(chars, count), flush);

    public virtual int GetByteCount(ReadOnlySpan<char> chars, bool flush) => GetByteCountCore(chars, flush);

    public abstract int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex, bool flush);

    public unsafe virtual int GetBytes(char* chars, int charCount, byte* bytes, int byteCount, bool flush) => GetBytes(new ReadOnlySpan<char>(chars, charCount), new Span<byte>(bytes, byteCount), flush);

    public unsafe virtual void Convert(char* chars, int charCount, byte* bytes, int byteCount, bool flush, out int charsUsed, out int bytesUsed, out bool completed) =>
        Convert(new ReadOnlySpan<char>(chars, charCount), new Span<byte>(bytes, byteCount), flush, out charsUsed, out bytesUsed, out completed);

    public virtual int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush)
    {
        var required = CountWithoutConsuming(chars, flush);
        if (required > bytes.Length) throw new ArgumentException("The destination buffer is too small.", nameof(bytes));
        return GetBytesCore(chars, bytes, flush);
    }

    public virtual void Convert(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex, int byteCount, bool flush, out int charsUsed, out int bytesUsed, out bool completed)
    {
        ArgumentNullException.ThrowIfNull(chars);
        ArgumentNullException.ThrowIfNull(bytes);
        Encoding.ValidateRange(charIndex, charCount, chars.Length);
        Encoding.ValidateDestination(byteIndex, bytes.Length);
        if (byteCount < 0 || byteCount > bytes.Length - byteIndex) throw new ArgumentOutOfRangeException(nameof(byteCount));

        var source = new ReadOnlySpan<char>(chars, charIndex, charCount);
        var pending = _pendingHighSurrogate;
        var combined = pending == '\0' ? source.ToArray() : Prepend(pending, source);
        var prefixLength = pending == '\0' ? 0 : 1;
        var held = !flush && combined.Length != 0 && IsHigh(combined[combined.Length - 1]);
        var encodableLength = held ? combined.Length - 1 : combined.Length;
        var take = encodableLength;
        while (take > 0 && GetEffectiveEncoding().GetByteCount(new ReadOnlySpan<char>(combined, 0, take)) > byteCount) take--;
        if (take == 0 && encodableLength != 0 && GetEffectiveEncoding().GetByteCount(ReadOnlySpan<char>.Empty) > byteCount) throw new ArgumentException("The destination buffer is too small.");

        var output = new Span<byte>(bytes, byteIndex, byteCount);
        bytesUsed = take == 0 ? 0 : GetEffectiveEncoding().GetBytes(new ReadOnlySpan<char>(combined, 0, take), output);
        charsUsed = take > prefixLength ? take - prefixLength : 0;
        if (held && take == encodableLength)
        {
            _pendingHighSurrogate = combined[combined.Length - 1];
            if (combined.Length > prefixLength) charsUsed++;
        }
        else
        {
            _pendingHighSurrogate = '\0';
        }
        completed = charsUsed == charCount && _pendingHighSurrogate == '\0' && (_fallbackBuffer is null || _fallbackBuffer.Remaining == 0);
    }

    public virtual void Convert(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush, out int charsUsed, out int bytesUsed, out bool completed)
    {
        var input = chars.ToArray();
        var output = new byte[bytes.Length];
        Convert(input, 0, input.Length, output, 0, output.Length, flush, out charsUsed, out bytesUsed, out completed);
        for (var index = 0; index < bytesUsed; index++) bytes[index] = output[index];
    }

    public virtual void Reset()
    {
        _fallbackBuffer?.Reset();
        _pendingHighSurrogate = '\0';
    }

    private int GetByteCountCore(ReadOnlySpan<char> chars, bool flush)
    {
        var pending = _pendingHighSurrogate;
        var combined = pending == '\0' ? chars.ToArray() : Prepend(pending, chars);
        var hold = !flush && combined.Length != 0 && IsHigh(combined[combined.Length - 1]);
        var encodeLength = hold ? combined.Length - 1 : combined.Length;
        var result = GetEffectiveEncoding().GetByteCount(new ReadOnlySpan<char>(combined, 0, encodeLength));
        _pendingHighSurrogate = hold ? combined[combined.Length - 1] : '\0';
        return result;
    }

    private int GetBytesCore(ReadOnlySpan<char> chars, Span<byte> bytes, bool flush)
    {
        var pending = _pendingHighSurrogate;
        var combined = pending == '\0' ? chars.ToArray() : Prepend(pending, chars);
        var hold = !flush && combined.Length != 0 && IsHigh(combined[combined.Length - 1]);
        var encodeLength = hold ? combined.Length - 1 : combined.Length;
        var result = GetEffectiveEncoding().GetBytes(new ReadOnlySpan<char>(combined, 0, encodeLength), bytes);
        _pendingHighSurrogate = hold ? combined[combined.Length - 1] : '\0';
        return result;
    }

    private int CountWithoutConsuming(ReadOnlySpan<char> chars, bool flush)
    {
        var saved = _pendingHighSurrogate;
        try { return GetByteCountCore(chars, flush); }
        finally { _pendingHighSurrogate = saved; }
    }

    private Encoding GetEffectiveEncoding()
    {
        if (_encoding is null) throw new InvalidOperationException("The encoder is not associated with an encoding.");
        if (_fallback is null || ReferenceEquals(_fallback, _encoding.EncoderFallback)) return _encoding;
        var clone = (Encoding)_encoding.Clone();
        clone.EncoderFallback = _fallback;
        return clone;
    }

    private static char[] Prepend(char value, ReadOnlySpan<char> chars)
    {
        var result = new char[chars.Length + 1];
        result[0] = value;
        for (var index = 0; index < chars.Length; index++) result[index + 1] = chars[index];
        return result;
    }

    private static bool IsHigh(char value) => value >= 0xD800 && value <= 0xDBFF;
}

internal sealed class EncodingEncoder : Encoder
{
    internal EncodingEncoder(Encoding encoding) => Initialize(encoding);

    public override int GetByteCount(char[] chars, int index, int count, bool flush)
    {
        ArgumentNullException.ThrowIfNull(chars);
        Encoding.ValidateRange(index, count, chars.Length);
        return GetByteCount(new ReadOnlySpan<char>(chars, index, count), flush);
    }

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex, bool flush)
    {
        ArgumentNullException.ThrowIfNull(chars);
        ArgumentNullException.ThrowIfNull(bytes);
        Encoding.ValidateRange(charIndex, charCount, chars.Length);
        Encoding.ValidateDestination(byteIndex, bytes.Length);
        return GetBytes(
            new ReadOnlySpan<char>(chars, charIndex, charCount),
            new Span<byte>(bytes, byteIndex, bytes.Length - byteIndex),
            flush);
    }
}
