namespace System.Runtime.InteropServices.ConsoleServices;

internal sealed class StandardOutputStreamSource : IConsoleOutputStreamSource
{
    private readonly IPlatformStreams _streams;

    internal StandardOutputStreamSource(IPlatformStreams streams)
    {
        _streams = streams ?? throw new ArgumentNullException();
    }

    public IPlatformOutputStream Open() => _streams.GetStandardOutput();
}
