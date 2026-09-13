namespace System.Runtime.InteropServices.ConsoleServices;

internal static class ConsoleCompositionRoot
{
    internal static IO.TextWriter CreateStandardOutputWriter() =>
        CreateWriter(new StandardOutputStreamSource(PlatformServices.Streams));

    internal static IO.TextWriter CreateStandardErrorWriter() =>
        CreateWriter(new StandardErrorStreamSource(PlatformServices.Streams));

    private static IO.TextWriter CreateWriter(IConsoleOutputStreamSource streamSource) =>
        new ConsoleTextWriter(
            new ConsoleIntegerLineFormatter(),
            new ConsoleStringLineFormatter(),
            streamSource);
}
