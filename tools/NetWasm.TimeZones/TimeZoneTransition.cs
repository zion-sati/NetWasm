namespace NetWasm.TimeZones;

internal readonly record struct TimeZoneTransition(
    long UnixSeconds,
    int OffsetSeconds,
    bool IsDaylightSavingTime);
