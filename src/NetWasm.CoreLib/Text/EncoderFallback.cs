// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under the MIT license.
namespace System.Text;

public abstract class EncoderFallback
{
    private static readonly EncoderFallback s_replacement = new EncoderReplacementFallback();
    private static readonly EncoderFallback s_exception = new EncoderExceptionFallback();
    public static EncoderFallback ReplacementFallback { get => s_replacement; }
    public static EncoderFallback ExceptionFallback { get => s_exception; }
    public abstract EncoderFallbackBuffer CreateFallbackBuffer();
    public abstract int MaxCharCount { get; }
}

public abstract class EncoderFallbackBuffer
{
    public abstract int Remaining { get; }
    public abstract bool Fallback(char charUnknown, int index);
    public abstract bool Fallback(char charUnknownHigh, char charUnknownLow, int index);
    public abstract char GetNextChar();
    public abstract bool MovePrevious();
    public virtual void Reset() { }
}

public sealed class EncoderReplacementFallback : EncoderFallback
{
    private readonly string _replacement;
    public EncoderReplacementFallback() : this("?") { }
    public EncoderReplacementFallback(string replacement)
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
    public override EncoderFallbackBuffer CreateFallbackBuffer() => new EncoderReplacementFallbackBuffer(this);
    public override bool Equals(object? value) => value is EncoderReplacementFallback other && _replacement == other._replacement;
    public override int GetHashCode() => _replacement.GetHashCode();
}

public sealed class EncoderReplacementFallbackBuffer : EncoderFallbackBuffer
{
    private readonly string _replacement;
    private int _index;
    public EncoderReplacementFallbackBuffer(EncoderReplacementFallback fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        _replacement = fallback.DefaultString;
        _index = _replacement.Length;
    }
    public override int Remaining { get => _replacement.Length - _index; }
    public override bool Fallback(char charUnknown, int index)
    {
        if (Remaining != 0) throw new ArgumentException();
        _index = 0;
        return _replacement.Length != 0;
    }
    public override bool Fallback(char charUnknownHigh, char charUnknownLow, int index)
    {
        if (charUnknownHigh < 0xD800 || charUnknownHigh > 0xDBFF) throw new ArgumentOutOfRangeException(nameof(charUnknownHigh));
        if (charUnknownLow < 0xDC00 || charUnknownLow > 0xDFFF) throw new ArgumentOutOfRangeException(nameof(charUnknownLow));
        return Fallback(charUnknownHigh, index);
    }
    public override char GetNextChar() => _index < _replacement.Length ? _replacement[_index++] : '\0';
    public override bool MovePrevious() { if (_index == 0) return false; _index--; return true; }
    public override void Reset() => _index = _replacement.Length;
}

public sealed class EncoderExceptionFallback : EncoderFallback
{
    public EncoderExceptionFallback() { }
    public override int MaxCharCount { get => 0; }
    public override EncoderFallbackBuffer CreateFallbackBuffer() => new EncoderExceptionFallbackBuffer();
    public override bool Equals(object? value) => value is EncoderExceptionFallback;
    public override int GetHashCode() => 654;
}

public sealed class EncoderExceptionFallbackBuffer : EncoderFallbackBuffer
{
    public EncoderExceptionFallbackBuffer() { }
    public override int Remaining { get => 0; }
    public override bool Fallback(char charUnknown, int index) => throw new EncoderFallbackException("Unable to encode character.", charUnknown, index);
    public override bool Fallback(char charUnknownHigh, char charUnknownLow, int index)
    {
        if (charUnknownHigh < 0xD800 || charUnknownHigh > 0xDBFF) throw new ArgumentOutOfRangeException(nameof(charUnknownHigh));
        if (charUnknownLow < 0xDC00 || charUnknownLow > 0xDFFF) throw new ArgumentOutOfRangeException(nameof(charUnknownLow));
        throw new EncoderFallbackException("Unable to encode surrogate.", charUnknownHigh, charUnknownLow, index);
    }
    public override char GetNextChar() => '\0';
    public override bool MovePrevious() => false;
}

public sealed class EncoderFallbackException : ArgumentException
{
    private readonly char _unknown;
    private readonly char _high;
    private readonly char _low;
    public EncoderFallbackException() { }
    public EncoderFallbackException(string? message) : base(message) { }
    public EncoderFallbackException(string? message, Exception? innerException) : base(message, innerException) { }
    internal EncoderFallbackException(string? message, char unknown, int index) : base(message) { _unknown = unknown; Index = index; }
    internal EncoderFallbackException(string? message, char high, char low, int index) : base(message) { _high = high; _low = low; Index = index; }
    public char CharUnknown { get => _unknown; }
    public char CharUnknownHigh { get => _high; }
    public char CharUnknownLow { get => _low; }
    public int Index { get; }
    public bool IsUnknownSurrogate() => _unknown >= 0xD800 && _unknown <= 0xDFFF || _high != '\0';
}
