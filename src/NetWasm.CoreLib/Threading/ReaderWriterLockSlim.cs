// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Adaptations: preserve ownership/recursion algorithms on one reactor; remove
// native wait queues/backoff and reject contention after releasing the internal lock.
// Invariant resource messages replace SR lookups.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics; // for TraceInformation
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Threading
{
    public enum LockRecursionPolicy
    {
        NoRecursion = 0,
        SupportsRecursion = 1,
    }

    //
    // ReaderWriterCount tracks how many of each kind of lock is held by each thread.
    // We keep a linked list for each thread, attached to a ThreadStatic field.
    // These are reused wherever possible, so that a given thread will only
    // allocate N of these, where N is the maximum number of locks held simultaneously
    // by that thread.
    //
    internal sealed class ReaderWriterCount
    {
        // Which lock does this object belong to?  This is a numeric ID for two reasons:
        // 1) We don't want this field to keep the lock object alive, and a WeakReference would
        //    be too expensive.
        // 2) Setting the value of a long is faster than setting the value of a reference.
        //    The "hot" paths in ReaderWriterLockSlim are short enough that this actually
        //    matters.
        public long lockID;

        // How many reader locks does this thread hold on this ReaderWriterLockSlim instance?
        public int readercount;

        // Ditto for writer/upgrader counts.  These are only used if the lock allows recursion.
        // But we have to have the fields on every ReaderWriterCount instance, because
        // we reuse it for different locks.
        public int writercount;
        public int upgradecount;

        // Next RWC in this thread's list.
        public ReaderWriterCount? next;
    }

    /// <summary>
    /// A reader-writer lock implementation that is intended to be simple, yet very
    /// efficient.  In particular only 1 interlocked operation is taken for any lock
    /// operation (we use spin locks to achieve this).  The spin lock is never held
    /// for more than a few instructions (in particular, we never call event APIs
    /// or in fact any non-trivial API while holding the spin lock).
    /// </summary>
    public class ReaderWriterLockSlim : IDisposable
    {
        // Specifying if the lock can be reacquired recursively.
        private readonly bool _fIsReentrant;

        // Lock specification for _spinLock:  This lock protects exactly the local fields associated with this
        // instance of ReaderWriterLockSlim.  It does NOT protect the memory associated with
        // the events that are raised by this lock (eg writeEvent, readEvent upgradeEvent).
        private SpinLock _spinLock;

        // These variables allow use to avoid Setting events (which is expensive) if we don't have to.

        private int _upgradeLockOwnerId;
        private int _writeLockOwnerId;

        // conditions we wait on.

        // Every lock instance has a unique ID, which is used by ReaderWriterCount to associate itself with the lock
        // without holding a reference to it.
        private static long s_nextLockID;
        private readonly long _lockID;

        // See comments on ReaderWriterCount.
        [ThreadStatic]
        private static ReaderWriterCount? t_rwc;

        private bool _fUpgradeThreadHoldingRead;

        // The uint, that contains info like if the writer lock is held, num of
        // readers etc.
        private uint _owners;

        // Various R/W masks
        // Note:
        // The Uint is divided as follows:
        //
        // Writer-Owned  Waiting-Writers   Waiting Upgraders     Num-Readers
        //    31          30                 29                 28.......0
        //
        // Dividing the uint, allows to vastly simplify logic for checking if a
        // reader should go in etc. Setting the writer bit will automatically
        // make the value of the uint much larger than the max num of readers
        // allowed, thus causing the check for max_readers to fail.

        private const uint WRITER_HELD = 0x80000000;
        private const uint WAITING_WRITERS = 0x40000000;

        // The max readers is actually one less then its theoretical max.
        // This is done in order to prevent reader count overflows. If the reader
        // count reaches max, other readers will wait.
        private const uint MAX_READER = 0x10000000 - 2;

        private const uint READER_MASK = 0x10000000 - 1;

        private bool _fDisposed;

        private void InitializeThreadCounts()
        {
            _upgradeLockOwnerId = -1;
            _writeLockOwnerId = -1;
        }

        public ReaderWriterLockSlim()
            : this(LockRecursionPolicy.NoRecursion)
        {
        }

        public ReaderWriterLockSlim(LockRecursionPolicy recursionPolicy)
        {
            if (recursionPolicy == LockRecursionPolicy.SupportsRecursion)
            {
                _fIsReentrant = true;
            }
            InitializeThreadCounts();
            _lockID = Interlocked.Increment(ref s_nextLockID);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsRWEntryEmpty(ReaderWriterCount rwc)
        {
            if (rwc.lockID == 0)
                return true;
            else if (rwc.readercount == 0 && rwc.writercount == 0 && rwc.upgradecount == 0)
                return true;
            else
                return false;
        }

        private bool IsRwHashEntryChanged(ReaderWriterCount lrwc)
        {
            return lrwc.lockID != _lockID;
        }

        /// <summary>
        /// This routine retrieves/sets the per-thread counts needed to enforce the
        /// various rules related to acquiring the lock.
        ///
        /// DontAllocate is set to true if the caller just wants to get an existing
        /// entry for this thread, but doesn't want to add one if an existing one
        /// could not be found.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReaderWriterCount? GetThreadRWCount(bool dontAllocate)
        {
            ReaderWriterCount? rwc = t_rwc;
            ReaderWriterCount? empty = null;
            while (rwc != null)
            {
                if (rwc.lockID == _lockID)
                    return rwc;

                if (!dontAllocate && empty == null && IsRWEntryEmpty(rwc))
                    empty = rwc;

                rwc = rwc.next;
            }

            if (dontAllocate)
                return null;

            if (empty == null)
            {
                empty = new ReaderWriterCount();
                empty.next = t_rwc;
                t_rwc = empty;
            }

            empty.lockID = _lockID;
            return empty;
        }

        public void EnterReadLock()
        {
            TryEnterReadLock(-1);
        }

        //
        // Common timeout support
        //
        private readonly struct TimeoutTracker
        {
            private readonly int _total;
            private readonly int _start;

            public TimeoutTracker(TimeSpan timeout)
            {
                long ltm = (long)timeout.TotalMilliseconds;
                ArgumentOutOfRangeException.ThrowIfLessThan(ltm, -1, nameof(timeout));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(ltm, int.MaxValue, nameof(timeout));

                _total = (int)ltm;
                if (_total != -1 && _total != 0)
                {
                    _start = Environment.TickCount;
                }
            }

            public TimeoutTracker(int millisecondsTimeout)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
                _total = millisecondsTimeout;
                if (_total != -1 && _total != 0)
                {
                    _start = Environment.TickCount;
                }
            }

            public int RemainingMilliseconds
            {
                get
                {
                    if (_total == -1 || _total == 0)
                        return _total;

                    int elapsed = Environment.TickCount - _start;
                    // elapsed may be negative if TickCount has overflowed by 2^31 milliseconds.
                    if (elapsed < 0 || elapsed >= _total)
                        return 0;

                    return _total - elapsed;
                }
            }

            public bool IsExpired => RemainingMilliseconds == 0;
        }

        public bool TryEnterReadLock(TimeSpan timeout)
        {
            return TryEnterReadLock(new TimeoutTracker(timeout));
        }

        public bool TryEnterReadLock(int millisecondsTimeout)
        {
            return TryEnterReadLock(new TimeoutTracker(millisecondsTimeout));
        }

        private bool TryEnterReadLock(TimeoutTracker timeout)
        {
            return TryEnterReadLockCore(timeout);
        }

        private bool TryEnterReadLockCore(TimeoutTracker timeout)
        {
            ObjectDisposedException.ThrowIf(_fDisposed, this);

            ReaderWriterCount lrwc;
            int id = Environment.CurrentManagedThreadId;

            if (!_fIsReentrant)
            {
                if (id == _writeLockOwnerId)
                {
                    // Check for AW->AR
                    throw new LockRecursionException("A read lock may not be acquired with the write lock held in this mode.");
                }

                _spinLock.Enter(EnterSpinLockReason.EnterAnyRead);

                lrwc = GetThreadRWCount(dontAllocate: false)!;

                // Check if the reader lock is already acquired. Note, we could
                // check the presence of a reader by not allocating rwc (But that
                // would lead to two lookups in the common case. It's better to keep
                // a count in the structure).
                if (lrwc.readercount > 0)
                {
                    _spinLock.Exit();
                    throw new LockRecursionException("Recursive read lock acquisitions not allowed in this mode.");
                }
                else if (id == _upgradeLockOwnerId)
                {
                    // The upgrade lock is already held.
                    // Update the global read counts and exit.

                    lrwc.readercount++;
                    _owners++;
                    _spinLock.Exit();
                    return true;
                }
            }
            else
            {
                _spinLock.Enter(EnterSpinLockReason.EnterAnyRead);
                lrwc = GetThreadRWCount(dontAllocate: false)!;
                if (lrwc.readercount > 0)
                {
                    lrwc.readercount++;
                    _spinLock.Exit();
                    return true;
                }
                else if (id == _upgradeLockOwnerId)
                {
                    // The upgrade lock is already held.
                    // Update the global read counts and exit.
                    lrwc.readercount++;
                    _owners++;
                    _spinLock.Exit();
                    _fUpgradeThreadHoldingRead = true;
                    return true;
                }
                else if (id == _writeLockOwnerId)
                {
                    // The write lock is already held.
                    // Update global read counts here,
                    lrwc.readercount++;
                    _owners++;
                    _spinLock.Exit();
                    return true;
                }
            }

            bool retVal = true;

            while (true)
            {
                // We can enter a read lock if there are only read-locks have been given out
                // and a writer is not trying to get in.

                if (_owners < MAX_READER)
                {
                    // Good case, there is no contention, we are basically done
                    _owners++;       // Indicate we have another reader
                    lrwc.readercount++;
                    break;
                }

                if (timeout.IsExpired)
                {
                    _spinLock.Exit();
                    return false;
                }

                _spinLock.Exit();
                throw new PlatformNotSupportedException("ReaderWriterLockSlim contention waiting is not supported on this NetWasm platform.");
            }

            _spinLock.Exit();
            return retVal;
        }

        public void EnterWriteLock()
        {
            TryEnterWriteLock(-1);
        }

        public bool TryEnterWriteLock(TimeSpan timeout)
        {
            return TryEnterWriteLock(new TimeoutTracker(timeout));
        }

        public bool TryEnterWriteLock(int millisecondsTimeout)
        {
            return TryEnterWriteLock(new TimeoutTracker(millisecondsTimeout));
        }

        private bool TryEnterWriteLock(TimeoutTracker timeout)
        {
            return TryEnterWriteLockCore(timeout);
        }

        private bool TryEnterWriteLockCore(TimeoutTracker timeout)
        {
            ObjectDisposedException.ThrowIf(_fDisposed, this);

            int id = Environment.CurrentManagedThreadId;
            ReaderWriterCount? lrwc;
            bool upgradingToWrite = false;

            if (!_fIsReentrant)
            {
                EnterSpinLockReason enterMyLockReason;
                if (id == _writeLockOwnerId)
                {
                    // Check for AW->AW
                    throw new LockRecursionException("Recursive write lock acquisitions not allowed in this mode.");
                }
                else if (id == _upgradeLockOwnerId)
                {
                    // AU->AW case is allowed once.
                    upgradingToWrite = true;
                    enterMyLockReason = EnterSpinLockReason.UpgradeToWrite;
                }
                else
                {
                    enterMyLockReason = EnterSpinLockReason.EnterWrite;
                }
                _spinLock.Enter(enterMyLockReason);

                lrwc = GetThreadRWCount(dontAllocate: true);

                // Can't acquire write lock with reader lock held.
                if (lrwc != null && lrwc.readercount > 0)
                {
                    _spinLock.Exit();
                    throw new LockRecursionException("Write lock may not be acquired with read lock held. This pattern is prone to deadlocks. Please ensure that read locks are released before taking a write lock. If an upgrade is necessary, use an upgrade lock in place of the read lock.");
                }
            }
            else
            {
                EnterSpinLockReason enterMyLockReason;
                if (id == _writeLockOwnerId)
                {
                    enterMyLockReason = EnterSpinLockReason.EnterRecursiveWrite;
                }
                else if (id == _upgradeLockOwnerId)
                {
                    enterMyLockReason = EnterSpinLockReason.UpgradeToWrite;
                }
                else
                {
                    enterMyLockReason = EnterSpinLockReason.EnterWrite;
                }
                _spinLock.Enter(enterMyLockReason);

                lrwc = GetThreadRWCount(dontAllocate: false)!;

                if (id == _writeLockOwnerId)
                {
                    lrwc.writercount++;
                    _spinLock.Exit();
                    return true;
                }
                else if (id == _upgradeLockOwnerId)
                {
                    upgradingToWrite = true;
                }
                else if (lrwc.readercount > 0)
                {
                    // Write locks may not be acquired if only read locks have been
                    // acquired.
                    _spinLock.Exit();
                    throw new LockRecursionException("Write lock may not be acquired with read lock held. This pattern is prone to deadlocks. Please ensure that read locks are released before taking a write lock. If an upgrade is necessary, use an upgrade lock in place of the read lock.");
                }
            }

            while (true)
            {
                if (IsWriterAcquired())
                {
                    // Good case, there is no contention, we are basically done
                    SetWriterAcquired();
                    break;
                }

                // Check if there is just one upgrader, and no readers.
                // Assumption: Only one thread can have the upgrade lock, so the
                // following check will fail for all other threads that may sneak in
                // when the upgrading thread is waiting.

                if (upgradingToWrite)
                {
                    uint readercount = GetNumReaders();

                    if (readercount == 1)
                    {
                        // Good case again, there is just one upgrader, and no readers.
                        SetWriterAcquired();    // indicate we have a writer.
                        break;
                    }
                    else if (readercount == 2)
                    {
                        if (lrwc != null)
                        {
                            if (IsRwHashEntryChanged(lrwc))
                                lrwc = GetThreadRWCount(dontAllocate: false)!;

                            if (lrwc.readercount > 0)
                            {
                                // This check is needed for EU->ER->EW case, as the owner count will be two.
                                Debug.Assert(_fIsReentrant);
                                Debug.Assert(_fUpgradeThreadHoldingRead);

                                // Good case again, there is just one upgrader, and no readers.
                                SetWriterAcquired();   // indicate we have a writer.
                                break;
                            }
                        }
                    }
                }

                if (timeout.IsExpired)
                {
                    _spinLock.Exit();
                    return false;
                }

                _spinLock.Exit();
                throw new PlatformNotSupportedException("ReaderWriterLockSlim contention waiting is not supported on this NetWasm platform.");
            }

            Debug.Assert((_owners & WRITER_HELD) > 0);

            if (_fIsReentrant)
            {
                Debug.Assert(lrwc != null, "Initialized based on _fIsReentrant earlier in the method");
                if (IsRwHashEntryChanged(lrwc))
                    lrwc = GetThreadRWCount(dontAllocate: false)!;
                lrwc.writercount++;
            }

            _spinLock.Exit();

            _writeLockOwnerId = id;

            return true;
        }

        public void EnterUpgradeableReadLock()
        {
            TryEnterUpgradeableReadLock(-1);
        }

        public bool TryEnterUpgradeableReadLock(TimeSpan timeout)
        {
            return TryEnterUpgradeableReadLock(new TimeoutTracker(timeout));
        }

        public bool TryEnterUpgradeableReadLock(int millisecondsTimeout)
        {
            return TryEnterUpgradeableReadLock(new TimeoutTracker(millisecondsTimeout));
        }

        private bool TryEnterUpgradeableReadLock(TimeoutTracker timeout)
        {
            return TryEnterUpgradeableReadLockCore(timeout);
        }

        private bool TryEnterUpgradeableReadLockCore(TimeoutTracker timeout)
        {
            ObjectDisposedException.ThrowIf(_fDisposed, this);

            int id = Environment.CurrentManagedThreadId;
            ReaderWriterCount? lrwc;

            if (!_fIsReentrant)
            {
                if (id == _upgradeLockOwnerId)
                {
                    // Check for AU->AU
                    throw new LockRecursionException("Recursive upgradeable lock acquisitions not allowed in this mode.");
                }
                else if (id == _writeLockOwnerId)
                {
                    // Check for AU->AW
                    throw new LockRecursionException("Upgradeable lock may not be acquired with write lock held in this mode. Acquiring Upgradeable lock gives the ability to read along with an option to upgrade to a writer.");
                }

                _spinLock.Enter(EnterSpinLockReason.EnterAnyRead);
                lrwc = GetThreadRWCount(dontAllocate: true);
                // Can't acquire upgrade lock with reader lock held.
                if (lrwc != null && lrwc.readercount > 0)
                {
                    _spinLock.Exit();
                    throw new LockRecursionException("Upgradeable lock may not be acquired with read lock held.");
                }
            }
            else
            {
                _spinLock.Enter(EnterSpinLockReason.EnterAnyRead);
                lrwc = GetThreadRWCount(dontAllocate: false)!;

                if (id == _upgradeLockOwnerId)
                {
                    lrwc.upgradecount++;
                    _spinLock.Exit();
                    return true;
                }
                else if (id == _writeLockOwnerId)
                {
                    // Write lock is already held, Just update the global state
                    // to show presence of upgrader.
                    Debug.Assert((_owners & WRITER_HELD) > 0);
                    _owners++;
                    _upgradeLockOwnerId = id;
                    lrwc.upgradecount++;
                    if (lrwc.readercount > 0)
                        _fUpgradeThreadHoldingRead = true;
                    _spinLock.Exit();
                    return true;
                }
                else if (lrwc.readercount > 0)
                {
                    // Upgrade locks may not be acquired if only read locks have been
                    // acquired.
                    _spinLock.Exit();
                    throw new LockRecursionException("Upgradeable lock may not be acquired with read lock held.");
                }
            }

            while (true)
            {
                // Once an upgrade lock is taken, it's like having a reader lock held
                // until upgrade or downgrade operations are performed.

                if ((_upgradeLockOwnerId == -1) && (_owners < MAX_READER))
                {
                    _owners++;
                    _upgradeLockOwnerId = id;
                    break;
                }

                if (timeout.IsExpired)
                {
                    _spinLock.Exit();
                    return false;
                }

                _spinLock.Exit();
                throw new PlatformNotSupportedException("ReaderWriterLockSlim contention waiting is not supported on this NetWasm platform.");
            }

            if (_fIsReentrant)
            {
                // The lock may have been dropped getting here, so make a quick check to see whether some other
                // thread did not grab the entry.
                Debug.Assert(lrwc != null, "Initialized based on _fIsReentrant earlier in the method");
                if (IsRwHashEntryChanged(lrwc))
                    lrwc = GetThreadRWCount(dontAllocate: false)!;
                lrwc.upgradecount++;
            }

            _spinLock.Exit();

            return true;
        }

        public void ExitReadLock()
        {
            _spinLock.Enter(EnterSpinLockReason.ExitAnyRead);

            ReaderWriterCount? lrwc = GetThreadRWCount(dontAllocate: true);

            if (lrwc == null || lrwc.readercount < 1)
            {
                // You have to be holding the read lock to make this call.
                _spinLock.Exit();
                throw new SynchronizationLockException("The read lock is being released without being held.");
            }

            if (_fIsReentrant)
            {
                if (lrwc.readercount > 1)
                {
                    lrwc.readercount--;
                    _spinLock.Exit();
                    return;
                }

                if (Environment.CurrentManagedThreadId == _upgradeLockOwnerId)
                {
                    _fUpgradeThreadHoldingRead = false;
                }
            }

            Debug.Assert(_owners > 0, "ReleasingReaderLock: releasing lock and no read lock taken");

            --_owners;

            Debug.Assert(lrwc.readercount == 1);
            lrwc.readercount--;

            ExitAndWakeUpAppropriateWaiters();
        }

        public void ExitWriteLock()
        {
            ReaderWriterCount lrwc;
            if (!_fIsReentrant)
            {
                if (Environment.CurrentManagedThreadId != _writeLockOwnerId)
                {
                    // You have to be holding the write lock to make this call.
                    throw new SynchronizationLockException("The write lock is being released without being held.");
                }
                _spinLock.Enter(EnterSpinLockReason.ExitAnyWrite);
            }
            else
            {
                _spinLock.Enter(EnterSpinLockReason.ExitAnyWrite);
                lrwc = GetThreadRWCount(dontAllocate: false)!;

                if (lrwc == null)
                {
                    _spinLock.Exit();
                    throw new SynchronizationLockException("The write lock is being released without being held.");
                }

                if (lrwc.writercount < 1)
                {
                    _spinLock.Exit();
                    throw new SynchronizationLockException("The write lock is being released without being held.");
                }

                lrwc.writercount--;

                if (lrwc.writercount > 0)
                {
                    _spinLock.Exit();
                    return;
                }
            }

            Debug.Assert((_owners & WRITER_HELD) > 0, "Calling ReleaseWriterLock when no write lock is held");

            ClearWriterAcquired();

            _writeLockOwnerId = -1;

            ExitAndWakeUpAppropriateWaiters();
        }

        public void ExitUpgradeableReadLock()
        {
            ReaderWriterCount? lrwc;
            if (!_fIsReentrant)
            {
                if (Environment.CurrentManagedThreadId != _upgradeLockOwnerId)
                {
                    // You have to be holding the upgrade lock to make this call.
                    throw new SynchronizationLockException("The upgradeable lock is being released without being held.");
                }
                _spinLock.Enter(EnterSpinLockReason.ExitAnyRead);
            }
            else
            {
                _spinLock.Enter(EnterSpinLockReason.ExitAnyRead);
                lrwc = GetThreadRWCount(dontAllocate: true);

                if (lrwc == null)
                {
                    _spinLock.Exit();
                    throw new SynchronizationLockException("The upgradeable lock is being released without being held.");
                }

                if (lrwc.upgradecount < 1)
                {
                    _spinLock.Exit();
                    throw new SynchronizationLockException("The upgradeable lock is being released without being held.");
                }

                lrwc.upgradecount--;

                if (lrwc.upgradecount > 0)
                {
                    _spinLock.Exit();
                    return;
                }

                _fUpgradeThreadHoldingRead = false;
            }

            _owners--;
            _upgradeLockOwnerId = -1;

            ExitAndWakeUpAppropriateWaiters();
        }

        /// <summary>
        /// A routine for lazily creating a event outside the lock (so if errors
        /// happen they are outside the lock and that we don't do much work
        /// while holding a spin lock).  If all goes well, reenter the lock and
        /// set 'waitEvent'
        /// </summary>

        /// <summary>
        /// Waits on 'waitEvent' with a timeout
        /// Before the wait 'numWaiters' is incremented and is restored before leaving this routine.
        /// </summary>

        /// <summary>
        /// Determines the appropriate events to set, leaves the locks, and sets the events.
        /// </summary>
        private void ExitAndWakeUpAppropriateWaiters() => _spinLock.Exit();

        private bool IsWriterAcquired()
        {
            return (_owners & ~WAITING_WRITERS) == 0;
        }

        private void SetWriterAcquired()
        {
            _owners |= WRITER_HELD;    // indicate we have a writer.
        }

        private void ClearWriterAcquired()
        {
            _owners &= ~WRITER_HELD;
        }

        private uint GetNumReaders()
        {
            return _owners & READER_MASK;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposing && !_fDisposed)
            {
                if (WaitingReadCount > 0 || WaitingUpgradeCount > 0 || WaitingWriteCount > 0)
                    throw new SynchronizationLockException("The lock is being disposed while still being used. It either is being held by a thread and/or has active waiters waiting to acquire the lock.");

                if (IsReadLockHeld || IsUpgradeableReadLockHeld || IsWriteLockHeld)
                    throw new SynchronizationLockException("The lock is being disposed while still being used. It either is being held by a thread and/or has active waiters waiting to acquire the lock.");

                _fDisposed = true;
            }
        }

        public bool IsReadLockHeld
        {
            get
            {
                if (RecursiveReadCount > 0)
                    return true;
                else
                    return false;
            }
        }

        public bool IsUpgradeableReadLockHeld
        {
            get
            {
                if (RecursiveUpgradeCount > 0)
                    return true;
                else
                    return false;
            }
        }

        public bool IsWriteLockHeld
        {
            get
            {
                if (RecursiveWriteCount > 0)
                    return true;
                else
                    return false;
            }
        }

        public LockRecursionPolicy RecursionPolicy
        {
            get
            {
                if (_fIsReentrant)
                {
                    return LockRecursionPolicy.SupportsRecursion;
                }
                else
                {
                    return LockRecursionPolicy.NoRecursion;
                }
            }
        }

        public int CurrentReadCount
        {
            get
            {
                int numreaders = (int)GetNumReaders();

                if (_upgradeLockOwnerId != -1)
                    return numreaders - 1;
                else
                    return numreaders;
            }
        }

        public int RecursiveReadCount
        {
            get
            {
                int count = 0;
                ReaderWriterCount? lrwc = GetThreadRWCount(dontAllocate: true);
                if (lrwc != null)
                    count = lrwc.readercount;

                return count;
            }
        }

        public int RecursiveUpgradeCount
        {
            get
            {
                if (_fIsReentrant)
                {
                    int count = 0;

                    ReaderWriterCount? lrwc = GetThreadRWCount(dontAllocate: true);
                    if (lrwc != null)
                        count = lrwc.upgradecount;

                    return count;
                }
                else
                {
                    if (Environment.CurrentManagedThreadId == _upgradeLockOwnerId)
                        return 1;
                    else
                        return 0;
                }
            }
        }

        public int RecursiveWriteCount
        {
            get
            {
                if (_fIsReentrant)
                {
                    int count = 0;

                    ReaderWriterCount? lrwc = GetThreadRWCount(dontAllocate: true);
                    if (lrwc != null)
                        count = lrwc.writercount;

                    return count;
                }
                else
                {
                    if (Environment.CurrentManagedThreadId == _writeLockOwnerId)
                        return 1;
                    else
                        return 0;
                }
            }
        }

        public int WaitingReadCount => (int)0U;

        public int WaitingUpgradeCount => (int)0U;

        // Include the thread that may be waiting in upgradable mode.
        public int WaitingWriteCount => (int)0U + (int)0U;

        private struct SpinLock
        {
            private bool _isLocked;
            public void Enter(EnterSpinLockReason reason)
            {
                if (_isLocked)
                    throw new PlatformNotSupportedException("ReaderWriterLockSlim internal contention is not supported on this NetWasm platform.");
                _isLocked = true;
            }
            public void Exit() => _isLocked = false;
            public bool IsHeld => _isLocked;
        }

        [Flags]

        private enum EnterSpinLockReason
        {
            EnterAnyRead = 0,
            ExitAnyRead = 1,
            EnterWrite = 2,
            UpgradeToWrite = 3,
            EnterRecursiveWrite = 4,
            ExitAnyWrite = 5,

            OperationMask = 0x7,

            Wait = 0x8
        }

    }
}
