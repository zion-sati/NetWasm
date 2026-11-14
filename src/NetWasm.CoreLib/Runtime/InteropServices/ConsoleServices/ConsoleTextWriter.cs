using System.IO;
using System.Text;
using System.Text.Unicode;

namespace System.Runtime.InteropServices.ConsoleServices;

// Numbers are formatted with a null provider. FormatProvider is always the current culture here,
// and NumberFormatInfo.CurrentInfo mirrors that culture's NumberFormatInfo (see
// CultureInfo.OnCurrentCultureChanged), so the text is the same as TextWriter's
// value.ToString(FormatProvider) without making CultureInfo reachable from console output.
// Text is encoded as Encoding.UTF8 encodes it, an unpaired surrogate becoming U+FFFD, without
// constructing an Encoding: at most three bytes for each UTF-16 code unit, and a long text in
// pieces of whole characters.
internal abstract class ConsoleTextWriter : TextWriter
{
    private const int MaximumCharsPerWrite = 4096;

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
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var buffer = new byte[Math.Min(value.Length, MaximumCharsPerWrite) * 3];
        for (var offset = 0; offset < value.Length;)
        {
            Utf8.FromUtf16(value.AsSpan(offset), buffer, out var charsRead, out var bytesWritten);
            WriteToStream(buffer.AsSpan(0, bytesWritten));
            offset += charsRead;
        }
    }

    public override void Write(int value) => Write(value.ToString());

    public override void Write(uint value) => Write(value.ToString());

    public override void Write(long value) => Write(value.ToString());

    public override void Write(ulong value) => Write(value.ToString());

    public override void Write(float value) => Write(value.ToString());

    public override void Write(double value) => Write(value.ToString());

    public override void Write(decimal value) => Write(value.ToString());

    public override void WriteLine(int value) =>
        Write(value.ToString() + NewLine);

    public override void WriteLine(string? value) => Write((value ?? string.Empty) + NewLine);

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
