namespace NetWasm.TimeZones;

internal enum PosixDateRuleKind
{
    JulianWithoutLeapDay,
    ZeroBasedDayOfYear,
    MonthWeekDay,
}

internal enum PosixTimeBasis
{
    Wall,
    Standard,
    Utc,
}

internal readonly record struct PosixDateRule(
    PosixDateRuleKind Kind,
    int First,
    int Second,
    int Third);

internal readonly record struct PosixTransitionRule(
    PosixDateRule Date,
    int Seconds,
    PosixTimeBasis Basis);

internal sealed record PosixFutureRule(
    int StandardOffsetSeconds,
    int DaylightOffsetSeconds,
    PosixTransitionRule Start,
    PosixTransitionRule End);
