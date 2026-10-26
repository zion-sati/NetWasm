// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from System.Private.CoreLib Lock for a single managed reactor.

namespace System.Threading;

/// <summary>
/// A mutual-exclusion lock. C# lowers <c>lock</c> on a <see cref="Lock"/> to
/// <see cref="EnterScope"/>. NetWasm has one managed execution reactor, so the
/// lock is always held by that reactor and every repeated entry is reentrant,
/// as with <see cref="Monitor"/>.
/// </summary>
public sealed class Lock
{
    private uint _recursionCount;

    public bool IsHeldByCurrentThread => _recursionCount != 0;

    public void Enter() => Acquire();

    public bool TryEnter()
    {
        Acquire();
        return true;
    }

    public bool TryEnter(int millisecondsTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, Timeout.Infinite);
        Acquire();
        return true;
    }

    public bool TryEnter(TimeSpan timeout)
    {
        var milliseconds = (long)timeout.TotalMilliseconds;
        if (milliseconds < Timeout.Infinite || milliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        Acquire();
        return true;
    }

    public void Exit()
    {
        if (_recursionCount == 0)
            throw new SynchronizationLockException();
        _recursionCount--;
    }

    public Scope EnterScope()
    {
        Acquire();
        return new Scope(this);
    }

    private void Acquire()
    {
        if (_recursionCount == uint.MaxValue)
            throw new LockRecursionException();
        _recursionCount++;
    }

    /// <summary>Exits the lock when disposed; returned by <see cref="EnterScope"/>.</summary>
    public ref struct Scope
    {
        private Lock? _lockObj;

        internal Scope(Lock lockObj) => _lockObj = lockObj;

        public void Dispose()
        {
            var lockObj = _lockObj;
            if (lockObj is null)
                return;
            _lockObj = null;
            lockObj.Exit();
        }
    }
}
