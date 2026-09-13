using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal sealed record TimeZoneDefinition(
    string Name,
    int InitialOffsetSeconds,
    bool InitialDaylightSavingTime,
    ImmutableArray<TimeZoneTransition> Transitions);
