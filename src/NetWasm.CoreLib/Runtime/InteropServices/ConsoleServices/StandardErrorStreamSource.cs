namespace System.Runtime.InteropServices.ConsoleServices;

internal sealed class StandardErrorStreamSource : IConsoleOutputStreamSource
{
    private readonly IPlatformStreams _streams;

    internal StandardErrorStreamSource(IPlatformStreams streams)
    {
        _streams = streams ?? throw new ArgumentNullException();
    }

    public IPlatformOutputStream Open() => _streams.GetStandardError();
}
