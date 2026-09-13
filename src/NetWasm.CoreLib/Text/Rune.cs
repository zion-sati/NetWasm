// Ported from dotnet/runtime System.Private.CoreLib; upstream commit 811225a482702af7ecc35d817966bc70b88a3a23.
// The retained implementation is the ordinal, culture-independent scalar-value closure.
// Licensed to the .NET Foundation under one or more agreements under the MIT license.

namespace System.Text;

/// <summary>Represents a Unicode scalar value.</summary>
public readonly partial struct Rune : IComparable, IComparable<Rune>, IEquatable<Rune>, IFormattable,
    IParsable<Rune>, ISpanFormattable, ISpanParsable<Rune>, IUtf8SpanFormattable, IUtf8SpanParsable<Rune>
{
    private readonly int _value;

    public Rune(char value)
    {
        if (IsSurrogate(value)) throw new ArgumentOutOfRangeException();
        _value = value;
    }

    public Rune(char highSurrogate, char lowSurrogate)
    {
        if (!IsHighSurrogate(highSurrogate) || !IsLowSurrogate(lowSurrogate))
            throw new ArgumentOutOfRangeException();
        _value = ((highSurrogate - 0xD800) << 10) + (lowSurrogate - 0xDC00) + 0x10000;
    }

    public Rune(int value)
    {
        if (!IsValid(value)) throw new ArgumentOutOfRangeException();
        _value = value;
    }

    public Rune(uint value)
    {
        if (!IsValid(value)) throw new ArgumentOutOfRangeException();
        _value = (int)value;
    }

    public int Value { get { return _value; } }
    public bool IsAscii { get { return _value <= 0x7F; } }
    public bool IsBmp { get { return _value <= 0xFFFF; } }
    public int Plane { get { return _value >> 16; } }
    public int Utf16SequenceLength { get { return _value <= 0xFFFF ? 1 : 2; } }
    public int Utf8SequenceLength { get { return _value <= 0x7F ? 1 : _value <= 0x7FF ? 2 : _value <= 0xFFFF ? 3 : 4; } }
    public static Rune ReplacementChar { get { return new Rune(0xFFFD); } }

    public static bool IsValid(int value) => (uint)value <= 0x10FFFF && !((uint)value >= 0xD800 && (uint)value <= 0xDFFF);
    public static bool IsValid(uint value) => value <= 0x10FFFF && !(value >= 0xD800 && value <= 0xDFFF);
    public static bool TryCreate(int value, out Rune result)
    {
        if (IsValid(value)) { result = new Rune(value); return true; }
        result = default; return false;
    }
    public static bool TryCreate(uint value, out Rune result)
    {
        if (IsValid(value)) { result = new Rune((int)value); return true; }
        result = default; return false;
    }
    public static bool TryCreate(char value, out Rune result)
    {
        if (!IsSurrogate(value)) { result = new Rune(value); return true; }
        result = default; return false;
    }
    public static bool TryCreate(char highSurrogate, char lowSurrogate, out Rune result)
    {
        if (IsHighSurrogate(highSurrogate) && IsLowSurrogate(lowSurrogate))
        { result = new Rune(highSurrogate, lowSurrogate); return true; }
        result = default; return false;
    }

    public static explicit operator Rune(char value) => new(value);
    public static explicit operator Rune(int value) => new(value);
    public static explicit operator Rune(uint value) => new(value);

    public int EncodeToUtf16(Span<char> destination)
    {
        if (!TryEncodeToUtf16(destination, out var charsWritten)) throw new ArgumentException();
        return charsWritten;
    }
    public bool TryEncodeToUtf16(Span<char> destination, out int charsWritten)
    {
        if (destination.Length < Utf16SequenceLength) { charsWritten = 0; return false; }
        if (_value <= 0xFFFF) destination[0] = (char)_value;
        else { var scalar = _value - 0x10000; destination[0] = (char)(0xD800 + (scalar >> 10)); destination[1] = (char)(0xDC00 + (scalar & 0x3FF)); }
        charsWritten = Utf16SequenceLength; return true;
    }
    public int EncodeToUtf8(Span<byte> destination)
    {
        if (!TryEncodeToUtf8(destination, out var bytesWritten)) throw new ArgumentException();
        return bytesWritten;
    }
    public bool TryEncodeToUtf8(Span<byte> destination, out int bytesWritten)
    {
        var count = Utf8SequenceLength;
        if (destination.Length < count) { bytesWritten = 0; return false; }
        if (count == 1) destination[0] = (byte)_value;
        else if (count == 2) { destination[0] = (byte)(0xC0 | (_value >> 6)); destination[1] = (byte)(0x80 | (_value & 0x3F)); }
        else if (count == 3) { destination[0] = (byte)(0xE0 | (_value >> 12)); destination[1] = (byte)(0x80 | ((_value >> 6) & 0x3F)); destination[2] = (byte)(0x80 | (_value & 0x3F)); }
        else { destination[0] = (byte)(0xF0 | (_value >> 18)); destination[1] = (byte)(0x80 | ((_value >> 12) & 0x3F)); destination[2] = (byte)(0x80 | ((_value >> 6) & 0x3F)); destination[3] = (byte)(0x80 | (_value & 0x3F)); }
        bytesWritten = count; return true;
    }

    public static System.Buffers.OperationStatus DecodeFromUtf16(ReadOnlySpan<char> source, out Rune result, out int charsConsumed)
    {
        if (!source.IsEmpty)
        {
            var first = source[0];
            if (TryCreate(first, out result))
            {
                charsConsumed = 1;
                return System.Buffers.OperationStatus.Done;
            }

            if (source.Length > 1)
            {
                if (TryCreate(first, source[1], out result))
                {
                    charsConsumed = 2;
                    return System.Buffers.OperationStatus.Done;
                }

                charsConsumed = 1;
                result = ReplacementChar;
                return System.Buffers.OperationStatus.InvalidData;
            }

            if (!IsHighSurrogate(first))
            {
                charsConsumed = 1;
                result = ReplacementChar;
                return System.Buffers.OperationStatus.InvalidData;
            }
        }

        charsConsumed = source.Length;
        result = ReplacementChar;
        return System.Buffers.OperationStatus.NeedMoreData;
    }

    public static System.Buffers.OperationStatus DecodeFromUtf8(ReadOnlySpan<byte> source, out Rune result, out int bytesConsumed)
    {
        if (source.IsEmpty)
        {
            result = ReplacementChar;
            bytesConsumed = 0;
            return System.Buffers.OperationStatus.NeedMoreData;
        }

        var first = source[0];
        if (first <= 0x7F)
        {
            result = new Rune(first);
            bytesConsumed = 1;
            return System.Buffers.OperationStatus.Done;
        }

        if (first < 0xC2 || first > 0xF4)
        {
            return InvalidData(1, out result, out bytesConsumed);
        }

        if (source.Length <= 1)
        {
            return NeedMoreData(1, out result, out bytesConsumed);
        }

        var second = source[1];
        if (!IsUtf8ContinuationByte(second))
        {
            return InvalidData(1, out result, out bytesConsumed);
        }

        if ((first == 0xE0 && second < 0xA0) ||
            (first == 0xED && second > 0x9F) ||
            (first == 0xF0 && second < 0x90) ||
            (first == 0xF4 && second > 0x8F))
        {
            return InvalidData(1, out result, out bytesConsumed);
        }

        if (first <= 0xDF)
        {
            result = new Rune(((first & 0x1F) << 6) | (second & 0x3F));
            bytesConsumed = 2;
            return System.Buffers.OperationStatus.Done;
        }

        if (source.Length <= 2)
        {
            return NeedMoreData(2, out result, out bytesConsumed);
        }

        var third = source[2];
        if (!IsUtf8ContinuationByte(third))
        {
            return InvalidData(2, out result, out bytesConsumed);
        }

        if (first <= 0xEF)
        {
            result = new Rune(((first & 0x0F) << 12) | ((second & 0x3F) << 6) | (third & 0x3F));
            bytesConsumed = 3;
            return System.Buffers.OperationStatus.Done;
        }

        if (source.Length <= 3)
        {
            return NeedMoreData(3, out result, out bytesConsumed);
        }

        var fourth = source[3];
        if (!IsUtf8ContinuationByte(fourth))
        {
            return InvalidData(3, out result, out bytesConsumed);
        }

        result = new Rune(
            ((first & 0x07) << 18) |
            ((second & 0x3F) << 12) |
            ((third & 0x3F) << 6) |
            (fourth & 0x3F));
        bytesConsumed = 4;
        return System.Buffers.OperationStatus.Done;
    }

    public static System.Buffers.OperationStatus DecodeLastFromUtf16(ReadOnlySpan<char> source, out Rune result, out int charsConsumed)
    {
        var index = source.Length - 1;
        if ((uint)index < (uint)source.Length)
        {
            var last = source[index];
            if (TryCreate(last, out result))
            {
                charsConsumed = 1;
                return System.Buffers.OperationStatus.Done;
            }

            if (IsLowSurrogate(last))
            {
                index--;
                if ((uint)index < (uint)source.Length && TryCreate(source[index], last, out result))
                {
                    charsConsumed = 2;
                    return System.Buffers.OperationStatus.Done;
                }

                charsConsumed = 1;
                result = ReplacementChar;
                return System.Buffers.OperationStatus.InvalidData;
            }
        }

        charsConsumed = source.IsEmpty ? 0 : 1;
        result = ReplacementChar;
        return System.Buffers.OperationStatus.NeedMoreData;
    }

    public static System.Buffers.OperationStatus DecodeLastFromUtf8(ReadOnlySpan<byte> source, out Rune result, out int bytesConsumed)
    {
        var index = source.Length - 1;
        if ((uint)index >= (uint)source.Length)
        {
            result = ReplacementChar;
            bytesConsumed = 0;
            return System.Buffers.OperationStatus.NeedMoreData;
        }

        var last = source[index];
        if (last <= 0x7F)
        {
            result = new Rune(last);
            bytesConsumed = 1;
            return System.Buffers.OperationStatus.Done;
        }

        if ((last & 0x40) != 0)
        {
            return DecodeFromUtf8(source.Slice(index), out result, out bytesConsumed);
        }

        for (var remaining = 3; remaining > 0; remaining--)
        {
            index--;
            if ((uint)index >= (uint)source.Length)
            {
                break;
            }

            var current = source[index];
            if (current <= 0x7F || current >= 0xC0)
            {
                var suffix = source.Slice(index);
                var status = DecodeFromUtf8(suffix, out var decoded, out var consumed);
                if (consumed == suffix.Length)
                {
                    result = decoded;
                    bytesConsumed = consumed;
                    return status;
                }

                break;
            }
        }

        result = ReplacementChar;
        bytesConsumed = 1;
        return System.Buffers.OperationStatus.InvalidData;
    }

    public static Rune GetRuneAt(string input, int index)
    {
        if (!TryGetRuneAt(input, index, out var result)) throw new ArgumentException();
        return result;
    }
    public static bool TryGetRuneAt(string input, int index, out Rune value)
    {
        if (input is null) throw new ArgumentNullException();
        if ((uint)index >= (uint)input.Length) throw new ArgumentOutOfRangeException();
        var status = DecodeFromUtf16(new ReadOnlySpan<char>(input.ToCharArray(), index, input.Length - index), out value, out _);
        return status == System.Buffers.OperationStatus.Done;
    }

    public int CompareTo(Rune other) => _value.CompareTo(other._value);
    int IComparable.CompareTo(object? obj) => obj is Rune other ? CompareTo(other) : throw new ArgumentException();
    public bool Equals(Rune other) => _value == other._value;
    public override bool Equals(object? obj) => obj is Rune other && Equals(other);
    public override int GetHashCode() => _value;
    public static bool operator ==(Rune left, Rune right) => left._value == right._value;
    public static bool operator !=(Rune left, Rune right) => left._value != right._value;
    public static bool operator <(Rune left, Rune right) => left._value < right._value;
    public static bool operator <=(Rune left, Rune right) => left._value <= right._value;
    public static bool operator >(Rune left, Rune right) => left._value > right._value;
    public static bool operator >=(Rune left, Rune right) => left._value >= right._value;
    public override string ToString()
    {
        if (_value <= 0xFFFF) return new string((char)_value, 1);
        var scalar = _value - 0x10000;
        return String.Concat(new string((char)(0xD800 + (scalar >> 10)), 1), new string((char)(0xDC00 + (scalar & 0x3FF)), 1));
    }

    private static bool IsHighSurrogate(char value) => value >= 0xD800 && value <= 0xDBFF;
    private static bool IsLowSurrogate(char value) => value >= 0xDC00 && value <= 0xDFFF;
    private static bool IsSurrogate(char value) => value >= 0xD800 && value <= 0xDFFF;
    private static bool IsUtf8ContinuationByte(byte value) => (value & 0xC0) == 0x80;

    private static System.Buffers.OperationStatus NeedMoreData(
        int consumed,
        out Rune result,
        out int bytesConsumed)
    {
        result = ReplacementChar;
        bytesConsumed = consumed;
        return System.Buffers.OperationStatus.NeedMoreData;
    }

    private static System.Buffers.OperationStatus InvalidData(
        int consumed,
        out Rune result,
        out int bytesConsumed)
    {
        result = ReplacementChar;
        bytesConsumed = consumed;
        return System.Buffers.OperationStatus.InvalidData;
    }
}
