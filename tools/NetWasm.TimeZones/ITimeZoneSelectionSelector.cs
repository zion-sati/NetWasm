using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal interface ITimeZoneSelectionSelector
{
    ImmutableArray<TimeZoneDefinition> Select(
        TimeZoneCatalog catalog,
        IReadOnlyCollection<string> requestedZones,
        bool includeAll);
}
