// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Deterministic port of dotnet/runtime System.Private.CoreLib Version.cs.
// Upstream commit: 474ed963699fc29809015392738a15231aaae2de.
// Culture, reflection metadata, and host dependencies are intentionally absent.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

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
        ValidateFieldCount(fieldCount);
        if (fieldCount == 0)
        {
            return string.Empty;
        }

        var length = fieldCount - 1;
        for (var index = 0; index < fieldCount; index++)
        {
            length += DecimalLength(GetComponent(index));
        }

        var result = new string('\0', length);
        if (!TryFormat(new Span<char>(ref result.GetRawStringData(), length), fieldCount, out _))
        {
            throw new ArgumentException(nameof(fieldCount));
        }
        return result;
    }

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => ToString();

    public bool TryFormat(Span<char> destination, out int charsWritten) => TryFormat(destination, DefaultFormatFieldCount, out charsWritten);

    public bool TryFormat(Span<char> destination, int fieldCount, out int charsWritten) =>
        TryFormatCore(MemoryMarshal.Cast<char, Utf16Char>(destination), fieldCount, out charsWritten);

    private bool TryFormatCore<TChar>(Span<TChar> destination, int fieldCount, out int charsWritten)
        where TChar : unmanaged, IUtfChar<TChar>
    {
        ValidateFieldCount(fieldCount);
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
                destination[written++] = TChar.CastFrom('.');
            }
            if (!TryWriteDecimal(destination.Slice(written), GetComponent(i), out var componentWritten))
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

    public bool TryFormat(Span<byte> utf8Destination, int fieldCount, out int bytesWritten) =>
        TryFormatCore(MemoryMarshal.Cast<byte, Utf8Char>(utf8Destination), fieldCount, out bytesWritten);

    bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => TryFormat(destination, out charsWritten);
    bool IUtf8SpanFormattable.TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => TryFormat(destination, out bytesWritten);

    private int DefaultFormatFieldCount => _Build == -1 ? 2 : _Revision == -1 ? 3 : 4;

    public static Version Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Parse(input.AsSpan());
    }

    public static Version Parse(ReadOnlySpan<char> input) =>
        ParseVersion(MemoryMarshal.Cast<char, Utf16Char>(input), true)!;

    public static Version Parse(ReadOnlySpan<byte> utf8Text) =>
        ParseVersion(MemoryMarshal.Cast<byte, Utf8Char>(utf8Text), true)!;

    public static bool TryParse([NotNullWhen(true)] string? input, [NotNullWhen(true)] out Version? result)
    {
        if (input is null)
        {
            result = null;
            return false;
        }
        result = ParseVersion(MemoryMarshal.Cast<char, Utf16Char>(input.AsSpan()), false);
        return result is not null;
    }

    public static bool TryParse(ReadOnlySpan<char> input, [NotNullWhen(true)] out Version? result)
    {
        result = ParseVersion(MemoryMarshal.Cast<char, Utf16Char>(input), false);
        return result is not null;
    }

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, [NotNullWhen(true)] out Version? result)
    {
        result = ParseVersion(MemoryMarshal.Cast<byte, Utf8Char>(utf8Text), false);
        return result is not null;
    }

    public static Version Parse(string s, IFormatProvider? provider) => Parse(s);
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [NotNullWhen(true)] out Version? result) => TryParse(s, out result);
    public static Version Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [NotNullWhen(true)] out Version? result) => TryParse(s, out result);

    static Version IUtf8SpanParsable<Version>.Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => Parse(utf8Text);
    static bool IUtf8SpanParsable<Version>.TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, [NotNullWhen(true)] out Version? result) => TryParse(utf8Text, out result);

    private static Version? ParseVersion<TChar>(ReadOnlySpan<TChar> input, bool throwOnFailure)
        where TChar : unmanaged, IUtfChar<TChar>
    {
        var separator = TChar.CastFrom('.');
        var majorEnd = input.IndexOf(separator);
        if (majorEnd < 0)
        {
            if (throwOnFailure) throw new ArgumentException("Version string must contain at least two components.", nameof(input));
            return null;
        }

        var buildEnd = -1;
        var minorEnd = input.Slice(majorEnd + 1).IndexOf(separator);
        if (minorEnd >= 0)
        {
            minorEnd += majorEnd + 1;
            buildEnd = input.Slice(minorEnd + 1).IndexOf(separator);
            if (buildEnd >= 0)
            {
                buildEnd += minorEnd + 1;
                if (input.Slice(buildEnd + 1).Contains(separator))
                {
                    if (throwOnFailure) throw new ArgumentException("Version string must contain between two and four components.", nameof(input));
                    return null;
                }
            }
        }

        if (!TryParseComponent(input.Slice(0, majorEnd), out var major, out var failure))
        {
            if (throwOnFailure) ThrowParseFailure(failure, nameof(input), input);
            return null;
        }

        if (minorEnd < 0)
        {
            return TryParseComponent(input.Slice(majorEnd + 1), out var minor, out failure)
                ? new Version(major, minor)
                : ParseFailureResult(failure, nameof(input), input, throwOnFailure);
        }

        if (!TryParseComponent(input.Slice(majorEnd + 1, minorEnd - majorEnd - 1), out var parsedMinor, out failure))
        {
            return ParseFailureResult(failure, nameof(input), input, throwOnFailure);
        }

        if (buildEnd < 0)
        {
            return TryParseComponent(input.Slice(minorEnd + 1), out var build, out failure)
                ? new Version(major, parsedMinor, build)
                : ParseFailureResult(failure, "build", input, throwOnFailure);
        }

        if (!TryParseComponent(input.Slice(minorEnd + 1, buildEnd - minorEnd - 1), out var parsedBuild, out failure))
        {
            return ParseFailureResult(failure, "build", input, throwOnFailure);
        }

        return TryParseComponent(input.Slice(buildEnd + 1), out var revision, out failure)
            ? new Version(major, parsedMinor, parsedBuild, revision)
            : ParseFailureResult(failure, "revision", input, throwOnFailure);
    }

    private static Version? ParseFailureResult<TChar>(ParseFailure failure, string parameterName, ReadOnlySpan<TChar> input, bool throwOnFailure)
        where TChar : unmanaged, IUtfChar<TChar>
    {
        if (throwOnFailure)
        {
            ThrowParseFailure(failure, parameterName, input);
        }
        return null;
    }

    private static bool TryParseComponent<TChar>(ReadOnlySpan<TChar> component, out int value, out ParseFailure failure)
        where TChar : unmanaged, IUtfChar<TChar>
    {
        var start = 0;
        var end = component.Length;
        while (start < end && IsWhiteSpace(component[start])) start++;
        while (end > start && IsWhiteSpace(component[end - 1])) end--;
        if (start == end)
        {
            value = 0;
            failure = ParseFailure.Format;
            return false;
        }
        var first = TChar.CastToUInt32(component[start]);
        var negative = first == '-';
        if (negative || first == '+') start++;
        if (start == end)
        {
            value = 0;
            failure = ParseFailure.Format;
            return false;
        }
        uint parsed = 0;
        for (var index = start; index < end; index++)
        {
            var character = TChar.CastToUInt32(component[index]);
            if (character is < '0' or > '9')
            {
                value = 0;
                failure = ParseFailure.Format;
                return false;
            }
            var digit = character - '0';
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

    private static void ThrowParseFailure<TChar>(ParseFailure failure, string parameterName, ReadOnlySpan<TChar> input)
        where TChar : unmanaged, IUtfChar<TChar>
    {
        if (failure == ParseFailure.Negative) throw new ArgumentOutOfRangeException(parameterName, "Version components must be non-negative.");
        if (failure == ParseFailure.Overflow) throw new OverflowException();
        throw new FormatException($"Version string '{CreateInputString(input)}' is not valid.");
    }

    private static string CreateInputString<TChar>(ReadOnlySpan<TChar> input)
        where TChar : unmanaged, IUtfChar<TChar> =>
        typeof(TChar) == typeof(Utf16Char)
            ? MemoryMarshal.Cast<TChar, char>(input).ToString()
            : Encoding.UTF8.GetString(MemoryMarshal.Cast<TChar, byte>(input));

    private static bool IsWhiteSpace<TChar>(TChar value)
        where TChar : unmanaged, IUtfChar<TChar> =>
        TChar.CastToUInt32(value) is 9 or 10 or 11 or 12 or 13 or 32;

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

    private int GetComponent(int index) => index switch
    {
        0 => _Major,
        1 => _Minor,
        2 => _Build,
        _ => _Revision,
    };

    private static int DecimalLength(int value)
    {
        var length = 1;
        while (value >= 10)
        {
            value /= 10;
            length++;
        }
        return length;
    }

    private static bool TryWriteDecimal<TChar>(Span<TChar> destination, int value, out int written)
        where TChar : unmanaged, IUtfChar<TChar>
    {
        Span<byte> digits = stackalloc byte[10];
        var count = 0;
        do
        {
            digits[count++] = (byte)('0' + value % 10);
            value /= 10;
        }
        while (value != 0);
        if (destination.Length < count)
        {
            written = 0;
            return false;
        }
        for (var i = 0; i < count; i++) destination[i] = TChar.CastFrom(digits[count - i - 1]);
        written = count;
        return true;
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
