using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal interface IPosixFutureRuleExpander
{
    ImmutableArray<TimeZoneTransition> Expand(
        string rule,
        long afterUnixSeconds);
}
