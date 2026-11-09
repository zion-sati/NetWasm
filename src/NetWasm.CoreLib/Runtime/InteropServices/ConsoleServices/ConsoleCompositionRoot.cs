using System.IO;
using System.Text;

namespace System.Runtime.InteropServices.ConsoleServices;

internal static class ConsoleCompositionRoot
{
    // Console.ReadBufferSize in System.Console.
    private const int ReadBufferSize = 4096;

    internal static TextWriter CreateStandardOutputWriter() =>
        new StandardOutputTextWriter(PlatformServices.StandardStreams);

    internal static TextWriter CreateStandardErrorWriter() =>
        new StandardErrorTextWriter(PlatformServices.StandardStreams);

    // As System.Console reads input on WASI: the console encoding, with no byte order mark
    // detection.
    internal static TextReader CreateStandardInputReader() =>
        new StreamReader(
            OpenStandardInput(),
            Encoding.Default,
            detectEncodingFromByteOrderMarks: false,
            ReadBufferSize,
            leaveOpen: true);

    internal static Stream OpenStandardInput() => new StandardInputStream(PlatformServices.StandardStreams);

    internal static Stream OpenStandardOutput() => new StandardOutputStream(PlatformServices.StandardStreams);

    internal static Stream OpenStandardError() => new StandardErrorStream(PlatformServices.StandardStreams);
}
