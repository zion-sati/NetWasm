// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneTransitionResolver : ITimeZoneTransitionResolver
    {
        private const long UnixEpochTicks = 621355968000000000;
        private const long TicksPerSecond = 10_000_000;
        private const int MaxOffsetSeconds = 24 * 60 * 60;

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
                : ResolveLocal(
                    zone,
                    seconds,
                    basis == LocalTimeBasis.LocalAmbiguousDaylight);
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
            var offset = selected < 0
                ? zone.InitialOffsetSeconds
                : transitions[selected].OffsetSeconds;
            var daylight = selected < 0
                ? zone.InitialDaylightSavingTime
                : transitions[selected].IsDaylightSavingTime;
            var next = selected + 1;
            var ambiguousDaylight = daylight &&
                next < transitions.Length &&
                transitions[next].OffsetSeconds < offset &&
                seconds >= transitions[next].UnixSeconds -
                    (offset - transitions[next].OffsetSeconds);
            return Create(offset, daylight, ambiguousDaylight);
        }

        private static LocalTimeOffset ResolveLocal(
            TimeZoneDefinition zone,
            long localSeconds,
            bool preferDaylight)
        {
            var candidateFound = false;
            var candidateOffset = zone.InitialOffsetSeconds;
            var candidateDaylight = zone.InitialDaylightSavingTime;
            var transitions = zone.Transitions;
            var first = FindFirstRelevantTransition(transitions, localSeconds);
            var periodStart = first == 0
                ? long.MinValue
                : transitions[first - 1].UnixSeconds;
            var periodOffset = first == 0
                ? zone.InitialOffsetSeconds
                : transitions[first - 1].OffsetSeconds;
            var periodDaylight = first == 0
                ? zone.InitialDaylightSavingTime
                : transitions[first - 1].IsDaylightSavingTime;
            for (var index = first; index <= transitions.Length; index++)
            {
                var periodEnd = index < transitions.Length
                    ? transitions[index].UnixSeconds
                    : long.MaxValue;
                var candidateUtc = AddSaturating(localSeconds, -periodOffset);
                if (candidateUtc >= periodStart && candidateUtc < periodEnd &&
                    (!candidateFound ||
                     (preferDaylight && !candidateDaylight && periodDaylight) ||
                     (!preferDaylight && candidateDaylight && !periodDaylight)))
                {
                    candidateFound = true;
                    candidateOffset = periodOffset;
                    candidateDaylight = periodDaylight;
                }

                if (index == transitions.Length)
                {
                    break;
                }

                var transition = transitions[index];
                if (transition.OffsetSeconds > periodOffset &&
                    localSeconds >= AddSaturating(transition.UnixSeconds, periodOffset) &&
                    localSeconds < AddSaturating(
                        transition.UnixSeconds,
                        transition.OffsetSeconds))
                {
                    return !periodDaylight
                        ? Create(periodOffset, false)
                        : Create(
                            transition.OffsetSeconds,
                            transition.IsDaylightSavingTime);
                }
                periodStart = transition.UnixSeconds;
                periodOffset = transition.OffsetSeconds;
                periodDaylight = transition.IsDaylightSavingTime;

                if (periodStart > AddSaturating(localSeconds, MaxOffsetSeconds))
                {
                    break;
                }
            }
            return Create(candidateOffset, candidateDaylight);
        }

        internal static bool IsInvalidLocalTime(
            TimeZoneDefinition zone,
            long localTicks)
        {
            var localSeconds = ToUnixSeconds(localTicks);
            var transitions = zone.Transitions;
            var first = FindFirstRelevantTransition(transitions, localSeconds);
            var previousOffset = first == 0
                ? zone.InitialOffsetSeconds
                : transitions[first - 1].OffsetSeconds;
            for (var index = first; index < transitions.Length; index++)
            {
                var transition = transitions[index];
                if (transition.UnixSeconds > AddSaturating(localSeconds, MaxOffsetSeconds))
                {
                    break;
                }
                if (transition.OffsetSeconds > previousOffset &&
                    localSeconds >= AddSaturating(transition.UnixSeconds, previousOffset) &&
                    localSeconds < AddSaturating(
                        transition.UnixSeconds,
                        transition.OffsetSeconds))
                {
                    return true;
                }
                previousOffset = transition.OffsetSeconds;
            }
            return false;
        }

        internal static bool TryGetAmbiguousOffsets(
            TimeZoneDefinition zone,
            long localTicks,
            out int firstOffsetSeconds,
            out int secondOffsetSeconds)
        {
            var localSeconds = ToUnixSeconds(localTicks);
            var transitions = zone.Transitions;
            var first = FindFirstRelevantTransition(transitions, localSeconds);
            var previousOffset = first == 0
                ? zone.InitialOffsetSeconds
                : transitions[first - 1].OffsetSeconds;
            for (var index = first; index < transitions.Length; index++)
            {
                var transition = transitions[index];
                if (transition.UnixSeconds > AddSaturating(localSeconds, MaxOffsetSeconds))
                {
                    break;
                }
                if (transition.OffsetSeconds < previousOffset &&
                    localSeconds >= AddSaturating(
                        transition.UnixSeconds,
                        transition.OffsetSeconds) &&
                    localSeconds < AddSaturating(transition.UnixSeconds, previousOffset))
                {
                    firstOffsetSeconds = previousOffset;
                    secondOffsetSeconds = transition.OffsetSeconds;
                    return true;
                }
                previousOffset = transition.OffsetSeconds;
            }
            firstOffsetSeconds = 0;
            secondOffsetSeconds = 0;
            return false;
        }

        private static int FindFirstRelevantTransition(
            TimeZoneTransition[] transitions,
            long localSeconds)
        {
            var target = AddSaturating(localSeconds, -MaxOffsetSeconds);
            var low = 0;
            var high = transitions.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (transitions[middle].UnixSeconds < target)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            return low;
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

        private static LocalTimeOffset Create(
            int seconds,
            bool daylight,
            bool ambiguousDaylight = false) =>
            new((long)seconds * TicksPerSecond, daylight, ambiguousDaylight);
    }
}
