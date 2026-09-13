using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal sealed record TimeZoneCatalog(
    string DataVersion,
    ImmutableDictionary<string, TimeZoneDefinition> Definitions,
    ImmutableDictionary<string, string> Aliases);
