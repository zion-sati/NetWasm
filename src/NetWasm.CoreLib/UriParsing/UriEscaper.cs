// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.

using System.Text;

namespace System.UriParsing;

internal sealed class UriEscaper : IUriEscaper
{
    private const string Hex = "0123456789ABCDEF";

    public string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.UTF8.GetBytes(value);
        var result = new StringBuilder(bytes.Length);
        foreach (var valueByte in bytes)
        {
            if (IsUnreserved(valueByte))
            {
                result.Append((char)valueByte);
            }
            else
            {
                result.Append('%');
                result.Append(Hex[valueByte >> 4]);
                result.Append(Hex[valueByte & 0x0F]);
            }
        }
        return result.ToString();
    }

    private static bool IsUnreserved(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' or
            >= (byte)'a' and <= (byte)'z' or
            >= (byte)'0' and <= (byte)'9' or
            (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~';
}
