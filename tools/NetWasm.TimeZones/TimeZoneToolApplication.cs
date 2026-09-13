namespace NetWasm.TimeZones;

internal sealed class TimeZoneToolApplication(
    ITimeZoneCommandLineParser arguments,
    ITimeZoneGenerationCommand generation) : ITimeZoneToolApplication
{
    private readonly ITimeZoneCommandLineParser _arguments =
        arguments ?? throw new ArgumentNullException(nameof(arguments));
    private readonly ITimeZoneGenerationCommand _generation =
        generation ?? throw new ArgumentNullException(nameof(generation));

    public int Run(string[] arguments, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        try
        {
            var manifest = _generation.Execute(_arguments.Parse(arguments));
            output.WriteLine(
                $"Timezone asset version={manifest.DataVersion} " +
                $"zones={manifest.Zones.Length} identity={manifest.Identity} " +
                $"bytes={manifest.UncompressedBytes} brotli={manifest.BrotliBytes}");
            return 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or
                UnauthorizedAccessException or InvalidOperationException or
                FormatException)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
    }
}
