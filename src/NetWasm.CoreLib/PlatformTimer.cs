namespace System.Runtime.InteropServices
{
    using Threading;
    using Threading.Tasks;

    internal interface IPlatformTimerFactory
    {
        ITimer Create(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period);
    }

    internal sealed class PlatformTimerFactory : IPlatformTimerFactory
    {
        private readonly IPlatformScheduler _scheduler;

        internal PlatformTimerFactory(IPlatformScheduler scheduler) =>
            _scheduler = scheduler ?? throw new ArgumentNullException();

        public ITimer Create(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) =>
            new PlatformTimer(_scheduler, callback, state, dueTime, period);
    }

    internal sealed class PlatformTimer : ITimer
    {
        private const long InfiniteTicks = -TimeSpan.TicksPerMillisecond;
        private const long MaxMilliseconds = uint.MaxValue - 1L;

        private readonly IPlatformScheduler _scheduler;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private readonly Threading.ExecutionContext? _executionContext;
        private TimeSpan _period;
        private IPlatformSchedule? _schedule;
        private bool _disposed;

        internal PlatformTimer(
            IPlatformScheduler scheduler,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException();
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _state = state;
            _executionContext = Threading.ExecutionContext.Capture();
            Validate(dueTime, nameof(dueTime));
            Validate(period, nameof(period));
            _period = period;
            Schedule(dueTime);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Validate(dueTime, nameof(dueTime));
            Validate(period, nameof(period));
            if (_disposed)
            {
                return false;
            }

            _schedule?.Dispose();
            _schedule = null;
            _period = period;
            Schedule(dueTime);
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _schedule?.Dispose();
            _schedule = null;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        private void Schedule(TimeSpan dueTime)
        {
            if (dueTime.Ticks == InfiniteTicks)
            {
                return;
            }

            _schedule = _scheduler.Schedule(
                Fire,
                checked((ulong)ToMilliseconds(dueTime) * 1_000_000UL));
        }

        private void Fire()
        {
            _schedule = null;
            if (_disposed)
            {
                return;
            }

            if (_executionContext is null)
            {
                _callback(_state);
            }
            else
            {
                Threading.ExecutionContext.Run(
                    _executionContext,
                    static value =>
                    {
                        var invocation = ((TimerCallback, object?))value!;
                        invocation.Item1(invocation.Item2);
                    },
                    (_callback, _state));
            }

            if (!_disposed && _period.Ticks > 0)
            {
                Schedule(_period);
            }
        }

        private static void Validate(TimeSpan value, string parameterName)
        {
            var milliseconds = value.Ticks / TimeSpan.TicksPerMillisecond;
            if (value.Ticks < InfiniteTicks || milliseconds > MaxMilliseconds)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static long ToMilliseconds(TimeSpan value) =>
            value.Ticks / TimeSpan.TicksPerMillisecond;
    }
}
