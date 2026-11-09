// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.
// Keep standard validation and lifetime contracts; use the existing reactor timer
// factory instead of native thread-pool queues. Native handle notification is unavailable.
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace System.Threading
{
    public sealed class Timer : MarshalByRefObject, IDisposable, IAsyncDisposable, ITimer
    {
        internal const uint MaxSupportedTimeout = 0xfffffffe;

        private TimerHolder _timer;

        public Timer(TimerCallback callback, object? state, int dueTime, int period)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, -1);
            ArgumentOutOfRangeException.ThrowIfLessThan(period, -1);
            TimerSetup(callback, state, (uint)dueTime, (uint)period);
        }

        public Timer(TimerCallback callback,
                     object? state,
                     TimeSpan dueTime,
                     TimeSpan period)
        {
            var dueTm = (long)dueTime.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTm, -1, nameof(dueTime));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(dueTm, MaxSupportedTimeout, nameof(dueTime));

            var periodTm = (long)period.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(periodTm, -1, nameof(period));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(periodTm, MaxSupportedTimeout, nameof(period));

            TimerSetup(callback, state, (uint)dueTm, (uint)periodTm);
        }

        [CLSCompliant(false)]
        public Timer(TimerCallback callback,
                     object? state,
                     uint dueTime,
                     uint period)
        {
            TimerSetup(callback, state, dueTime, period);
        }

        public Timer(TimerCallback callback,
                     object? state,
                     long dueTime,
                     long period)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, -1);
            ArgumentOutOfRangeException.ThrowIfLessThan(period, -1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(dueTime, MaxSupportedTimeout);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(period, MaxSupportedTimeout);

            TimerSetup(callback, state, (uint)dueTime, (uint)period);
        }

        public Timer(TimerCallback callback)
        {
            const uint DueTime = unchecked((uint)(-1)); // We want timer to be registered, but not activated.  Requires caller to call
            const uint Period = unchecked((uint)(-1));  // Change after a timer instance is created.  This is to avoid the potential
                                                        // for a timer to be fired before the returned value is assigned to the variable,
                                                        // potentially causing the callback to reference a bogus value (if passing the timer to the callback).

            TimerSetup(callback, this, DueTime, Period);
        }

        [MemberNotNull(nameof(_timer))]
        private void TimerSetup(TimerCallback callback,
                                object? state,
                                uint dueTime,
                                uint period)
        {
            ArgumentNullException.ThrowIfNull(callback);

            _timer = new TimerHolder(PlatformServices.TimerFactory.Create(
                callback, state, ToTimeSpan(dueTime), ToTimeSpan(period)));
        }

        public bool Change(int dueTime, int period)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, -1);
            ArgumentOutOfRangeException.ThrowIfLessThan(period, -1);

            return Change((uint)dueTime, (uint)period);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) =>
            Change(GetMilliseconds(dueTime, nameof(dueTime)), GetMilliseconds(period, nameof(period)));

        [CLSCompliant(false)]
        public bool Change(uint dueTime, uint period)
        {
            return _timer.Value.Change(ToTimeSpan(dueTime), ToTimeSpan(period));
        }

        public bool Change(long dueTime, long period)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, -1);
            ArgumentOutOfRangeException.ThrowIfLessThan(period, -1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(dueTime, MaxSupportedTimeout);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(period, MaxSupportedTimeout);

            return Change((uint)dueTime, (uint)period);
        }

        public bool Dispose(WaitHandle notifyObject)
        {
            ArgumentNullException.ThrowIfNull(notifyObject);

            throw new PlatformNotSupportedException("Timer disposal notification through native wait handles is not supported on this NetWasm platform.");
        }

        public void Dispose()
        {
            _timer.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            return _timer.DisposeAsync();
        }

        private static uint GetMilliseconds(TimeSpan time, string parameter)
        {
            var milliseconds = (long)time.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(milliseconds, -1, parameter);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(milliseconds, MaxSupportedTimeout, parameter);
            return (uint)milliseconds;
        }

        private static TimeSpan ToTimeSpan(uint milliseconds) =>
            milliseconds == uint.MaxValue ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(milliseconds);

        // Retain upstream's separate finalizable holder: suppressing finalization
        // on the public Timer must not alter its underlying resource ownership.
        private sealed class TimerHolder
        {
            internal readonly ITimer Value;
            internal TimerHolder(ITimer timer) => Value = timer;
            ~TimerHolder() => Value.Dispose();
            internal void Dispose()
            {
                Value.Dispose();
                GC.SuppressFinalize(this);
            }
            internal ValueTask DisposeAsync()
            {
                var result = Value.DisposeAsync();
                GC.SuppressFinalize(this);
                return result;
            }
        }
    }
}
