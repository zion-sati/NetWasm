// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Text;

namespace NetWasm.Compiler.Analysis.CompositeFormats;

// Reads a composite format as string.Format reads it (dotnet/runtime's ValueStringBuilder.AppendFormatHelper): text
// with doubled braces for braces, and holes of an index, an optional alignment after a comma and an optional item
// format after a colon, with spaces after the index and around the alignment. One pass over the format, which also
// writes it encoded as TypedCompositeFormat reads it.
internal static class CompositeFormatParser
{
    private const int IndexLimit = 1_000_000;
    private const int WidthLimit = 1_000_000;

    // How TypedCompositeFormat's CompositeFormatBuilder.NextHole reads a hole of the encoded format: '{', one character
    // of HoleBase + index + 8 * (alignment + AlignmentLimit), the item format and '}'. The character is past every brace
    // and below the surrogates for the eight indexes TypedCompositeFormat takes and alignments from -AlignmentLimit up
    // to AlignmentLimit - 1.
    internal const int HoleBase = 0x100;
    internal const int AlignmentLimit = 2048;
    internal const int EncodedIndexes = 8;

    // Whether string.Format formats the format without throwing for any arguments it indexes, how many arguments it
    // indexes (one more than its largest index, none without holes) and, as bits by index below 32, which of them a
    // hole names an item format for (string.Format passes none for an empty one) and which of those an item format
    // other than a hexadecimal one of at most two digits of precision (X, x, X2, x16) for. The format encoded for
    // TypedCompositeFormat is its text as written, doubled braces included, with each hole encoded as above; null where
    // a hole's index or alignment is past what the encoding holds.
    public static bool TryParse(
        string format, out int argumentCount, out uint itemFormatted, out uint otherThanHex, out string? encoded)
    {
        argumentCount = 0;
        itemFormatted = 0;
        otherThanHex = 0;
        encoded = null;
        var encoding = new StringBuilder(format.Length);
        var encodable = true;
        var textStart = 0;
        var position = 0;
        while (true)
        {
            // Text up to the next brace that is not doubled.
            while (true)
            {
                var brace = position < format.Length ? format.AsSpan(position).IndexOfAny('{', '}') : -1;
                if (brace < 0)
                {
                    encoded = encodable ? encoding.Append(format, textStart, format.Length - textStart).ToString() : null;
                    return true;
                }
                position += brace;
                var opening = format[position];
                if (!TryMoveNext(format, ref position, out var next))
                {
                    return false;
                }
                if (opening == next)
                {
                    position++;
                    continue;
                }
                if (opening != '{')
                {
                    return false;
                }
                break;
            }
            encoding.Append(format, textStart, position - 1 - textStart);

            var ch = format[position];
            var index = ch - '0';
            var alignment = 0;
            var itemStart = 0;
            var itemLength = 0;
            if ((uint)index >= 10u || !TryMoveNext(format, ref position, out ch))
            {
                return false;
            }
            if (ch != '}')
            {
                while (char.IsAsciiDigit(ch) && index < IndexLimit)
                {
                    index = index * 10 + ch - '0';
                    if (!TryMoveNext(format, ref position, out ch))
                    {
                        return false;
                    }
                }
                if (!TrySkipSpaces(format, ref position, ref ch))
                {
                    return false;
                }
                if (ch == ',')
                {
                    do
                    {
                        if (!TryMoveNext(format, ref position, out ch))
                        {
                            return false;
                        }
                    }
                    while (ch == ' ');
                    var leftJustify = ch == '-';
                    if (leftJustify && !TryMoveNext(format, ref position, out ch))
                    {
                        return false;
                    }
                    var width = ch - '0';
                    if ((uint)width >= 10u || !TryMoveNext(format, ref position, out ch))
                    {
                        return false;
                    }
                    while (char.IsAsciiDigit(ch) && width < WidthLimit)
                    {
                        width = width * 10 + ch - '0';
                        if (!TryMoveNext(format, ref position, out ch))
                        {
                            return false;
                        }
                    }
                    if (!TrySkipSpaces(format, ref position, ref ch))
                    {
                        return false;
                    }
                    alignment = leftJustify ? -width : width;
                }
                // A colon starts an item format, which runs to the closing brace and holds no opening one; anything
                // else leaves the hole open.
                if (ch != '}')
                {
                    if (ch != ':')
                    {
                        return false;
                    }
                    itemStart = position + 1;
                    do
                    {
                        if (!TryMoveNext(format, ref position, out ch) || ch == '{')
                        {
                            return false;
                        }
                    }
                    while (ch != '}');
                    itemLength = position - itemStart;
                    if (index < 32 && itemLength > 0)
                    {
                        itemFormatted |= 1u << index;
                        if (!IsHexadecimal(format.AsSpan(itemStart, itemLength)))
                        {
                            otherThanHex |= 1u << index;
                        }
                    }
                }
            }
            argumentCount = Math.Max(argumentCount, index + 1);
            if (index < EncodedIndexes && alignment >= -AlignmentLimit && alignment < AlignmentLimit)
            {
                encoding.Append('{')
                    .Append((char)(HoleBase + index + 8 * (alignment + AlignmentLimit)))
                    .Append(format, itemStart, itemLength)
                    .Append('}');
            }
            else
            {
                encodable = false;
            }
            position++;
            textStart = position;
        }
    }

    internal static bool IsHexadecimal(ReadOnlySpan<char> itemFormat) =>
        itemFormat.Length is >= 1 and <= 3 && itemFormat[0] is 'X' or 'x' && !itemFormat[1..].ContainsAnyExceptInRange('0', '9');

    private static bool TrySkipSpaces(string format, ref int position, ref char ch)
    {
        while (ch == ' ')
        {
            if (!TryMoveNext(format, ref position, out ch))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryMoveNext(string format, ref int position, out char ch)
    {
        position++;
        if (position >= format.Length)
        {
            ch = default;
            return false;
        }
        ch = format[position];
        return true;
    }
}
