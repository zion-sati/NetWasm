using System.IO;
using System.Text;

namespace System.Runtime.InteropServices.ConsoleServices;

// Floating-point and decimal values are written with a null provider. FormatProvider is always
// the current culture here, and the no-provider form writes the same text, so console output does
// not make the provider-driven formatter reachable.
internal abstract class ConsoleTextWriter : TextWriter
{

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

    private void WriteBytes(byte[] contents)
    {
        if (contents.Length != 0)
        {
            WriteToStream(contents);
        }
    }

    // Writes to the stream's platform service.
    protected abstract void WriteToStream(ReadOnlySpan<byte> contents);
}

internal sealed class StandardOutputTextWriter(WebAssembly.IStandardStreams streams) : ConsoleTextWriter
{
    private readonly WebAssembly.IStandardStreams _streams = streams ?? throw new ArgumentNullException();

    protected override void WriteToStream(ReadOnlySpan<byte> contents) => _streams.WriteOutput(contents);
}

internal sealed class StandardErrorTextWriter(WebAssembly.IStandardStreams streams) : ConsoleTextWriter
{
    private readonly WebAssembly.IStandardStreams _streams = streams ?? throw new ArgumentNullException();

    protected override void WriteToStream(ReadOnlySpan<byte> contents) => _streams.WriteError(contents);
}
