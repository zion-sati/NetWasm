// Licensed under the MIT License. Adapted support for pinned System.Private.Uri behavior.

using System.Text;

namespace System.UriParsing;

internal sealed class UriUnescaper : IUriUnescaper
{
    public string Unescape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var result = new StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            if (value[index] != '%')
            {
                result.Append(value[index++]);
                continue;
            }

            var start = index;
            var bytes = new byte[(value.Length - index) / 3 + 1];
            var count = 0;
            while (index + 2 < value.Length && value[index] == '%')
            {
                var high = HexValue(value[index + 1]);
                var low = HexValue(value[index + 2]);
                if (high < 0 || low < 0)
                {
                    break;
                }
                bytes[count++] = (byte)((high << 4) | low);
                index += 3;
            }
            if (count == 0)
            {
                result.Append(value[start]);
                index = start + 1;
                continue;
            }
            result.Append(Encoding.UTF8.GetString(bytes, 0, count));
        }
        return result.ToString();
    }

    private static int HexValue(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'A' and <= 'F' => value - 'A' + 10,
        >= 'a' and <= 'f' => value - 'a' + 10,
        _ => -1,
    };
}
