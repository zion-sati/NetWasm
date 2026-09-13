// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under the MIT license.
namespace System.Text;

public abstract class DecoderFallback
{
    private static readonly DecoderFallback s_replacement = new DecoderReplacementFallback();
    private static readonly DecoderFallback s_exception = new DecoderExceptionFallback();
    public static DecoderFallback ReplacementFallback { get => s_replacement; }
    public static DecoderFallback ExceptionFallback { get => s_exception; }
    public abstract DecoderFallbackBuffer CreateFallbackBuffer();
    public abstract int MaxCharCount { get; }
}

public abstract class DecoderFallbackBuffer
{
    public abstract int Remaining { get; }
    public abstract bool Fallback(byte[] bytesUnknown, int index);
    public abstract char GetNextChar();
    public abstract bool MovePrevious();
    public virtual void Reset() { }
}

public sealed class DecoderReplacementFallback : DecoderFallback
{
    private readonly string _replacement;
    public DecoderReplacementFallback() : this("\uFFFD") { }
    public DecoderReplacementFallback(string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        for (var index = 0; index < replacement.Length; index++)
        {
            var value = replacement[index];
            if (value >= 0xD800 && value <= 0xDBFF)
            {
                if (index + 1 >= replacement.Length || replacement[index + 1] < 0xDC00 || replacement[index + 1] > 0xDFFF) throw new ArgumentException();
                index++;
            }
            else if (value >= 0xDC00 && value <= 0xDFFF) throw new ArgumentException();
        }
        _replacement = replacement;
    }
    public string DefaultString { get => _replacement; }
    public override int MaxCharCount { get => _replacement.Length; }
    public override DecoderFallbackBuffer CreateFallbackBuffer() => new DecoderReplacementFallbackBuffer(this);
    public override bool Equals(object? value) => value is DecoderReplacementFallback other && _replacement == other._replacement;
    public override int GetHashCode() => _replacement.GetHashCode();
}

public sealed class DecoderReplacementFallbackBuffer : DecoderFallbackBuffer
{
    private readonly string _replacement;
    private int _index;
    public DecoderReplacementFallbackBuffer(DecoderReplacementFallback fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        _replacement = fallback.DefaultString;
        _index = _replacement.Length;
    }
    public override int Remaining { get => _replacement.Length - _index; }
    public override bool Fallback(byte[] bytesUnknown, int index)
    {
        if (Remaining != 0) throw new ArgumentException();
        _index = 0;
        return _replacement.Length != 0;
    }
    public override char GetNextChar() => _index < _replacement.Length ? _replacement[_index++] : '\0';
    public override bool MovePrevious() { if (_index == 0) return false; _index--; return true; }
    public override void Reset() => _index = _replacement.Length;
}

public sealed class DecoderExceptionFallback : DecoderFallback
{
    public DecoderExceptionFallback() { }
    public override int MaxCharCount { get => 0; }
    public override DecoderFallbackBuffer CreateFallbackBuffer() => new DecoderExceptionFallbackBuffer();
    public override bool Equals(object? value) => value is DecoderExceptionFallback;
    public override int GetHashCode() => 879;
}

public sealed class DecoderExceptionFallbackBuffer : DecoderFallbackBuffer
{
    public DecoderExceptionFallbackBuffer() { }
    public override int Remaining { get => 0; }
    public override bool Fallback(byte[] bytesUnknown, int index) => throw new DecoderFallbackException("Unable to decode bytes.", bytesUnknown, index);
    public override char GetNextChar() => '\0';
    public override bool MovePrevious() => false;
}

public sealed class DecoderFallbackException : ArgumentException
{
    private readonly byte[]? _bytes;
    public DecoderFallbackException() { }
    public DecoderFallbackException(string? message) : base(message) { }
    public DecoderFallbackException(string? message, byte[]? bytesUnknown, int index) : base(message)
    {
        _bytes = bytesUnknown;
        Index = index;
    }
    public DecoderFallbackException(string? message, Exception? innerException) : base(message, innerException) { }
    public byte[]? BytesUnknown { get => _bytes; }
    public int Index { get; }
}
