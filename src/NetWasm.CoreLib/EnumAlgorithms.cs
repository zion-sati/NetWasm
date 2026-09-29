// Adapted from dotnet/runtime System.Private.CoreLib System.Enum at
// c22108eb6662866497186496b1458f4b8cbc8ef0.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace System;

internal static class EnumAlgorithms
{
    internal static ulong Parse(nint metadata, string? value, bool ignoreCase) =>
        Parse(new EnumMetadataView(metadata), value, ignoreCase);

    internal static ulong Parse(
        EnumMetadataView metadata,
        string? value,
        bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(value);

        var text = value!.Trim();
        if (text.Length == 0)
            throw new ArgumentException("Enum text is empty.", nameof(value));

        if (StartsNumeric(text))
        {
            if (TryParseNumeric(
                    metadata.UnderlyingTypeCode, text, out var numeric, out var overflow))
                return numeric;
            if (overflow)
                throw new OverflowException();
        }

        if (TryParseByName(metadata, text, ignoreCase, out var result))
            return result;

        throw new ArgumentException("Enum text does not name a declared value.", nameof(value));
    }

    internal static bool TryParse(
        nint metadata,
        string? value,
        bool ignoreCase,
        out ulong result) =>
        TryParse(new EnumMetadataView(metadata), value, ignoreCase, out result);

    internal static bool TryParse(
        EnumMetadataView metadata,
        string? value,
        bool ignoreCase,
        out ulong result)
    {
        if (value == null)
        {
            result = 0;
            return false;
        }

        var text = value.Trim();
        if (text.Length == 0)
        {
            result = 0;
            return false;
        }

        return StartsNumeric(text)
            ? TryParseNumeric(metadata.UnderlyingTypeCode, text, out result, out _)
            : TryParseByName(metadata, text, ignoreCase, out result);
    }

    internal static string Format(
        nint metadata,
        ulong value,
        string? format) =>
        Format(new EnumMetadataView(metadata), value, format);

    internal static string Format(
        EnumMetadataView metadata,
        ulong value,
        string? format)
    {
        value = Normalize(value, metadata.UnderlyingTypeCode);
        var formatCharacter = format == null || format.Length == 0
            ? 'G'
            : format.Length == 1
                ? format[0]
                : throw new FormatException();

        return formatCharacter switch
        {
            'D' or 'd' => FormatDecimal(value, metadata.UnderlyingTypeCode),
            'X' or 'x' => FormatHex(value, metadata.UnderlyingTypeCode),
            'F' or 'f' => FormatFlags(metadata, value) ??
                          FormatDecimal(value, metadata.UnderlyingTypeCode),
            'G' or 'g' when metadata.HasFlagsAttribute =>
                FormatFlags(metadata, value) ??
                FormatDecimal(value, metadata.UnderlyingTypeCode),
            'G' or 'g' => GetName(metadata, value) ??
                          FormatDecimal(value, metadata.UnderlyingTypeCode),
            _ => throw new FormatException(),
        };
    }

    private static bool StartsNumeric(string value)
    {
        var first = value[0];
        return first is '+' or '-' || (uint)(first - '0') <= 9;
    }

    private static bool TryParseByName(
        EnumMetadataView metadata,
        string value,
        bool ignoreCase,
        out ulong result)
    {
        var names = metadata.Names;
        var values = metadata.Values;
        var remaining = value.AsSpan();
        ulong localResult = 0;

        while (remaining.Length > 0)
        {
            var separator = remaining.IndexOf(',');
            ReadOnlySpan<char> token;
            if (separator < 0)
            {
                token = remaining.Trim();
                remaining = default;
            }
            else if (separator != remaining.Length - 1)
            {
                token = remaining[..separator].Trim();
                remaining = remaining[(separator + 1)..];
            }
            else
            {
                result = 0;
                return false;
            }

            var matched = false;
            for (var index = 0; index < names.Length; index++)
            {
                if (ignoreCase
                        ? token.Equals(names[index], StringComparison.OrdinalIgnoreCase)
                        : token.SequenceEqual(names[index]))
                {
                    localResult |= values[index];
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                result = 0;
                return false;
            }
        }

        result = Normalize(localResult, metadata.UnderlyingTypeCode);
        return true;
    }

    private static string? GetName(EnumMetadataView metadata, ulong value)
    {
        var names = metadata.Names;
        var values = metadata.Values;
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] == value)
                return names[index];
        }

        return null;
    }

    private static string? FormatFlags(EnumMetadataView metadata, ulong value)
    {
        var names = metadata.Names;
        var values = metadata.Values;
        if (value == 0)
            return values.Length > 0 && values[0] == 0 ? names[0] : "0";

        for (var index = values.Length - 1; index >= 0; index--)
        {
            if (values[index] == value)
                return names[index];
        }

        Span<int> foundItems = stackalloc int[64];
        var remaining = value;
        var foundCount = 0;
        var resultLength = 0;
        for (var index = values.Length - 1; index >= 0; index--)
        {
            var current = values[index];
            if (current == 0)
                continue;
            if ((remaining & current) != current)
                continue;

            remaining &= ~current;
            foundItems[foundCount++] = index;
            resultLength = checked(resultLength + names[index].Length);
            if (remaining == 0)
                break;
        }

        if (remaining != 0)
            return null;

        var characters = new char[checked(resultLength + ((foundCount - 1) * 2))];
        var destination = 0;
        for (var item = foundCount - 1; item >= 0; item--)
        {
            var name = names[foundItems[item]];
            for (var character = 0; character < name.Length; character++)
                characters[destination++] = name[character];
            if (item == 0)
                continue;
            characters[destination++] = ',';
            characters[destination++] = ' ';
        }

        return new string(characters);
    }

    private static bool TryParseNumeric(
        int typeCode,
        string value,
        out ulong result,
        out bool overflow)
    {
        var signed = typeCode is 1 or 3 or 5 or 7;
        var bits = typeCode switch
        {
            1 or 2 => 8,
            3 or 4 or 9 => 16,
            5 or 6 => 32,
            _ => 64,
        };
        var index = 0;
        var negative = false;
        if (value[index] is '+' or '-')
        {
            negative = value[index] == '-';
            index++;
        }
        if (index == value.Length)
        {
            result = 0;
            overflow = false;
            return false;
        }

        var limit = negative && !signed
            ? 0
            : signed
            ? negative
                ? 1UL << (bits - 1)
                : (1UL << (bits - 1)) - 1
            : bits == 64
                ? ulong.MaxValue
                : (1UL << bits) - 1;
        ulong magnitude = 0;
        var overflowed = false;
        for (; index < value.Length; index++)
        {
            var digit = (uint)(value[index] - '0');
            if (digit > 9)
            {
                result = 0;
                overflow = false;
                return false;
            }
            if (overflowed)
                continue;
            if (magnitude > limit / 10 ||
                (magnitude == limit / 10 && digit > limit % 10))
            {
                overflowed = true;
                continue;
            }
            magnitude = (magnitude * 10) + digit;
        }

        if (overflowed)
        {
            result = 0;
            overflow = true;
            return false;
        }

        result = Normalize(negative ? unchecked(0UL - magnitude) : magnitude, typeCode);
        overflow = false;
        return true;
    }

    private static string FormatDecimal(ulong value, int typeCode) => typeCode switch
    {
        1 => unchecked((sbyte)value).ToString(CultureInfo.InvariantCulture),
        2 => unchecked((byte)value).ToString(CultureInfo.InvariantCulture),
        3 => unchecked((short)value).ToString(CultureInfo.InvariantCulture),
        4 or 9 => unchecked((ushort)value).ToString(CultureInfo.InvariantCulture),
        5 => unchecked((int)value).ToString(CultureInfo.InvariantCulture),
        6 => unchecked((uint)value).ToString(CultureInfo.InvariantCulture),
        7 => unchecked((long)value).ToString(CultureInfo.InvariantCulture),
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    private static string FormatHex(ulong value, int typeCode) => typeCode switch
    {
        1 or 2 => unchecked((byte)value).ToString("X2", CultureInfo.InvariantCulture),
        3 or 4 or 9 => unchecked((ushort)value).ToString("X4", CultureInfo.InvariantCulture),
        5 or 6 => unchecked((uint)value).ToString("X8", CultureInfo.InvariantCulture),
        _ => value.ToString("X16", CultureInfo.InvariantCulture),
    };

    private static ulong Normalize(ulong value, int typeCode) => typeCode switch
    {
        1 or 2 => (byte)value,
        3 or 4 or 9 => (ushort)value,
        5 or 6 => (uint)value,
        _ => value,
    };
}
