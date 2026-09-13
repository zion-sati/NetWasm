// Portions derived from dotnet/runtime System.Private.CoreLib WebUtility.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Text;

namespace System.Net
{
    /// <summary>HTML and URL escaping helpers for the ordinal profile.</summary>
    public static class WebUtility
    {
        public static string? HtmlEncode(string? value)
        {
            if (value is null)
            {
                return null;
            }

            var result = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                switch (current)
                {
                    case '&': result.Append("&amp;"); break;
                    case '<': result.Append("&lt;"); break;
                    case '>': result.Append("&gt;"); break;
                    case '"': result.Append("&quot;"); break;
                    case '\'': result.Append("&#39;"); break;
                    default:
                        if (current is >= '\u00a0' and <= '\u00ff')
                        {
                            result.Append("&#").Append((int)current).Append(';');
                        }
                        else if (current is >= '\ud800' and <= '\udbff' && index + 1 < value.Length &&
                                 value[index + 1] is >= '\udc00' and <= '\udfff')
                        {
                            var scalar = ((current - '\ud800') << 10) + (value[++index] - '\udc00') + 0x10000;
                            result.Append("&#").Append(scalar).Append(';');
                        }
                        else if (current is >= '\ud800' and <= '\udfff')
                        {
                            result.Append('\ufffd');
                        }
                        else
                        {
                            result.Append(current);
                        }
                        break;
                }
            }
            return result.ToString();
        }

        public static string? HtmlDecode(string? value)
        {
            if (value is null)
            {
                return null;
            }

            var result = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] != '&')
                {
                    result.Append(value[index]);
                    continue;
                }

                var end = index + 1;
                while (end < value.Length && end - index <= 12 && value[end] != ';' && value[end] != '&')
                {
                    end++;
                }
                if (end >= value.Length || value[end] != ';')
                {
                    result.Append('&');
                    continue;
                }

                var entity = value.Substring(index + 1, end - index - 1);
                if (TryDecodeEntity(entity, out var decoded))
                {
                    result.Append(decoded);
                    index = end;
                }
                else
                {
                    result.Append('&');
                }
            }
            return result.ToString();
        }

        public static string? UrlEncode(string? value)
        {
            if (value is null)
            {
                return null;
            }
            var bytes = Encoding.UTF8.GetBytes(value);
            var result = new StringBuilder(bytes.Length);
            foreach (var valueByte in bytes)
            {
                if (IsUrlSafe(valueByte))
                {
                    result.Append((char)valueByte);
                }
                else if (valueByte == (byte)' ')
                {
                    result.Append('+');
                }
                else
                {
                    result.Append('%').Append(Hex(valueByte >> 4)).Append(Hex(valueByte & 0xf));
                }
            }
            return result.ToString();
        }

        public static byte[]? UrlEncodeToBytes(byte[]? value, int offset, int count)
        {
            ValidateRange(value, offset, count);
            if (value is null)
            {
                return null;
            }
            var result = new List<byte>(count);
            for (var index = offset; index < offset + count; index++)
            {
                var valueByte = value[index];
                if (IsUrlSafe(valueByte))
                {
                    result.Add(valueByte);
                }
                else if (valueByte == (byte)' ')
                {
                    result.Add((byte)'+');
                }
                else
                {
                    result.Add((byte)'%');
                    result.Add((byte)Hex(valueByte >> 4));
                    result.Add((byte)Hex(valueByte & 0xf));
                }
            }
            return result.ToArray();
        }

        public static string? UrlDecode(string? encodedValue)
        {
            if (encodedValue is null)
            {
                return null;
            }

            var result = new StringBuilder(encodedValue.Length);
            var encodedBytes = new List<byte>();
            for (var index = 0; index < encodedValue.Length; index++)
            {
                if (encodedValue[index] == '%' && index + 2 < encodedValue.Length &&
                    TryHex(encodedValue[index + 1], out var high) && TryHex(encodedValue[index + 2], out var low))
                {
                    encodedBytes.Add((byte)((high << 4) | low));
                    index += 2;
                    continue;
                }

                FlushDecodedBytes(result, encodedBytes);
                result.Append(encodedValue[index] == '+' ? ' ' : encodedValue[index]);
            }
            FlushDecodedBytes(result, encodedBytes);
            return result.ToString();
        }

        public static byte[]? UrlDecodeToBytes(byte[]? encodedValue, int offset, int count)
        {
            ValidateRange(encodedValue, offset, count);
            if (encodedValue is null)
            {
                return null;
            }

            var result = new List<byte>(count);
            for (var index = offset; index < offset + count; index++)
            {
                if (encodedValue[index] == '%' && index + 2 < offset + count &&
                    TryHex((char)encodedValue[index + 1], out var high) && TryHex((char)encodedValue[index + 2], out var low))
                {
                    result.Add((byte)((high << 4) | low));
                    index += 2;
                }
                else
                {
                    result.Add(encodedValue[index] == (byte)'+' ? (byte)' ' : encodedValue[index]);
                }
            }
            return result.ToArray();
        }

        private static void FlushDecodedBytes(StringBuilder result, List<byte> bytes)
        {
            if (bytes.Count == 0)
            {
                return;
            }
            result.Append(Encoding.UTF8.GetString(bytes.ToArray()));
            bytes.Clear();
        }

        private static bool TryDecodeEntity(string entity, out string decoded)
        {
            if (entity.Length > 1 && entity[0] == '#')
            {
                var radix = 10;
                var start = 1;
                if (start < entity.Length && (entity[start] == 'x' || entity[start] == 'X'))
                {
                    radix = 16;
                    start++;
                }
                var scalar = 0;
                if (start == entity.Length)
                {
                    decoded = string.Empty;
                    return false;
                }
                for (var index = start; index < entity.Length; index++)
                {
                    var digit = radix == 16 ? HexValue(entity[index]) : entity[index] - '0';
                    if (digit < 0 || digit >= radix || scalar > (0x10ffff - digit) / radix)
                    {
                        decoded = string.Empty;
                        return false;
                    }
                    scalar = scalar * radix + digit;
                }
                if (scalar > 0x10ffff || scalar is >= 0xd800 and <= 0xdfff)
                {
                    decoded = string.Empty;
                    return false;
                }
                if (scalar <= 0xffff)
                {
                    decoded = new string((char)scalar, 1);
                }
                else
                {
                    var pair = scalar - 0x10000;
                    decoded = new StringBuilder(2)
                        .Append((char)((pair >> 10) + 0xd800))
                        .Append((char)((pair & 0x3ff) + 0xdc00))
                        .ToString();
                }
                return true;
            }

            decoded = entity switch
            {
                "amp" => "&",
                "lt" => "<",
                "gt" => ">",
                "quot" => "\"",
                "apos" => "'",
                "nbsp" => "\u00a0",
                "copy" => "\u00a9",
                "reg" => "\u00ae",
                "hellip" => "\u2026",
                _ => string.Empty,
            };
            return decoded.Length != 0;
        }

        private static bool IsUrlSafe(byte value) =>
            value is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z' or
            >= (byte)'0' and <= (byte)'9' or (byte)'!' or (byte)'(' or (byte)')' or
            (byte)'*' or (byte)'-' or (byte)'.' or (byte)'_';

        private static char Hex(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);

        private static bool TryHex(char value, out int result)
        {
            result = HexValue(value);
            return result >= 0;
        }

        private static int HexValue(char value) => value is >= '0' and <= '9'
            ? value - '0'
            : value is >= 'a' and <= 'f'
                ? value - 'a' + 10
                : value is >= 'A' and <= 'F' ? value - 'A' + 10 : -1;

        private static void ValidateRange(byte[]? value, int offset, int count)
        {
            if (value is null)
            {
                if (offset != 0 || count != 0)
                {
                    throw new ArgumentNullException(nameof(value));
                }
                return;
            }
            if (offset < 0 || count < 0 || offset > value.Length - count)
            {
                throw new ArgumentOutOfRangeException();
            }
        }
    }
}
