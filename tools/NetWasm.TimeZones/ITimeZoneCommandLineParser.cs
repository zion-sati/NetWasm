namespace NetWasm.TimeZones;

internal interface ITimeZoneCommandLineParser
{
    TimeZoneGenerationRequest Parse(string[] arguments);
}
