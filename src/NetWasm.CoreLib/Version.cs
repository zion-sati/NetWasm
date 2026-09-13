// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Deterministic port of dotnet/runtime System.Private.CoreLib Version.cs.
// Upstream commit: 811225a482702af7ecc35d817966bc70b88a3a23.
// Culture, reflection metadata, and host dependencies are intentionally absent.

using System.Diagnostics.CodeAnalysis;

namespace System;

// A Version object contains four hierarchical numeric components. Build and
// revision may be unspecified, represented internally by -1.
public sealed class Version : ICloneable,
    IComparable,
    IComparable<Version?>,
    IEquatable<Version?>,
    IFormattable,
    ISpanFormattable,
    IUtf8SpanFormattable,
    IUtf8SpanParsable<Version>
{
    private readonly int _Major;
    private readonly int _Minor;
    private readonly int _Build;
    private readonly int _Revision;

    public Version(int major, int minor, int build, int revision)
    {
        ValidateComponent(major, nameof(major));
        ValidateComponent(minor, nameof(minor));
        ValidateComponent(build, nameof(build));
        ValidateComponent(revision, nameof(revision));
        _Major = major;
        _Minor = minor;
        _Build = build;
        _Revision = revision;
    }

    public Version(int major, int minor, int build)
    {
        ValidateComponent(major, nameof(major));
        ValidateComponent(minor, nameof(minor));
        ValidateComponent(build, nameof(build));
        _Major = major;
        _Minor = minor;
        _Build = build;
        _Revision = -1;
    }

    public Version(int major, int minor)
    {
        ValidateComponent(major, nameof(major));
        ValidateComponent(minor, nameof(minor));
        _Major = major;
        _Minor = minor;
        _Build = -1;
        _Revision = -1;
    }

    public Version(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var parsed = Parse(version);
        _Major = parsed._Major;
        _Minor = parsed._Minor;
        _Build = parsed._Build;
        _Revision = parsed._Revision;
    }

    public Version()
    {
        _Build = -1;
        _Revision = -1;
    }

    private Version(Version version)
    {
        _Major = version._Major;
        _Minor = version._Minor;
        _Build = version._Build;
        _Revision = version._Revision;
    }

    public object Clone() => new Version(this);

    public int Major
    {
        get => _Major;
    }

    public int Minor
    {
        get => _Minor;
    }

    public int Build
    {
        get => _Build;
    }

    public int Revision
    {
        get => _Revision;
    }

    public short MajorRevision
    {
        get => (short)(_Revision >> 16);
    }

    public short MinorRevision
    {
        get => (short)(_Revision & 0xFFFF);
    }

    public int CompareTo(object? value)
    {
        if (value is null) return 1;
        if (value is not Version other) throw new ArgumentException("Object must be of type Version.", nameof(value));
        return CompareTo(other);
    }

    public int CompareTo(Version? value)
    {
        if (ReferenceEquals(value, this)) return 0;
        if (value is null) return 1;
        if (_Major != value._Major) return _Major > value._Major ? 1 : -1;
        if (_Minor != value._Minor) return _Minor > value._Minor ? 1 : -1;
        if (_Build != value._Build) return _Build > value._Build ? 1 : -1;
        if (_Revision != value._Revision) return _Revision > value._Revision ? 1 : -1;
        return 0;
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as Version);

    public bool Equals([NotNullWhen(true)] Version? obj) =>
        ReferenceEquals(obj, this) || obj is not null &&
        _Major == obj._Major && _Minor == obj._Minor && _Build == obj._Build && _Revision == obj._Revision;

    public override int GetHashCode()
    {
        var accumulator = 0;
        accumulator |= (_Major & 0x0000000F) << 28;
        accumulator |= (_Minor & 0x000000FF) << 20;
        accumulator |= (_Build & 0x000000FF) << 12;
        accumulator |= _Revision & 0x00000FFF;
        return accumulator;
    }

    public override string ToString() => ToString(DefaultFormatFieldCount);

    public string ToString(int fieldCount)
    {
        var chars = new char[43];
        if (!TryFormat(chars, fieldCount, out var written)) throw new ArgumentException(nameof(fieldCount));
        return string.Create(chars, 0, written);
    }

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

    public bool TryFormat(Span<char> destination, out int charsWritten) => TryFormat(destination, DefaultFormatFieldCount, out charsWritten);

    public bool TryFormat(Span<char> destination, int fieldCount, out int charsWritten)
    {
        ValidateFieldCount(fieldCount);
        var values = new[] { _Major, _Minor, _Build, _Revision };
        var written = 0;
        for (var i = 0; i < fieldCount; i++)
        {
            if (i != 0)
            {
                if (written == destination.Length)
                {
                    charsWritten = 0;
                    return false;
                }
                destination[written++] = '.';
            }
            if (!TryWriteDecimal(destination.Slice(written), values[i], out var componentWritten))
            {
                charsWritten = 0;
                return false;
            }
            written += componentWritten;
        }
        charsWritten = written;
        return true;
    }

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten) => TryFormat(utf8Destination, DefaultFormatFieldCount, out bytesWritten);

    public bool TryFormat(Span<byte> utf8Destination, int fieldCount, out int bytesWritten)
    {
        var chars = new char[43];
        if (!TryFormat(chars, fieldCount, out var written))
        {
            bytesWritten = 0;
            return false;
        }
        if (utf8Destination.Length < written)
        {
            bytesWritten = 0;
            return false;
        }
        for (var i = 0; i < written; i++) utf8Destination[i] = (byte)chars[i];
        bytesWritten = written;
        return true;
    }

    bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => TryFormat(destination, out charsWritten);
    bool IUtf8SpanFormattable.TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => TryFormat(destination, out bytesWritten);

    private int DefaultFormatFieldCount => _Build == -1 ? 2 : _Revision == -1 ? 3 : 4;

    public static Version Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Parse(input.AsSpan());
    }

    public static Version Parse(ReadOnlySpan<char> input) => ParseVersion(input, true)!;

    public static Version Parse(ReadOnlySpan<byte> utf8Text)
    {
        var chars = DecodeAscii(utf8Text);
        return Parse(chars);
    }

    public static bool TryParse([NotNullWhen(true)] string? input, [NotNullWhen(true)] out Version? result)
    {
        if (input is null)
        {
            result = null;
            return false;
        }
        result = ParseVersion(input.AsSpan(), false);
        return result is not null;
    }

    public static bool TryParse(ReadOnlySpan<char> input, [NotNullWhen(true)] out Version? result)
    {
        result = ParseVersion(input, false);
        return result is not null;
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out Version? result)
    {
        try
        {
            result = ParseVersion(DecodeAscii(utf8Text), false);
            return result is not null;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    public static Version Parse(string s, IFormatProvider? provider) => Parse(s);
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Version? result) => TryParse(s, out result);
    public static Version Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [NotNullWhen(true)] out Version? result) => TryParse(s, out result);

    static Version IUtf8SpanParsable<Version>.Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text);
    static bool IUtf8SpanParsable<Version>.TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, [NotNullWhen(true)] out Version? result) => TryParse(utf8Text, out result);

    private static Version? ParseVersion(ReadOnlySpan<char> input, bool throwOnFailure)
    {
        var parts = new int[4];
        var count = 0;
        var start = 0;
        for (var index = 0; index <= input.Length; index++)
        {
            if (index != input.Length && input[index] != '.') continue;
            if (count == 4)
            {
                if (throwOnFailure) throw new ArgumentException("Version string must contain between two and four components.", nameof(input));
                return null;
            }
            var component = input.Slice(start, index - start);
            if (!TryParseComponent(component, out parts[count], out var failure))
            {
                if (throwOnFailure) ThrowParseFailure(failure, nameof(input), input);
                return null;
            }
            count++;
            start = index + 1;
        }

        if (count < 2)
        {
            if (throwOnFailure) throw new ArgumentException("Version string must contain at least two components.", nameof(input));
            return null;
        }
        return count switch
        {
            2 => new Version(parts[0], parts[1]),
            3 => new Version(parts[0], parts[1], parts[2]),
            _ => new Version(parts[0], parts[1], parts[2], parts[3])
        };
    }

    private static bool TryParseComponent(ReadOnlySpan<char> component, out int value, out ParseFailure failure)
    {
        var start = 0;
        var end = component.Length;
        while (start < end && char.IsWhiteSpace(component[start])) start++;
        while (end > start && char.IsWhiteSpace(component[end - 1])) end--;
        if (start == end)
        {
            value = 0;
            failure = ParseFailure.Format;
            return false;
        }
        var negative = component[start] == '-';
        if (negative || component[start] == '+') start++;
        if (start == end)
        {
            value = 0;
            failure = ParseFailure.Format;
            return false;
        }
        uint parsed = 0;
        for (var index = start; index < end; index++)
        {
            var digit = component[index] - '0';
            if (digit is < 0 or > 9)
            {
                value = 0;
                failure = ParseFailure.Format;
                return false;
            }
            if (parsed > (uint.MaxValue - (uint)digit) / 10)
            {
                value = int.MaxValue;
                failure = ParseFailure.Overflow;
                return false;
            }
            parsed = parsed * 10U + (uint)digit;
        }
        if (negative)
        {
            value = parsed > 0x8000_0000U ? int.MinValue : -(int)parsed;
            failure = ParseFailure.Negative;
            return false;
        }
        if (parsed > int.MaxValue)
        {
            value = int.MaxValue;
            failure = ParseFailure.Overflow;
            return false;
        }
        value = (int)parsed;
        failure = ParseFailure.None;
        return true;
    }

    private static void ThrowParseFailure(ParseFailure failure, string parameterName, ReadOnlySpan<char> input)
    {
        if (failure == ParseFailure.Negative) throw new ArgumentOutOfRangeException(parameterName, "Version components must be non-negative.");
        if (failure == ParseFailure.Overflow) throw new OverflowException();
        throw new FormatException($"Version string '{input.ToString()}' is not valid.");
    }

    private static void ValidateComponent(int value, string parameterName)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(parameterName);
    }

    private void ValidateFieldCount(int fieldCount)
    {
        if ((uint)fieldCount > 4) throw new ArgumentException("Field count must be between zero and four.", nameof(fieldCount));
        if (fieldCount >= 3 && _Build == -1) throw new ArgumentException("This version has no build component.", nameof(fieldCount));
        if (fieldCount == 4 && _Revision == -1) throw new ArgumentException("This version has no revision component.", nameof(fieldCount));
    }

    private static bool TryWriteDecimal(Span<char> destination, int value, out int written)
    {
        Span<char> digits = stackalloc char[10];
        var count = 0;
        do
        {
            digits[count++] = (char)('0' + value % 10);
            value /= 10;
        }
        while (value != 0);
        if (destination.Length < count)
        {
            written = 0;
            return false;
        }
        for (var i = 0; i < count; i++) destination[i] = digits[count - i - 1];
        written = count;
        return true;
    }

    private static char[] DecodeAscii(ReadOnlySpan<byte> input)
    {
        var chars = new char[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] > 0x7F) throw new FormatException("Version input is not ASCII.");
            chars[i] = (char)input[i];
        }
        return chars;
    }

    private enum ParseFailure : byte
    {
        None,
        Format,
        Negative,
        Overflow
    }

    public static bool operator ==(Version? v1, Version? v2) => v2 is null ? v1 is null : ReferenceEquals(v2, v1) || v2.Equals(v1);
    public static bool operator !=(Version? v1, Version? v2) => !(v1 == v2);
    public static bool operator <(Version? v1, Version? v2) => v1 is null ? v2 is not null : v1.CompareTo(v2) < 0;
    public static bool operator <=(Version? v1, Version? v2) => v1 is null || v1.CompareTo(v2) <= 0;
    public static bool operator >(Version? v1, Version? v2) => v2 < v1;
    public static bool operator >=(Version? v1, Version? v2) => v2 <= v1;
}
