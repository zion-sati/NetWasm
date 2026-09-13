// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using System.Runtime.InteropServices;

namespace System.Threading
{
    /// <summary>Provides a periodic timer backed by the single NetWasm reactor.</summary>
    public sealed class PeriodicTimer : IDisposable
    {
        private const long MaxSupportedMilliseconds = uint.MaxValue - 1L;

        private readonly State _state;
        private readonly TimeProvider _timeProvider;
        private TimeSpan _period;

        public PeriodicTimer(TimeSpan period)
            : this(period, TimeProvider.System)
        {
        }

        public PeriodicTimer(TimeSpan period, TimeProvider timeProvider)
        {
            ValidatePeriod(period, out _);
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _period = period;
            _state = new State(this);
            _state.Schedule();
        }

        public TimeSpan Period
        {
            get => _period;
            set
            {
                ValidatePeriod(value, out _);
                _state.ChangePeriod(value);
                _period = value;
            }
        }

        public ValueTask<bool> WaitForNextTickAsync(
            CancellationToken cancellationToken = default(System.Threading.CancellationToken)) =>
            _state.WaitForNextTickAsync(cancellationToken);

        public void Dispose() => _state.Stop();

        private static void ValidatePeriod(TimeSpan period, out ulong milliseconds)
        {
            var value = period.Ticks / TimeSpan.TicksPerMillisecond;
            if (period.Ticks < TimeSpan.TicksPerMillisecond || value < 1 || value > MaxSupportedMilliseconds)
            {
                throw new ArgumentOutOfRangeException(nameof(period));
            }

            milliseconds = (ulong)value;
        }

        private sealed class State : IValueTaskSource<bool>
        {
            private readonly PeriodicTimer _owner;
            private ITimer? _schedule;
            private CancellationTokenRegistration _registration;
            private ManualResetValueTaskSourceCore<bool> _source;
            private bool _activeWait;
            private bool _signaled;
            private bool _stopped;
            private bool _cancelledWait;
            private bool _waitCompleted;
            private bool _waitCompletedByTick;
            private CancellationToken _waitCancellationToken;

            internal State(PeriodicTimer owner) => _owner = owner;

            internal void Schedule()
            {
                if (_stopped)
                {
                    return;
                }

                ValidatePeriod(_owner._period, out var milliseconds);
                _schedule = _owner._timeProvider.CreateTimer(
                    _ => SignalTick(),
                    null,
                    TimeSpan.FromMilliseconds((long)milliseconds),
                    TimeSpan.FromMilliseconds(-1));
            }

            internal void ChangePeriod(TimeSpan period)
            {
                if (_stopped)
                {
                    throw new ObjectDisposedException(nameof(PeriodicTimer));
                }

                _schedule?.Dispose();
                _schedule = null;
                _owner._period = period;
                Schedule();
            }

            internal ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken)
            {
                if (_activeWait)
                {
                    throw new InvalidOperationException("Only one WaitForNextTickAsync operation may be active at a time.");
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

                _activeWait = true;
                _cancelledWait = false;
                _waitCompleted = false;
                _waitCompletedByTick = false;
                _waitCancellationToken = cancellationToken;
                _registration = cancellationToken.Register(CancelWait);
                return new ValueTask<bool>(this, _source.Version);
            }

            internal void Stop()
            {
                if (_stopped)
                {
                    return;
                }

                _stopped = true;
                _schedule?.Dispose();
                _schedule = null;
                _signaled = true;
                if (_activeWait && !_waitCompleted)
                {
                    _waitCompleted = true;
                    _source.SetResult(false);
                }
            }

            private void SignalTick()
            {
                _schedule = null;
                if (_stopped)
                {
                    return;
                }

                var wasSignaled = _signaled;
                _signaled = true;
                if (_activeWait && !_waitCompleted && !wasSignaled)
                {
                    _waitCompleted = true;
                    _waitCompletedByTick = true;
                    _source.SetResult(true);
                }
                Schedule();
            }

            private void CancelWait()
            {
                if (!_activeWait || _stopped || _waitCompleted)
                {
                    return;
                }

                _cancelledWait = true;
                _waitCompleted = true;
                _source.SetException(new OperationCanceledException(
                    _waitCancellationToken));
            }

            bool IValueTaskSource<bool>.GetResult(short token)
            {
                _registration.Dispose();
                try
                {
                    _source.GetResult(token);
                    return !_stopped && !_cancelledWait;
                }
                finally
                {
                    _source.Reset();
                    _registration = default;
                    _activeWait = false;
                    _cancelledWait = false;
                    _waitCompleted = false;
                    _waitCancellationToken = default;
                    if (!_stopped && _waitCompletedByTick)
                    {
                        _signaled = false;
                    }
                    _waitCompletedByTick = false;
                }
            }

            ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token) => _source.GetStatus(token);

            void IValueTaskSource<bool>.OnCompleted(
                Action<object?> continuation,
                object? state,
                short token,
                ValueTaskSourceOnCompletedFlags flags) =>
                _source.OnCompleted(continuation, state, token, flags);
        }
    }
}
