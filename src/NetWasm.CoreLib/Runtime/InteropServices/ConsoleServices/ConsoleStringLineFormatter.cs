using System.Text;

namespace System.Runtime.InteropServices.ConsoleServices;

internal sealed class ConsoleStringLineFormatter : IConsoleStringLineFormatter
{
    public byte[] Format(string? value)
    {
        var valueBytes = value is null ? [] : Encoding.UTF8.GetBytes(value);
        var line = new byte[valueBytes.Length + 1];
        Array.Copy(valueBytes, line, valueBytes.Length);
        line[^1] = (byte)'\n';
        return line;
    }
}
