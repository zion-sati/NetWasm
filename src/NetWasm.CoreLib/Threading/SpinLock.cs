// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Retain owner-bit, acquisition, exit and validation behavior on one reactor.
// Contention cannot be resolved by another managed thread, so do not spin/block.
using System.Diagnostics;
namespace System.Threading
{
    public struct SpinLock
    {
        private int _owner;
        private const int LOCK_UNOWNED = 0;
        private const int LOCK_ID_DISABLE_MASK = unchecked((int)0x80000000);
        private const int LOCK_ANONYMOUS_OWNED = 1;
        private const int ID_DISABLED_AND_ANONYMOUS_OWNED = LOCK_ID_DISABLE_MASK | LOCK_ANONYMOUS_OWNED;
        public bool IsThreadOwnerTrackingEnabled => (_owner & LOCK_ID_DISABLE_MASK) == 0;
        private static int CompareExchange(ref int location, int value, int comparand, ref bool success)
        {
            var result = Interlocked.CompareExchange(ref location, value, comparand);
            success = (result == comparand);
            return result;
        }

        public SpinLock(bool enableThreadOwnerTracking)
        {
            _owner = LOCK_UNOWNED;
            if (!enableThreadOwnerTracking)
            {
                _owner |= LOCK_ID_DISABLE_MASK;
                Debug.Assert(!IsThreadOwnerTrackingEnabled, "property should be false by now");
            }
        }

        public void Enter(ref bool lockTaken)
        {
            // Try to keep the code and branching in this method as small as possible in order to inline the method
            var observedOwner = _owner;
            if (lockTaken || // invalid parameter
                (observedOwner & ID_DISABLED_AND_ANONYMOUS_OWNED) != LOCK_ID_DISABLE_MASK || // thread tracking is enabled or the lock is already acquired
                CompareExchange(ref _owner, observedOwner | LOCK_ANONYMOUS_OWNED, observedOwner, ref lockTaken) != observedOwner) // acquiring the lock failed
                ContinueTryEnter(Timeout.Infinite, ref lockTaken); // Then try the slow path if any of the above conditions is met
        }

        public void TryEnter(ref bool lockTaken)
        {
            var observedOwner = _owner;
            if (((observedOwner & LOCK_ID_DISABLE_MASK) == 0) | lockTaken)
            {
                // Thread tracking enabled or invalid arg. Take slow path.
                ContinueTryEnter(0, ref lockTaken);
            }
            else if ((observedOwner & LOCK_ANONYMOUS_OWNED) != 0)
            {
                // Lock already held by someone
                lockTaken = false;
            }
            else
            {
                // Lock wasn't held; try to acquire it.
                CompareExchange(ref _owner, observedOwner | LOCK_ANONYMOUS_OWNED, observedOwner, ref lockTaken);
            }
        }

        public void TryEnter(TimeSpan timeout, ref bool lockTaken)
        {
            // Validate the timeout
            var totalMilliseconds = (long)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout), timeout, "The timeout must be a value between -1 and Int32.MaxValue, inclusive.");
            }

            // Call reliable enter with the int-based timeout milliseconds
            TryEnter((int)timeout.TotalMilliseconds, ref lockTaken);
        }

        public void TryEnter(int millisecondsTimeout, ref bool lockTaken)
        {
            var observedOwner = _owner;
            if (millisecondsTimeout < -1 || // invalid parameter
                lockTaken || // invalid parameter
                (observedOwner & ID_DISABLED_AND_ANONYMOUS_OWNED) != LOCK_ID_DISABLE_MASK ||  // thread tracking is enabled or the lock is already acquired
                CompareExchange(ref _owner, observedOwner | LOCK_ANONYMOUS_OWNED, observedOwner, ref lockTaken) != observedOwner) // acquiring the lock failed
                ContinueTryEnter(millisecondsTimeout, ref lockTaken); // The call the slow pth
        }

        public void Exit()
        {
            // This is the fast path for the thread tracking is disabled, otherwise go to the slow path
            if ((_owner & LOCK_ID_DISABLE_MASK) == 0)
                ExitSlowPath(true);
            else
                Interlocked.Decrement(ref _owner);
        }

        public void Exit(bool useMemoryBarrier)
        {
            // This is the fast path for the thread tracking is disabled and not to use memory barrier, otherwise go to the slow path
            // The reason not to add else statement if the usememorybarrier is that it will add more branching in the code and will prevent
            // method inlining, so this is optimized for useMemoryBarrier=false and Exit() overload optimized for useMemoryBarrier=true.
            var tmpOwner = _owner;
            if ((tmpOwner & LOCK_ID_DISABLE_MASK) != 0 & !useMemoryBarrier)
            {
                _owner = tmpOwner & (~LOCK_ANONYMOUS_OWNED);
            }
            else
            {
                ExitSlowPath(useMemoryBarrier);
            }
        }

        private void ExitSlowPath(bool useMemoryBarrier)
        {
            var threadTrackingEnabled = (_owner & LOCK_ID_DISABLE_MASK) == 0;
            if (threadTrackingEnabled && !IsHeldByCurrentThread)
            {
                throw new SynchronizationLockException("The calling thread does not hold the lock.");
            }

            if (useMemoryBarrier)
            {
                if (threadTrackingEnabled)
                {
                    Interlocked.Exchange(ref _owner, LOCK_UNOWNED);
                }
                else
                {
                    Interlocked.Decrement(ref _owner);
                }
            }
            else
            {
                if (threadTrackingEnabled)
                {
                    _owner = LOCK_UNOWNED;
                }
                else
                {
                    var tmpOwner = _owner;
                    _owner = tmpOwner & (~LOCK_ANONYMOUS_OWNED);
                }
            }
        }

        public bool IsHeld
        {
            get
            {
                if (IsThreadOwnerTrackingEnabled)
                    return _owner != LOCK_UNOWNED;

                return (_owner & LOCK_ANONYMOUS_OWNED) != LOCK_UNOWNED;
            }
        }

        public bool IsHeldByCurrentThread
        {
            get
            {
                if (!IsThreadOwnerTrackingEnabled)
                {
                    throw new InvalidOperationException("Thread tracking is disabled.");
                }
                return (_owner & (~LOCK_ID_DISABLE_MASK)) == Environment.CurrentManagedThreadId;
            }
        }
        private void ContinueTryEnter(int millisecondsTimeout, ref bool lockTaken)
        {
            if (lockTaken)
            {
                lockTaken = false;
                throw new ArgumentException("The tookLock argument must be set to false before calling this method.");
            }
            if (millisecondsTimeout < -1)
                throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout), millisecondsTimeout,
                    "The timeout must be a value between -1 and Int32.MaxValue, inclusive.");
            if (IsThreadOwnerTrackingEnabled)
            {
                var owner = Environment.CurrentManagedThreadId;
                if (_owner == owner)
                    throw new LockRecursionException("The calling thread already holds the lock.");
                if (CompareExchange(ref _owner, owner, LOCK_UNOWNED, ref lockTaken) == LOCK_UNOWNED)
                    return;
            }
            else
            {
                var observedOwner = _owner;
                if ((observedOwner & LOCK_ANONYMOUS_OWNED) == 0 &&
                    CompareExchange(ref _owner, observedOwner | LOCK_ANONYMOUS_OWNED, observedOwner, ref lockTaken) == observedOwner)
                    return;
            }
            if (millisecondsTimeout == 0) return;
            throw new PlatformNotSupportedException("SpinLock contention waiting is not supported on this NetWasm platform.");
        }
    }
}
