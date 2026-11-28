// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.ConsoleServices;

// Console text in UTF-8, the bytes Encoding.UTF8 writes: a surrogate pair is one four-byte character and an unpaired
// surrogate becomes U+FFFD. A loop over code units rather than System.Text.Unicode.Utf8's vectorized transcoder:
// console text is short and goes to a stream, and the transcoder would be a quarter of what Console.WriteLine(42)
// carries.
internal static class ConsoleUtf8Encoder
{
    private const int ReplacementCharacter = 0xFFFD;

    // Encodes the characters of text that fit in destination, a surrogate pair whole or not at all, and returns how
    // many code units it read.
    internal static int Encode(ReadOnlySpan<char> text, Span<byte> destination, out int bytesWritten)
    {
        var read = 0;
        var written = 0;
        while (read < text.Length)
        {
            int value = text[read];
            if (value < 0x80)
            {
                if (written == destination.Length)
                {
                    break;
                }

                destination[written++] = (byte)value;
                read++;
                continue;
            }

            var units = 1;
            if ((uint)(value - 0xD800) <= 0x7FF)
            {
                if (value <= 0xDBFF && read + 1 < text.Length && (uint)(text[read + 1] - 0xDC00) <= 0x3FF)
                {
                    value = 0x10000 + ((value - 0xD800) << 10) + (text[read + 1] - 0xDC00);
                    units = 2;
                }
                else
                {
                    value = ReplacementCharacter;
                }
            }

            var length = value < 0x800 ? 2 : value < 0x10000 ? 3 : 4;
            if (destination.Length - written < length)
            {
                break;
            }

            switch (length)
            {
                case 2:
                    destination[written] = (byte)(0xC0 | (value >> 6));
                    break;
                case 3:
                    destination[written] = (byte)(0xE0 | (value >> 12));
                    destination[written + 1] = (byte)(0x80 | ((value >> 6) & 0x3F));
                    break;
                default:
                    destination[written] = (byte)(0xF0 | (value >> 18));
                    destination[written + 1] = (byte)(0x80 | ((value >> 12) & 0x3F));
                    destination[written + 2] = (byte)(0x80 | ((value >> 6) & 0x3F));
                    break;
            }

            destination[written + length - 1] = (byte)(0x80 | (value & 0x3F));
            written += length;
            read += units;
        }

        bytesWritten = written;
        return read;
    }
}
