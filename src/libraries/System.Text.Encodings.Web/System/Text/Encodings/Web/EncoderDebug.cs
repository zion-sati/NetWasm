namespace System.Text.Encodings.Web;

internal static class Debug
{
    internal static void Assert(bool condition, string? message = null) { }
}

internal static class EncoderHexConverter
{
    private static readonly char[] Hex = "0123456789ABCDEF".ToCharArray();

    internal static void ToBytesBuffer(byte value, Span<byte> buffer, int startingIndex = 0)
    {
        buffer[startingIndex] = (byte)Hex[value >> 4];
        buffer[startingIndex + 1] = (byte)Hex[value & 0xF];
    }

    internal static void ToCharsBuffer(byte value, Span<char> buffer, int startingIndex = 0)
    {
        buffer[startingIndex] = Hex[value >> 4];
        buffer[startingIndex + 1] = Hex[value & 0xF];
    }
}
