using System.IO;
using System.Text;

namespace System.Runtime.InteropServices.ConsoleServices;

internal sealed class ConsoleTextWriter : TextWriter
{
    private readonly IConsoleIntegerLineFormatter _integerFormatter;
    private readonly IConsoleStringLineFormatter _stringFormatter;
    private readonly IConsoleOutputStreamSource _streamSource;

    internal ConsoleTextWriter(
        IConsoleIntegerLineFormatter integerFormatter,
        IConsoleStringLineFormatter stringFormatter,
        IConsoleOutputStreamSource streamSource)
    {
        _integerFormatter = integerFormatter ?? throw new ArgumentNullException();
        _stringFormatter = stringFormatter ?? throw new ArgumentNullException();
        _streamSource = streamSource ?? throw new ArgumentNullException();
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value) => Write(value.ToString());

    public override void Write(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Write(new string(buffer, index, count));
    }

    public override void Write(ReadOnlySpan<char> buffer) => Write(buffer.ToString());

    public override void Write(string? value)
    {
        if (value is not null)
        {
            WriteBytes(Encoding.UTF8.GetBytes(value));
        }
    }

    public override void WriteLine(int value) => WriteBytes(_integerFormatter.Format(value));

    public override void WriteLine(string? value) => WriteBytes(_stringFormatter.Format(value));

    private void WriteBytes(byte[] contents)
    {
        if (contents.Length == 0)
        {
            return;
        }

        using var output = _streamSource.Open();
        output.WriteBlocking(contents);
    }
}
