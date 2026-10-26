namespace System.Runtime.InteropServices
{
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

    // The host's wake target. The wake export is always present, so it calls
    // through this interface: a program that never constructs a reactor has
    // no implementation to reach, and keeps none of the reactor's state.
    internal interface IHostWakeTarget
    {
        void Wake(uint token);
    }

    internal sealed class WasiPollableReactor :
        IPlatformScheduler,
        IPlatformPollableScheduler,
        IHostWakeTarget
    {
        private sealed class PendingOperation
        {
            internal PendingOperation(Action continuation)
            {
                Continuation = continuation;
            }

            internal Action Continuation { get; }

            // Set once the table has given the operation its token.
            internal WasiPollableRegistration? Registration { get; set; }
        }

        // Pending operations by token. A token's low bits are its slot's index
        // plus one, so a lookup is an index with no hash table in every
        // asynchronous application (issue #128); its high bits count the
        // slot's reuses, so a reused slot gets a token the host has never
        // seen and a stale wake or cancellation finds nothing.
        private sealed class PendingOperations
        {
            private const int SlotBits = 20;
            private const uint SlotMask = (1u << SlotBits) - 1;
            private const int GenerationMask = (1 << (32 - SlotBits)) - 1;

            private PendingOperation?[] _slots = [];
            private int[] _generations = [];
            private int _slotCount;
            private int[] _freeSlots = [];
            private int _freeSlotCount;

            internal uint Add(PendingOperation operation)
            {
                var slot = TakeFreeSlot();
                _slots[slot] = operation;
                return Token(slot);
            }

            // Removes the token's pending operation, or returns null for a
            // token with none, such as one already completed or cancelled.
            internal PendingOperation? Release(uint token)
            {
                var slot = (int)(token & SlotMask) - 1;
                if (slot < 0 || slot >= _slotCount || _slots[slot] is not { } operation || Token(slot) != token)
                {
                    return null;
                }
                _slots[slot] = null;
                _generations[slot] = (_generations[slot] + 1) & GenerationMask;
                _freeSlots[_freeSlotCount++] = slot;
                return operation;
            }

            private uint Token(int slot) => ((uint)_generations[slot] << SlotBits) | (uint)(slot + 1);

            private int TakeFreeSlot()
            {
                if (_freeSlotCount != 0)
                {
                    return _freeSlots[--_freeSlotCount];
                }
                // The free list grows with the slots, so a release never allocates.
                if (_slotCount == _slots.Length)
                {
                    if (_slotCount == SlotMask)
                    {
                        throw new InvalidOperationException("Too many pending pollable operations.");
                    }
                    var capacity = (int)Math.Min(SlotMask, Math.Max(4, _slots.Length * 2));
                    var slots = new PendingOperation?[capacity];
                    var generations = new int[capacity];
                    Array.Copy(_slots, slots, _slotCount);
                    Array.Copy(_generations, generations, _slotCount);
                    _slots = slots;
                    _generations = generations;
                    _freeSlots = new int[capacity];
                }
                return _slotCount++;
            }
        }

        private readonly PendingOperations _pending = new();
        private readonly IPlatformPollableSubscription _pollables;
        private readonly IPlatformReactorWatcher _watcher;
        private readonly IPlatformReactorCancellation _cancellation;

        // The WASI export has no instance receiver. This is a host callback
        // slot registered by the composition root, not service resolution.
        private static IHostWakeTarget? _hostReactor;

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

            var operation = new PendingOperation(continuation);
            var token = _pending.Add(operation);
            var registration = new WasiPollableRegistration(this, token);
            operation.Registration = registration;
            try
            {
                _watcher.Watch(pollable, token);
                return registration;
            }
            catch
            {
                _pending.Release(token);
                registration.Complete();
                throw;
            }
        }

        internal void Cancel(uint token)
        {
            if (_pending.Release(token) is not null)
            {
                _cancellation.Cancel(token);
            }
        }

        [WitExport("netwasm:runtime@1.0.0/reactor-guest", "wake")]
        internal static void Wake(uint token)
        {
            _hostReactor?.Wake(token);
        }

        void IHostWakeTarget.Wake(uint token) => WakeInstance(token);

        internal void WakeInstance(uint token)
        {
            if (_pending.Release(token) is not { } operation)
            {
                return;
            }

            operation.Registration!.Complete();
            operation.Continuation();
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
