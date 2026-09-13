namespace NetWasm.TimeZones;

internal interface ITimeZoneToolApplication
{
    int Run(string[] arguments, TextWriter output, TextWriter error);
}
