// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 474ed963699fc29809015392738a15231aaae2de.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace System.Threading
{
    /// <summary>Provides a periodic timer backed by the single NetWasm reactor.</summary>
    public sealed class PeriodicTimer : IDisposable
    {
        private const long MaxSupportedMilliseconds = uint.MaxValue - 1L;

        private readonly ITimer _timer;
        private readonly State _state;
        private TimeSpan _period;

        public PeriodicTimer(TimeSpan period)
            : this(period, TimeProvider.System)
        {
        }

        public PeriodicTimer(TimeSpan period, TimeProvider timeProvider)
        {
            if (!IsValidPeriod(period))
            {
                GC.SuppressFinalize(this);
                throw new ArgumentOutOfRangeException(nameof(period));
            }
            if (timeProvider is null)
            {
                GC.SuppressFinalize(this);
                throw new ArgumentNullException(nameof(timeProvider));
            }

            _period = period;
            _state = new State();
            using (ExecutionContext.SuppressFlow())
            {
                _timer = timeProvider.CreateTimer(
                    static state => ((State)state!).Signal(),
                    _state,
                    period,
                    period);
            }
        }

        public TimeSpan Period
        {
            get => _period;
            set
            {
                if (!IsValidPeriod(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _period = value;
                if (!_timer.Change(value, value))
                {
                    throw new ObjectDisposedException(nameof(PeriodicTimer));
                }
            }
        }

        public ValueTask<bool> WaitForNextTickAsync(
            CancellationToken cancellationToken = default) =>
            _state.WaitForNextTickAsync(this, cancellationToken);

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            _timer.Dispose();
            _state.Signal(stopping: true);
        }

        ~PeriodicTimer() => Dispose();

        private static bool IsValidPeriod(TimeSpan period)
        {
            var milliseconds = (long)period.TotalMilliseconds;
            return (milliseconds >= 1 &&
                    milliseconds <= MaxSupportedMilliseconds) ||
                period == Timeout.InfiniteTimeSpan;
        }

        private sealed class State : IValueTaskSource<bool>
        {
            private PeriodicTimer? _owner;
            private ManualResetValueTaskSourceCore<bool> _source;
            private CancellationTokenRegistration _registration;
            private bool _stopped;
            private bool _signaled;
            private bool _activeWait;

            internal ValueTask<bool> WaitForNextTickAsync(
                PeriodicTimer owner,
                CancellationToken cancellationToken)
            {
                if (_activeWait)
                {
                    throw new InvalidOperationException(
                        "Only one WaitForNextTickAsync operation may be active at a time.");
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    return new ValueTask<bool>(Task<bool>.FromCanceled(cancellationToken));
                }
                if (_signaled)
                {
                    if (!_stopped)
                    {
                        _signaled = false;
                    }
                    return new ValueTask<bool>(!_stopped);
                }

                _owner = owner;
                _activeWait = true;
                _registration = cancellationToken.UnsafeRegister(
                    static (state, token) =>
                        ((State)state!).Signal(cancellationToken: token),
                    this);
                return new ValueTask<bool>(this, _source.Version);
            }

            internal void Signal(
                bool stopping = false,
                CancellationToken cancellationToken = default)
            {
                _stopped |= stopping;
                if (_signaled)
                {
                    return;
                }

                _signaled = true;
                if (!_activeWait)
                {
                    return;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    _source.SetException(new OperationCanceledException(
                        cancellationToken));
                }
                else
                {
                    _source.SetResult(true);
                }
            }

            bool IValueTaskSource<bool>.GetResult(short token)
            {
                _registration.Dispose();
                try
                {
                    _source.GetResult(token);
                }
                finally
                {
                    _source.Reset();
                    _registration = default;
                    _activeWait = false;
                    _owner = null;
                    if (!_stopped)
                    {
                        _signaled = false;
                    }
                }

                return !_stopped;
            }

            ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) =>
                _source.GetStatus(token);

            void IValueTaskSource<bool>.OnCompleted(
                Action<object?> continuation,
                object? state,
                short token,
                ValueTaskSourceOnCompletedFlags flags) =>
                _source.OnCompleted(continuation, state, token, flags);
        }
    }
}
