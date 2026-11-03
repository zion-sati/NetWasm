using System.IO;
using System.Text;

namespace System.Runtime.InteropServices.ConsoleServices;

// Floating-point and decimal values are written with a null provider. FormatProvider is always
// the current culture here, and the no-provider form writes the same text, so console output does
// not make the provider-driven formatter reachable.
internal sealed class ConsoleTextWriter : TextWriter
{
    private readonly IPlatformOutputStream _output;
    private bool _disposed;

    internal ConsoleTextWriter(IPlatformOutputStream output)
    {
        _output = output ?? throw new ArgumentNullException();
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

    public override void Write(float value) => Write(value.ToString());

    public override void Write(double value) => Write(value.ToString());

    public override void Write(decimal value) => Write(value.ToString());

    public override void WriteLine(int value) =>
        Write(value.ToString(FormatProvider) + NewLine);

    public override void WriteLine(string? value) => Write((value ?? string.Empty) + NewLine);

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _output.Dispose();
        }

        base.Dispose(disposing);
    }

    private void WriteBytes(byte[] contents)
    {
        if (contents.Length == 0)
        {
            return;
        }

        _output.WriteBlocking(contents);
    }
}
