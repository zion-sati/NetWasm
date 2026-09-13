namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneTransitionResolver : ITimeZoneTransitionResolver
    {
        private const long UnixEpochTicks = 621355968000000000;
        private const long TicksPerSecond = 10_000_000;

        public LocalTimeOffset Resolve(
            TimeZoneDefinition zone,
            long ticks,
            LocalTimeBasis basis)
        {
            if (zone == null)
            {
                throw new ArgumentNullException();
            }
            var seconds = ToUnixSeconds(ticks);
            return basis == LocalTimeBasis.Utc
                ? ResolveUtc(zone, seconds)
                : ResolveLocal(zone, seconds);
        }

        private static LocalTimeOffset ResolveUtc(
            TimeZoneDefinition zone,
            long seconds)
        {
            var transitions = zone.Transitions;
            var low = 0;
            var high = transitions.Length - 1;
            var selected = -1;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                if (transitions[middle].UnixSeconds <= seconds)
                {
                    selected = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            return selected < 0
                ? Create(zone.InitialOffsetSeconds, zone.InitialDaylightSavingTime)
                : Create(
                    transitions[selected].OffsetSeconds,
                    transitions[selected].IsDaylightSavingTime);
        }

        private static LocalTimeOffset ResolveLocal(
            TimeZoneDefinition zone,
            long localSeconds)
        {
            var candidateFound = false;
            var candidateOffset = zone.InitialOffsetSeconds;
            var candidateDaylight = zone.InitialDaylightSavingTime;
            var periodStart = long.MinValue;
            var periodOffset = zone.InitialOffsetSeconds;
            var periodDaylight = zone.InitialDaylightSavingTime;
            foreach (var transition in zone.Transitions)
            {
                SelectCandidate(periodStart, transition.UnixSeconds);
                if (localSeconds >= AddSaturating(transition.UnixSeconds, periodOffset) &&
                    localSeconds < AddSaturating(transition.UnixSeconds, transition.OffsetSeconds))
                {
                    return !periodDaylight
                        ? Create(periodOffset, false)
                        : Create(transition.OffsetSeconds,
                            transition.IsDaylightSavingTime);
                }
                periodStart = transition.UnixSeconds;
                periodOffset = transition.OffsetSeconds;
                periodDaylight = transition.IsDaylightSavingTime;
            }
            SelectCandidate(periodStart, long.MaxValue);
            return Create(candidateOffset, candidateDaylight);

            void SelectCandidate(long utcStart, long utcEnd)
            {
                var localStart = AddSaturating(utcStart, periodOffset);
                var localEnd = AddSaturating(utcEnd, periodOffset);
                if (localSeconds < localStart || localSeconds >= localEnd)
                {
                    return;
                }
                if (!candidateFound || candidateDaylight && !periodDaylight)
                {
                    candidateFound = true;
                    candidateOffset = periodOffset;
                    candidateDaylight = periodDaylight;
                }
            }
        }

        private static long ToUnixSeconds(long ticks)
        {
            var delta = ticks - UnixEpochTicks;
            return delta >= 0
                ? delta / TicksPerSecond
                : -((-delta + TicksPerSecond - 1) / TicksPerSecond);
        }

        private static long AddSaturating(long value, int offset) =>
            offset > 0 && value > long.MaxValue - offset
                ? long.MaxValue
                : offset < 0 && value < long.MinValue - offset
                    ? long.MinValue
                    : value + offset;

        private static LocalTimeOffset Create(int seconds, bool daylight) =>
            new((long)seconds * TicksPerSecond, daylight);
    }
}
