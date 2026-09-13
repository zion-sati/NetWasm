// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/runtime System.Private.CoreLib Guid.cs.
// Upstream commit: 811225a482702af7ecc35d817966bc70b88a3a23.
// Guid.NewGuid uses the narrow WASI cryptographically secure entropy bridge;
// no weak pseudo-random or JavaScript fallback is permitted.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System;

// Represents a Globally Unique Identifier.
public readonly partial struct Guid : IComparable,
    IComparable<Guid>,
    IEquatable<Guid>,
    IFormattable,
    IParsable<Guid>,
    ISpanFormattable,
    ISpanParsable<Guid>,
    IUtf8SpanFormattable,
    IUtf8SpanParsable<Guid>
{
    internal const int TryFormatFlags_UseDashes = unchecked((int)0x80000000);
    internal const int TryFormatFlags_CurlyBraces = ('}' << 16) | ('{' << 8);
    internal const int TryFormatFlags_Parens = (')' << 16) | ('(' << 8);

    private const byte Variant10xxMask = 0xC0;
    private const byte Variant10xxValue = 0x80;
    private const ushort VersionMask = 0xF000;
    private const ushort Version4Value = 0x4000;
    private const ushort Version7Value = 0x7000;

    public static readonly Guid Empty;

    private readonly int _a;
    private readonly short _b;
    private readonly short _c;
    private readonly byte _d;
    private readonly byte _e;
    private readonly byte _f;
    private readonly byte _g;
    private readonly byte _h;
    private readonly byte _i;
    private readonly byte _j;
    private readonly byte _k;

    public Guid(byte[] b)
        : this(new ReadOnlySpan<byte>(b ?? throw new ArgumentNullException(nameof(b))))
    {
    }

    public Guid(ReadOnlySpan<byte> b)
    {
        if (b.Length != 16)
        {
            throw new ArgumentException("Byte array for Guid must be exactly 16 bytes.", nameof(b));
        }

        _a = (int)((uint)b[0] | ((uint)b[1] << 8) | ((uint)b[2] << 16) | ((uint)b[3] << 24));
        _b = (short)(b[4] | (b[5] << 8));
        _c = (short)(b[6] | (b[7] << 8));
        _d = b[8];
        _e = b[9];
        _f = b[10];
        _g = b[11];
        _h = b[12];
        _i = b[13];
        _j = b[14];
        _k = b[15];
    }

    public Guid(ReadOnlySpan<byte> b, bool bigEndian)
    {
        if (b.Length != 16)
        {
            throw new ArgumentException("Byte array for Guid must be exactly 16 bytes.", nameof(b));
        }

        if (bigEndian)
        {
            _a = (int)((uint)b[3] | ((uint)b[2] << 8) | ((uint)b[1] << 16) | ((uint)b[0] << 24));
            _b = (short)((b[5] << 8) | b[4]);
            _c = (short)((b[7] << 8) | b[6]);
        }
        else
        {
            _a = (int)((uint)b[0] | ((uint)b[1] << 8) | ((uint)b[2] << 16) | ((uint)b[3] << 24));
            _b = (short)(b[4] | (b[5] << 8));
            _c = (short)(b[6] | (b[7] << 8));
        }

        _d = b[8];
        _e = b[9];
        _f = b[10];
        _g = b[11];
        _h = b[12];
        _i = b[13];
        _j = b[14];
        _k = b[15];
    }

    public Guid(uint a, ushort b, ushort c, byte d, byte e, byte f, byte g, byte h, byte i, byte j, byte k)
    {
        _a = (int)a;
        _b = (short)b;
        _c = (short)c;
        _d = d;
        _e = e;
        _f = f;
        _g = g;
        _h = h;
        _i = i;
        _j = j;
        _k = k;
    }

    public Guid(int a, short b, short c, byte[] d)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (d.Length != 8)
        {
            throw new ArgumentException("Byte array for Guid tail must be exactly 8 bytes.", nameof(d));
        }

        _a = a;
        _b = b;
        _c = c;
        _d = d[0];
        _e = d[1];
        _f = d[2];
        _g = d[3];
        _h = d[4];
        _i = d[5];
        _j = d[6];
        _k = d[7];
    }

    public Guid(int a, short b, short c, byte d, byte e, byte f, byte g, byte h, byte i, byte j, byte k)
        : this((uint)a, (ushort)b, (ushort)c, d, e, f, g, h, i, j, k)
    {
    }

    public Guid(string g)
    {
        ArgumentNullException.ThrowIfNull(g);
        if (!TryParseCore(g.AsSpan(), out this, true))
        {
            throw new FormatException("Guid string was not recognized.");
        }
    }

    public int Variant
    {
        get => _d >> 4;
    }

    public int Version
    {
        get => (ushort)_c >>> 12;
    }

    public static Guid AllBitsSet
    {
        get => new(
            uint.MaxValue, ushort.MaxValue, ushort.MaxValue,
            byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue,
            byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
    }

    /// <summary>Creates a new <see cref="Guid" /> according to RFC 9562, following the Version 7 format.</summary>
    /// <returns>A new <see cref="Guid" /> according to RFC 9562, following the Version 7 format.</returns>
    /// <remarks>
    /// This uses <see cref="DateTimeOffset.UtcNow" /> for the Unix Epoch timestamp and
    /// cryptographically secure entropy for the random fields.
    /// </remarks>
    public static Guid CreateVersion7() => CreateVersion7(DateTimeOffset.UtcNow);

    /// <summary>Creates a new <see cref="Guid" /> according to RFC 9562, following the Version 7 format.</summary>
    /// <param name="timestamp">The date time offset used to determine the Unix Epoch timestamp.</param>
    /// <returns>A new <see cref="Guid" /> according to RFC 9562, following the Version 7 format.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timestamp" /> represents an offset prior to <see cref="DateTimeOffset.UnixEpoch" />.</exception>
    /// <remarks>This seeds the rand_a and rand_b sub-fields with cryptographically secure entropy.</remarks>
    public static Guid CreateVersion7(DateTimeOffset timestamp)
    {
        var result = NewGuid();

        // 2^48 is roughly 8925.5 years, so DateTimeOffset.MaxValue cannot overflow
        // the UUIDv7 timestamp field. Timestamps before the Unix Epoch are invalid
        // because UUIDv7 stores an unsigned 48-bit millisecond value.
        var unixTsMs = timestamp.ToUnixTimeMilliseconds();
        ArgumentOutOfRangeException.ThrowIfNegative(unixTsMs, nameof(timestamp));

        Unsafe.AsRef(in result._a) = (int)(unixTsMs >> 16);
        Unsafe.AsRef(in result._b) = (short)unixTsMs;
        Unsafe.AsRef(in result._c) = (short)((result._c & ~VersionMask) | Version7Value);
        Unsafe.AsRef(in result._d) = (byte)((result._d & ~Variant10xxMask) | Variant10xxValue);

        return result;
    }

    /// <summary>Creates a new Version 4 GUID from cryptographically secure entropy.</summary>
    /// <remarks>
    /// The NetWasm profile provides the narrow internal boundary
    /// <c>Interop.GetCryptographicallySecureRandomBytes(byte* buffer, int length)</c>.
    /// It fills exactly <paramref name="length" /> bytes or throws a
    /// <see cref="System.Security.Cryptography.CryptographicException" />; no pseudo-random substitute is valid.
    /// </remarks>
    public static unsafe Guid NewGuid()
    {
        var result = default(Guid);
        Interop.GetCryptographicallySecureRandomBytes((byte*)&result, sizeof(Guid));

        unchecked
        {
            Unsafe.AsRef(in result._c) = (short)((result._c & ~VersionMask) | Version4Value);
            Unsafe.AsRef(in result._d) = (byte)((result._d & ~Variant10xxMask) | Variant10xxValue);
        }

        return result;
    }

    public static Guid Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Parse(input.AsSpan());
    }

    public static Guid Parse(ReadOnlySpan<char> input)
    {
        if (!TryParseCore(input, out var result, true))
        {
            throw new FormatException("Guid string was not recognized.");
        }
        return result;
    }

    public static Guid Parse(ReadOnlySpan<byte> utf8Text)
    {
        if (!TryParseCore(utf8Text, out var result, true))
        {
            throw new FormatException("Guid string was not recognized.");
        }
        return result;
    }

    public static bool TryParse([NotNullWhen(true)] string? input, out Guid result)
    {
        if (input is null)
        {
            result = default;
            return false;
        }
        return TryParse(input.AsSpan(), out result);
    }

    public static bool TryParse(ReadOnlySpan<char> input, out Guid result) => TryParseCore(input, out result, false);

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out Guid result) => TryParseCore(utf8Text, out result, false);

    public static Guid ParseExact(string input, string format)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(format);
        return ParseExact(input.AsSpan(), format.AsSpan());
    }

    public static Guid ParseExact(ReadOnlySpan<char> input, ReadOnlySpan<char> format)
    {
        if (format.Length != 1)
        {
            throw new FormatException("Guid format specification must be one character.");
        }
        input = Trim(input);
        var kind = Lower(format[0]);
        if (!TryParseExact(input, kind, out var result, true))
        {
            throw new FormatException("Guid string was not recognized.");
        }
        return result;
    }

    public static bool TryParseExact([NotNullWhen(true)] string? input, [NotNullWhen(true)] string? format, out Guid result)
    {
        if (input is null || format is null)
        {
            result = default;
            return false;
        }
        return TryParseExact(input.AsSpan(), format.AsSpan(), out result);
    }

    public static bool TryParseExact(ReadOnlySpan<char> input, ReadOnlySpan<char> format, out Guid result)
    {
        if (format.Length != 1)
        {
            result = default;
            return false;
        }
        return TryParseExact(Trim(input), Lower(format[0]), out result, false);
    }

    private static bool TryParseCore(ReadOnlySpan<char> input, out Guid result, bool throwOnOverflow)
    {
        input = Trim(input);
        if (input.Length < 32)
        {
            result = default;
            return false;
        }

        var kind = input[0] switch
        {
            '(' => 'p',
            '{' when input.Length > 9 && input[9] == '-' => 'b',
            '{' => 'x',
            _ when input.Length > 8 && input[8] == '-' => 'd',
            _ => 'n'
        };
        return TryParseExact(input, kind, out result, throwOnOverflow);
    }

    private static bool TryParseCore(ReadOnlySpan<byte> input, out Guid result, bool throwOnOverflow)
    {
        input = Trim(input);
        if (input.Length < 32)
        {
            result = default;
            return false;
        }

        var kind = input[0] switch
        {
            (byte)'(' => 'p',
            (byte)'{' when input.Length > 9 && input[9] == (byte)'-' => 'b',
            (byte)'{' => 'x',
            _ when input.Length > 8 && input[8] == (byte)'-' => 'd',
            _ => 'n'
        };
        return TryParseExact(input, kind, out result, throwOnOverflow);
    }

    private static bool TryParseExact(ReadOnlySpan<char> input, char kind, out Guid result, bool throwOnOverflow)
    {
        if (kind is 'd' or 'b' or 'p' or 'n')
        {
            var offset = kind is 'b' or 'p' ? 1 : 0;
            var length = kind is 'b' or 'p' ? 38 : kind == 'd' ? 36 : 32;
            var open = kind == 'b' ? '{' : kind == 'p' ? '(' : '\0';
            var close = kind == 'b' ? '}' : kind == 'p' ? ')' : '\0';
            if (input.Length != length || (open != '\0' && (input[0] != open || input[^1] != close)))
            {
                result = default;
                return false;
            }

            Span<byte> bytes = stackalloc byte[16];
            if (kind == 'd' || kind == 'b' || kind == 'p')
            {
                if (input[offset + 8] != '-' || input[offset + 13] != '-' || input[offset + 18] != '-' || input[offset + 23] != '-')
                {
                    result = default;
                    return false;
                }
                var positions = new int[16, 2]
                {
                    { 6, 7 }, { 4, 5 }, { 2, 3 }, { 0, 1 },
                    { 11, 12 }, { 9, 10 }, { 16, 17 }, { 14, 15 },
                    { 19, 20 }, { 21, 22 }, { 24, 25 }, { 26, 27 },
                    { 28, 29 }, { 30, 31 }, { 32, 33 }, { 34, 35 }
                };
                for (var i = 0; i < bytes.Length; i++)
                {
                    if (!TryDecodeByte(input[offset + positions[i, 0]], input[offset + positions[i, 1]], out bytes[i]))
                    {
                        result = default;
                        return false;
                    }
                }
            }
            else
            {
                for (var i = 0; i < 16; i++)
                {
                    if (!TryDecodeByte(input[i * 2], input[i * 2 + 1], out bytes[i]))
                    {
                        result = default;
                        return false;
                    }
                }
            }

            result = new Guid(bytes);
            return true;
        }

        if (kind == 'x')
        {
            return TryParseX(input, out result, throwOnOverflow);
        }

        result = default;
        return false;
    }

    private static bool TryParseExact(ReadOnlySpan<byte> input, char kind, out Guid result, bool throwOnOverflow)
    {
        Span<char> chars = stackalloc char[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] > 0x7f)
            {
                result = default;
                return false;
            }
            chars[i] = (char)input[i];
        }
        return TryParseExact(chars, kind, out result, throwOnOverflow);
    }

    private static bool TryParseX(ReadOnlySpan<char> input, out Guid result, bool throwOnOverflow)
    {
        var index = 0;
        SkipWhitespace(input, ref index);
        if (!Take(input, ref index, '{') || !TryReadHexComponent(input, ref index, out var a, 8, throwOnOverflow) ||
            !Take(input, ref index, ',') || !TryReadHexComponent(input, ref index, out var b, 4, throwOnOverflow) ||
            !Take(input, ref index, ',') || !TryReadHexComponent(input, ref index, out var c, 4, throwOnOverflow) ||
            !Take(input, ref index, ',') || !Take(input, ref index, '{'))
        {
            result = default;
            return false;
        }

        Span<byte> tail = stackalloc byte[8];
        for (var i = 0; i < tail.Length; i++)
        {
            if (!TryReadHexComponent(input, ref index, out var value, 2, throwOnOverflow) || value > byte.MaxValue)
            {
                if (value > byte.MaxValue && throwOnOverflow) throw new OverflowException();
                result = default;
                return false;
            }
            tail[i] = (byte)value;
            if (i != tail.Length - 1 && !Take(input, ref index, ','))
            {
                result = default;
                return false;
            }
        }

        if (!Take(input, ref index, '}') || !Take(input, ref index, '}'))
        {
            result = default;
            return false;
        }
        SkipWhitespace(input, ref index);
        if (index != input.Length)
        {
            result = default;
            return false;
        }
        result = new Guid(a, (ushort)b, (ushort)c, tail[0], tail[1], tail[2], tail[3], tail[4], tail[5], tail[6], tail[7]);
        return true;
    }

    private static bool TryReadHexComponent(ReadOnlySpan<char> input, ref int index, out uint value, int expectedDigits, bool throwOnOverflow)
    {
        SkipWhitespace(input, ref index);
        if (index < input.Length && input[index] == '+') index++;
        if (index + 1 >= input.Length || input[index] != '0' || Lower(input[index + 1]) != 'x')
        {
            value = 0;
            return false;
        }
        index += 2;
        // The desktop parser accepts the historical 0x+, 0x+0x forms.
        if (index < input.Length && input[index] == '+') index++;
        if (index + 1 < input.Length && input[index] == '0' && Lower(input[index + 1]) == 'x') index += 2;
        SkipWhitespace(input, ref index);
        var start = index;
        var digitCount = 0;
        value = 0;
        while (index < input.Length)
        {
            if (char.IsWhiteSpace(input[index]))
            {
                index++;
                continue;
            }
            var digit = HexValue(input[index]);
            if (digit < 0) break;
            if (value > (uint.MaxValue - (uint)digit) / 16)
            {
                if (throwOnOverflow) throw new OverflowException();
                value = 0;
                return false;
            }
            value = value * 16 + (uint)digit;
            index++;
            digitCount++;
        }
        return digitCount != 0;
    }

    private static bool Take(ReadOnlySpan<char> input, ref int index, char value)
    {
        SkipWhitespace(input, ref index);
        if (index >= input.Length || input[index] != value) return false;
        index++;
        return true;
    }

    private static void SkipWhitespace(ReadOnlySpan<char> input, ref int index)
    {
        while (index < input.Length && char.IsWhiteSpace(input[index])) index++;
    }

    private static ReadOnlySpan<char> Trim(ReadOnlySpan<char> input)
    {
        var start = 0;
        var end = input.Length;
        while (start < end && char.IsWhiteSpace(input[start])) start++;
        while (end > start && char.IsWhiteSpace(input[end - 1])) end--;
        return input.Slice(start, end - start);
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> input)
    {
        var start = 0;
        var end = input.Length;
        while (start < end && IsAsciiWhitespace(input[start])) start++;
        while (end > start && IsAsciiWhitespace(input[end - 1])) end--;
        return input.Slice(start, end - start);
    }

    private static bool IsAsciiWhitespace(byte value) => value is 9 or 10 or 11 or 12 or 13 or 32;

    private static bool TryDecodeByte(char high, char low, out byte value)
    {
        var h = HexValue(high);
        var l = HexValue(low);
        if (h < 0 || l < 0)
        {
            value = 0;
            return false;
        }
        value = (byte)((h << 4) | l);
        return true;
    }

    private static int HexValue(char value) =>
        value is >= '0' and <= '9' ? value - '0' :
        value is >= 'a' and <= 'f' ? value - 'a' + 10 :
        value is >= 'A' and <= 'F' ? value - 'A' + 10 : -1;

    private static char Lower(char value) => value is >= 'A' and <= 'Z' ? (char)(value + ('a' - 'A')) : value;

    public byte[] ToByteArray()
    {
        var bytes = new byte[16];
        WriteLittleEndian(bytes);
        return bytes;
    }

    public byte[] ToByteArray(bool bigEndian)
    {
        var bytes = new byte[16];
        if (bigEndian)
        {
            WriteBigEndian(bytes);
        }
        else
        {
            WriteLittleEndian(bytes);
        }
        return bytes;
    }

    public bool TryWriteBytes(Span<byte> destination)
    {
        if (destination.Length < 16) return false;
        WriteLittleEndian(destination);
        return true;
    }

    public bool TryWriteBytes(Span<byte> destination, bool bigEndian, out int bytesWritten)
    {
        if (destination.Length < 16)
        {
            bytesWritten = 0;
            return false;
        }
        if (bigEndian) WriteBigEndian(destination);
        else WriteLittleEndian(destination);
        bytesWritten = 16;
        return true;
    }

    private void WriteLittleEndian(Span<byte> destination)
    {
        destination[0] = (byte)_a;
        destination[1] = (byte)(_a >> 8);
        destination[2] = (byte)(_a >> 16);
        destination[3] = (byte)(_a >> 24);
        destination[4] = (byte)_b;
        destination[5] = (byte)(_b >> 8);
        destination[6] = (byte)_c;
        destination[7] = (byte)(_c >> 8);
        destination[8] = _d;
        destination[9] = _e;
        destination[10] = _f;
        destination[11] = _g;
        destination[12] = _h;
        destination[13] = _i;
        destination[14] = _j;
        destination[15] = _k;
    }

    private void WriteBigEndian(Span<byte> destination)
    {
        destination[0] = (byte)(_a >> 24);
        destination[1] = (byte)(_a >> 16);
        destination[2] = (byte)(_a >> 8);
        destination[3] = (byte)_a;
        destination[4] = (byte)(_b >> 8);
        destination[5] = (byte)_b;
        destination[6] = (byte)(_c >> 8);
        destination[7] = (byte)_c;
        destination[8] = _d;
        destination[9] = _e;
        destination[10] = _f;
        destination[11] = _g;
        destination[12] = _h;
        destination[13] = _i;
        destination[14] = _j;
        destination[15] = _k;
    }

    public override int GetHashCode() => _a ^ (_b << 16 | (ushort)_c) ^ (_d << 24 | _e << 16 | _f << 8 | _g) ^ (_h << 24 | _i << 16 | _j << 8 | _k);

    public override bool Equals([NotNullWhen(true)] object? o) => o is Guid g && Equals(g);

    public bool Equals(Guid g) =>
        _a == g._a && _b == g._b && _c == g._c && _d == g._d && _e == g._e && _f == g._f &&
        _g == g._g && _h == g._h && _i == g._i && _j == g._j && _k == g._k;

    public int CompareTo(object? value)
    {
        if (value is null) return 1;
        if (value is not Guid other) throw new ArgumentException("Object must be of type Guid.", nameof(value));
        return CompareTo(other);
    }

    public int CompareTo(Guid value)
    {
        if (_a != value._a) return CompareUnsigned((uint)_a, (uint)value._a);
        if (_b != value._b) return CompareUnsigned((uint)(ushort)_b, (uint)(ushort)value._b);
        if (_c != value._c) return CompareUnsigned((uint)(ushort)_c, (uint)(ushort)value._c);
        if (_d != value._d) return CompareUnsigned(_d, value._d);
        if (_e != value._e) return CompareUnsigned(_e, value._e);
        if (_f != value._f) return CompareUnsigned(_f, value._f);
        if (_g != value._g) return CompareUnsigned(_g, value._g);
        if (_h != value._h) return CompareUnsigned(_h, value._h);
        if (_i != value._i) return CompareUnsigned(_i, value._i);
        if (_j != value._j) return CompareUnsigned(_j, value._j);
        if (_k != value._k) return CompareUnsigned(_k, value._k);
        return 0;
    }

    private static int CompareUnsigned(uint left, uint right) => left < right ? -1 : 1;

    public static bool operator ==(Guid a, Guid b) => a.Equals(b);
    public static bool operator !=(Guid a, Guid b) => !a.Equals(b);
    public static bool operator <(Guid left, Guid right) => left.CompareTo(right) < 0;
    public static bool operator <=(Guid left, Guid right) => left.CompareTo(right) <= 0;
    public static bool operator >(Guid left, Guid right) => left.CompareTo(right) > 0;
    public static bool operator >=(Guid left, Guid right) => left.CompareTo(right) >= 0;

    public override string ToString() => ToString("d", null);
    public string ToString(string? format) => ToString(format, null);

    public string ToString(string? format, IFormatProvider? provider)
    {
        var kind = string.IsNullOrEmpty(format) ? 'd' : Lower(format![0]);
        if (!string.IsNullOrEmpty(format) && format!.Length != 1) throw new FormatException("Guid format specification must be one character.");
        var length = kind is 'b' or 'p' ? 38 : kind == 'n' ? 32 : kind == 'x' ? 68 : kind == 'd' ? 36 : 0;
        if (length == 0) throw new FormatException("Guid format specification is invalid.");
        var chars = new char[length];
        if (!TryFormatCore(chars, out var written, kind, true)) throw new FormatException("Guid formatting failed.");
        return string.Create(chars, 0, written);
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, [StringSyntax(StringSyntaxAttribute.GuidFormat)] ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>)) =>
        TryFormatCore(destination, out charsWritten, format.Length == 0 ? 'd' : Lower(format[0]), format.Length == 0 || format.Length == 1);

    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, [StringSyntax(StringSyntaxAttribute.GuidFormat)] ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>)) =>
        TryFormatCore(utf8Destination, out bytesWritten, format.Length == 0 ? 'd' : Lower(format[0]), format.Length == 0 || format.Length == 1);

    bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => TryFormat(destination, out charsWritten, format);
    bool IUtf8SpanFormattable.TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider) => TryFormat(destination, out bytesWritten, format);

    internal bool TryFormatCore(Span<char> destination, out int written, char kind, bool validFormat)
    {
        var required = kind is 'b' or 'p' ? 38 : kind == 'n' ? 32 : kind == 'x' ? 68 : kind == 'd' ? 36 : 0;
        if (!validFormat || required == 0) throw new FormatException("Guid format specification is invalid.");
        if (destination.Length < required)
        {
            written = 0;
            return false;
        }
        if (kind == 'x')
        {
            WriteX(destination);
            written = 68;
            return true;
        }

        var index = 0;
        if (kind == 'b' || kind == 'p') destination[index++] = kind == 'b' ? '{' : '(';
        WriteHex(destination, ref index, (uint)_a, 8);
        if (kind != 'n') destination[index++] = '-';
        WriteHex(destination, ref index, (ushort)_b, 4);
        if (kind != 'n') destination[index++] = '-';
        WriteHex(destination, ref index, (ushort)_c, 4);
        if (kind != 'n') destination[index++] = '-';
        WriteHex(destination, ref index, _d, 2);
        WriteHex(destination, ref index, _e, 2);
        if (kind != 'n') destination[index++] = '-';
        WriteHex(destination, ref index, _f, 2);
        WriteHex(destination, ref index, _g, 2);
        WriteHex(destination, ref index, _h, 2);
        WriteHex(destination, ref index, _i, 2);
        WriteHex(destination, ref index, _j, 2);
        WriteHex(destination, ref index, _k, 2);
        if (kind == 'b' || kind == 'p') destination[index++] = kind == 'b' ? '}' : ')';
        written = index;
        return true;
    }

    private bool TryFormatCore(Span<byte> destination, out int written, char kind, bool validFormat)
    {
        var chars = new char[kind is 'b' or 'p' ? 38 : kind == 'n' ? 32 : kind == 'x' ? 68 : 36];
        var ok = TryFormatCore(chars, out var count, kind, validFormat);
        if (!ok || destination.Length < count)
        {
            written = 0;
            return false;
        }
        for (var i = 0; i < count; i++) destination[i] = (byte)chars[i];
        written = count;
        return true;
    }

    // Compatibility entry point used by the Microsoft Utf8Formatter.Guid port.
    internal bool TryFormatCore(Span<byte> destination, out int written, int flags)
    {
        var required = (byte)flags;
        var opening = (char)(byte)(flags >> 8);
        var kind = required switch
        {
            32 => 'n',
            36 => 'd',
            38 when opening == '{' => 'b',
            38 when opening == '(' => 'p',
            _ => '\0'
        };
        return TryFormatCore(destination, out written, kind, required != 0 && (flags < 0 || required == 32 || required == 36 || required == 38));
    }

    private static void WriteHex(Span<char> destination, ref int index, uint value, int digits)
    {
        for (var shift = (digits - 1) * 4; shift >= 0; shift -= 4)
        {
            destination[index++] = Hex((byte)(value >> shift & 0xf));
        }
    }

    private void WriteX(Span<char> destination)
    {
        var index = 0;
        destination[index++] = '{'; destination[index++] = '0'; destination[index++] = 'x'; WriteHex(destination, ref index, (uint)_a, 8);
        destination[index++] = ','; destination[index++] = '0'; destination[index++] = 'x'; WriteHex(destination, ref index, (ushort)_b, 4);
        destination[index++] = ','; destination[index++] = '0'; destination[index++] = 'x'; WriteHex(destination, ref index, (ushort)_c, 4);
        destination[index++] = ','; destination[index++] = '{';
        WriteXByte(destination, ref index, _d, true); WriteXByte(destination, ref index, _e, true); WriteXByte(destination, ref index, _f, true); WriteXByte(destination, ref index, _g, true);
        WriteXByte(destination, ref index, _h, true); WriteXByte(destination, ref index, _i, true); WriteXByte(destination, ref index, _j, true); WriteXByte(destination, ref index, _k, false);
        destination[index++] = '}'; destination[index] = '}';
    }

    private static void WriteXByte(Span<char> destination, ref int index, byte value, bool comma)
    {
        destination[index++] = '0'; destination[index++] = 'x'; destination[index++] = Hex((byte)(value >> 4)); destination[index++] = Hex((byte)(value & 0xf));
        if (comma) destination[index++] = ',';
    }

    private static char Hex(byte value) => value < 10 ? (char)('0' + value) : (char)('a' + value - 10);

    public static Guid Parse(string s, IFormatProvider? provider) => Parse(s);
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Guid result) => TryParse(s, out result);
    public static Guid Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Guid result) => TryParse(s, out result);
    public static Guid Parse(ReadOnlySpan<byte> s, IFormatProvider? provider) => Parse(s);
    public static bool TryParse(ReadOnlySpan<byte> s, IFormatProvider? provider, out Guid result) => TryParse(s, out result);
}
