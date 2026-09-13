using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal sealed class TimeZoneCommandLineParser : ITimeZoneCommandLineParser
{
    public TimeZoneGenerationRequest Parse(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Length == 0 || arguments[0] != "generate")
        {
            throw new ArgumentException("The 'generate' command is required.");
        }
        string? source = null;
        string? asset = null;
        string? brotli = null;
        string? manifest = null;
        string? browserLoader = null;
        var zones = ImmutableArray.CreateBuilder<string>();
        var includeAll = false;
        for (var index = 1; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--source": source = ReadValue(arguments, ref index); break;
                case "--asset": asset = ReadValue(arguments, ref index); break;
                case "--brotli": brotli = ReadValue(arguments, ref index); break;
                case "--manifest": manifest = ReadValue(arguments, ref index); break;
                case "--browser-loader": browserLoader = ReadValue(arguments, ref index); break;
                case "--zone": zones.Add(ReadValue(arguments, ref index)); break;
                case "--all": includeAll = true; break;
                default:
                    throw new ArgumentException(
                        $"Unknown timezone generation option '{arguments[index]}'.");
            }
        }
        return new TimeZoneGenerationRequest(
            Require(source, "--source"),
            Require(asset, "--asset"),
            Require(brotli, "--brotli"),
            Require(manifest, "--manifest"),
            Require(browserLoader, "--browser-loader"),
            zones.ToImmutable(),
            includeAll);
    }

    private static string ReadValue(string[] arguments, ref int index)
    {
        if (++index == arguments.Length || arguments[index].Length == 0)
        {
            throw new ArgumentException("Timezone generation option requires a value.");
        }
        return arguments[index];
    }

    private static string Require(string? value, string option) =>
        value ?? throw new ArgumentException(
            $"Timezone generation requires '{option}'.");
}
