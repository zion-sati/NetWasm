namespace NetWasm.TimeZones;

internal interface ITzifParser
{
    TimeZoneDefinition Parse(string name, byte[] contents);
}
