namespace System.Runtime.InteropServices
{
    using Collections.Generic;
    using WebAssembly;

    internal sealed class WasiPollableRegistration : IPlatformSchedule
    {
        private readonly WasiPollableReactor _reactor;
        private readonly uint _token;
        private bool _active = true;

        internal WasiPollableRegistration(WasiPollableReactor reactor, uint token)
        {
            _reactor = reactor ?? throw new ArgumentNullException();
            _token = token;
        }

        public void Dispose()
        {
            if (_active)
            {
                _active = false;
                _reactor.Cancel(_token);
            }
        }

        internal void Complete() => _active = false;
    }

    internal sealed class WasiPollableReactor :
        IPlatformScheduler,
        IPlatformPollableScheduler
    {
        private sealed class PendingOperation
        {
            internal PendingOperation(Action continuation, WasiPollableRegistration registration)
            {
                Continuation = continuation;
                Registration = registration;
            }

            internal Action Continuation { get; }
            internal WasiPollableRegistration Registration { get; }
        }

        private readonly Dictionary<uint, PendingOperation> _pending = new();
        private readonly IPlatformPollableSubscription _pollables;
        private readonly IPlatformReactorWatcher _watcher;
        private readonly IPlatformReactorCancellation _cancellation;
        private uint _nextToken;

        // The WASI export has no instance receiver. This is a host callback
        // slot registered by the composition root, not service resolution.
        private static WasiPollableReactor? _hostReactor;

        internal WasiPollableReactor(
            IPlatformPollableSubscription pollables,
            IPlatformReactorWatcher watcher,
            IPlatformReactorCancellation cancellation)
        {
            _pollables = pollables ?? throw new ArgumentNullException();
            _watcher = watcher ?? throw new ArgumentNullException();
            _cancellation = cancellation ?? throw new ArgumentNullException();
        }

        internal void RegisterForHostWakeups() => _hostReactor = this;

        public IPlatformSchedule Schedule(Action continuation, ulong delayNanoseconds)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException();
            }

            var pollable = _pollables.SubscribeDuration(
                unchecked((long)delayNanoseconds));
            return SchedulePollable(continuation, pollable);
        }

        public IPlatformSchedule SchedulePollable(Action continuation, int pollable)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException();
            }

            if (pollable < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pollable));
            }

            var token = NextToken();
            var registration = new WasiPollableRegistration(this, token);
            _pending.Add(token, new PendingOperation(continuation, registration));
            try
            {
                _watcher.Watch(pollable, token);
                return registration;
            }
            catch
            {
                _pending.Remove(token);
                registration.Complete();
                throw;
            }
        }

        internal void Cancel(uint token)
        {
            if (_pending.Remove(token))
            {
                _cancellation.Cancel(token);
            }
        }

        [WitExport("netwasm:runtime@1.0.0/reactor-guest", "wake")]
        internal static void Wake(uint token)
        {
            _hostReactor?.WakeInstance(token);
        }

        internal void WakeInstance(uint token)
        {
            if (!_pending.TryGetValue(token, out var operation))
            {
                return;
            }

            _pending.Remove(token);
            operation.Registration.Complete();
            operation.Continuation();
        }

        private uint NextToken()
        {
            do
            {
                _nextToken++;
            }
            while (_nextToken == 0 || _pending.ContainsKey(_nextToken));

            return _nextToken;
        }
    }

    internal sealed class WasiPollableSubscription : IPlatformPollableSubscription
    {
        public int SubscribeDuration(long durationNanoseconds) =>
            WasiClockImports.SubscribeDuration(durationNanoseconds);
    }

    internal sealed class WasiReactorWatcher : IPlatformReactorWatcher
    {
        public void Watch(int pollable, uint token) =>
            ReactorHostImports.Watch(pollable, token);
    }

    internal sealed class WasiReactorCancellation : IPlatformReactorCancellation
    {
        public void Cancel(uint token) => ReactorHostImports.Cancel(token);
    }

    internal static class ReactorHostImports
    {
        [WitImport("netwasm:runtime@1.0.0/reactor-host", "watch")]
        internal static extern void Watch(int pollable, uint token);

        [WitImport("netwasm:runtime@1.0.0/reactor-host", "cancel")]
        internal static extern void Cancel(uint token);
    }
}
