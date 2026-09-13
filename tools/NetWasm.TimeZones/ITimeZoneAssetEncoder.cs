using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal interface ITimeZoneAssetEncoder
{
    TimeZoneAssetArtifact Encode(
        string dataVersion,
        ImmutableArray<TimeZoneDefinition> zones);
}
