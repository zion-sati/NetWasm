using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal sealed class PosixFutureRuleExpander(IPosixRuleParser parser) :
    IPosixFutureRuleExpander
{
    private const long MinimumUnixSeconds = -62_135_596_800;
    private const long MaximumUnixSeconds = 253_402_300_799;
    private readonly IPosixRuleParser _parser =
        parser ?? throw new ArgumentNullException(nameof(parser));

    public ImmutableArray<TimeZoneTransition> Expand(
        string rule,
        long afterUnixSeconds)
    {
        var parsed = _parser.Parse(rule);
        if (parsed is null)
        {
            return [];
        }
        var bounded = Math.Clamp(
            afterUnixSeconds,
            MinimumUnixSeconds,
            MaximumUnixSeconds);
        var firstYear = DateTimeOffset.FromUnixTimeSeconds(bounded).Year;
        var transitions = ImmutableArray.CreateBuilder<TimeZoneTransition>();
        for (var year = firstYear; year <= 9999; year++)
        {
            Add(
                parsed.Start,
                parsed.StandardOffsetSeconds,
                parsed.DaylightOffsetSeconds,
                true,
                year);
            Add(
                parsed.End,
                parsed.DaylightOffsetSeconds,
                parsed.StandardOffsetSeconds,
                false,
                year);
        }
        transitions.Sort(static (left, right) =>
            left.UnixSeconds.CompareTo(right.UnixSeconds));
        return transitions.ToImmutable();

        void Add(
            PosixTransitionRule transition,
            int wallOffset,
            int resultingOffset,
            bool daylight,
            int year)
        {
            DateTime local;
            try
            {
                var date = ResolveDate(transition.Date, year);
                local = date.AddSeconds(transition.Seconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return;
            }
            var basisOffset = transition.Basis switch
            {
                PosixTimeBasis.Utc => 0,
                PosixTimeBasis.Standard => parsed.StandardOffsetSeconds,
                _ => wallOffset,
            };
            var unixSeconds = new DateTimeOffset(local, TimeSpan.Zero)
                .ToUnixTimeSeconds() - basisOffset;
            if (unixSeconds > afterUnixSeconds &&
                unixSeconds <= MaximumUnixSeconds)
            {
                transitions.Add(new(
                    unixSeconds,
                    resultingOffset,
                    daylight));
            }
        }
    }

    private static DateTime ResolveDate(PosixDateRule rule, int year) => rule.Kind switch
    {
        PosixDateRuleKind.JulianWithoutLeapDay =>
            new DateTime(year, 1, 1).AddDays(
                rule.First - 1 +
                (DateTime.IsLeapYear(year) && rule.First >= 60 ? 1 : 0)),
        PosixDateRuleKind.ZeroBasedDayOfYear =>
            new DateTime(year, 1, 1).AddDays(rule.First),
        _ => ResolveMonthWeekDay(rule, year),
    };

    private static DateTime ResolveMonthWeekDay(PosixDateRule rule, int year)
    {
        if (rule.Second == 5)
        {
            var last = new DateTime(
                year,
                rule.First,
                DateTime.DaysInMonth(year, rule.First));
            var difference = ((int)last.DayOfWeek - rule.Third + 7) % 7;
            return last.AddDays(-difference);
        }
        var first = new DateTime(year, rule.First, 1);
        var offset = (rule.Third - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + (rule.Second - 1) * 7);
    }
}
