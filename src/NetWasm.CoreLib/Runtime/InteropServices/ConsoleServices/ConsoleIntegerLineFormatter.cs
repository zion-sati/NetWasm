using System.Text;

namespace System.Runtime.InteropServices.ConsoleServices;

internal sealed class ConsoleIntegerLineFormatter : IConsoleIntegerLineFormatter
{
    public byte[] Format(int value)
    {
        var valueBytes = Encoding.UTF8.GetBytes(value.ToString());
        var line = new byte[valueBytes.Length + 1];
        Array.Copy(valueBytes, line, valueBytes.Length);
        line[^1] = (byte)'\n';
        return line;
    }
}
