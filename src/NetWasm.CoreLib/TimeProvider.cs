// Portions derived from dotnet/runtime at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
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
