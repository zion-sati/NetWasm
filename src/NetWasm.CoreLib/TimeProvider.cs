// Portions derived from dotnet/runtime at commit
// 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under one or more agreements. The .NET
// Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Threading;

namespace System
{
    public abstract class TimeProvider
    {
        public static TimeProvider System { get; } = new SystemTimeProvider();

        protected TimeProvider()
        {
        }

        public virtual DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;

        private static readonly long s_minDateTicks = DateTime.MinValue.Ticks;
        private static readonly long s_maxDateTicks = DateTime.MaxValue.Ticks;

        public DateTimeOffset GetLocalNow()
        {
            var utcDateTime = GetUtcNow();
            var zoneInfo = LocalTimeZone;
            if (zoneInfo is null)
            {
                throw new InvalidOperationException(
                    "The local time zone cannot be null.");
            }

            var offset = zoneInfo.GetUtcOffset(utcDateTime);
            if (offset.Ticks == 0)
            {
                return utcDateTime;
            }

            var localTicks = utcDateTime.Ticks + offset.Ticks;
            if ((ulong)localTicks > (ulong)s_maxDateTicks)
            {
                localTicks = localTicks < s_minDateTicks
                    ? s_minDateTicks
                    : s_maxDateTicks;
            }

            return new DateTimeOffset(localTicks, offset);
        }

        public virtual TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;

        public virtual long TimestampFrequency => 1_000_000_000L;

        public virtual long GetTimestamp() =>
            unchecked((long)PlatformServices.MonotonicClock.GetMonotonicTime());

        public TimeSpan GetElapsedTime(long startingTimestamp) =>
            GetElapsedTime(startingTimestamp, GetTimestamp());

        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
        {
            var frequency = TimestampFrequency;
            if (frequency <= 0)
            {
                throw new InvalidOperationException(
                    "TimestampFrequency must be positive.");
            }

            return new TimeSpan((long)((endingTimestamp - startingTimestamp) *
                ((double)TimeSpan.TicksPerSecond / frequency)));
        }

        public virtual ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return PlatformServices.TimerFactory.Create(
                callback,
                state,
                dueTime,
                period);
        }

        private sealed class SystemTimeProvider : TimeProvider
        {
        }
    }
}
