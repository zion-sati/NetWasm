// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from System.Private.CoreLib Monitor for a single managed reactor.

namespace System.Threading;

public static class Monitor
{
    // NetWasm currently has one managed execution reactor. A held monitor is
    // therefore always owned by that reactor and every repeated entry is
    // reentrant. Keep only live acquisitions so released objects are not rooted.
    private static LockState? s_locks;

    public static void Enter(object obj)
    {
        var lockTaken = false;
        Enter(obj, ref lockTaken);
    }

    public static void Enter(object obj, ref bool lockTaken)
    {
        ValidateLockTaken(lockTaken);
        ArgumentNullException.ThrowIfNull(obj);
        Acquire(obj);
        lockTaken = true;
    }

    public static bool TryEnter(object obj)
    {
        var lockTaken = false;
        TryEnter(obj, 0, ref lockTaken);
        return lockTaken;
    }

    public static void TryEnter(object obj, ref bool lockTaken) =>
        TryEnter(obj, 0, ref lockTaken);

    public static bool TryEnter(object obj, int millisecondsTimeout)
    {
        var lockTaken = false;
        TryEnter(obj, millisecondsTimeout, ref lockTaken);
        return lockTaken;
    }

    public static void TryEnter(object obj, int millisecondsTimeout, ref bool lockTaken)
    {
        ValidateLockTaken(lockTaken);
        ArgumentNullException.ThrowIfNull(obj);
        if (millisecondsTimeout < Timeout.Infinite)
            throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout));

        Acquire(obj);
        lockTaken = true;
    }

    public static bool TryEnter(object obj, TimeSpan timeout)
    {
        var lockTaken = false;
        TryEnter(obj, timeout, ref lockTaken);
        return lockTaken;
    }

    public static void TryEnter(object obj, TimeSpan timeout, ref bool lockTaken)
    {
        ValidateLockTaken(lockTaken);
        TryEnter(obj, MillisecondsTimeoutFromTimeSpan(timeout), ref lockTaken);
    }

    public static void Exit(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var state = s_locks;
        if (state is null)
            throw new SynchronizationLockException();

        LockState? previous = null;
        while (state is not null)
        {
            if (!ReferenceEquals(state.Target, obj))
            {
                previous = state;
                state = state.Next;
                continue;
            }

            if (--state.RecursionCount == 0)
            {
                if (previous is null)
                    s_locks = state.Next;
                else
                    previous.Next = state.Next;
                state.Target = null;
                state.Next = null;
            }
            return;
        }

        throw new SynchronizationLockException();
    }

    public static bool IsEntered(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        for (var state = s_locks; state is not null; state = state.Next)
            if (ReferenceEquals(state.Target, obj))
                return true;
        return false;
    }

    // A synchronous waiter requires another managed thread to release or pulse
    // the monitor. The single-reactor profile cannot provide that contract.
    public static bool Wait(object obj) => throw BlockingNotSupported();
    public static bool Wait(object obj, int millisecondsTimeout) => throw BlockingNotSupported();
    public static bool Wait(object obj, TimeSpan timeout) => throw BlockingNotSupported();
    public static bool Wait(object obj, int millisecondsTimeout, bool exitContext) =>
        throw BlockingNotSupported();
    public static bool Wait(object obj, TimeSpan timeout, bool exitContext) =>
        throw BlockingNotSupported();
    public static void Pulse(object obj) => throw BlockingNotSupported();
    public static void PulseAll(object obj) => throw BlockingNotSupported();

    private static void Acquire(object obj)
    {
        for (var state = s_locks; state is not null; state = state.Next)
        {
            if (!ReferenceEquals(state.Target, obj))
                continue;
            state.RecursionCount++;
            return;
        }

        s_locks = new LockState(obj, s_locks);
    }

    private static int MillisecondsTimeoutFromTimeSpan(TimeSpan timeout)
    {
        var milliseconds = (long)timeout.TotalMilliseconds;
        if (milliseconds < Timeout.Infinite || milliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        return (int)milliseconds;
    }

    private static void ValidateLockTaken(bool lockTaken)
    {
        if (lockTaken)
            throw new ArgumentException("The lockTaken argument must be false.", nameof(lockTaken));
    }

    private static PlatformNotSupportedException BlockingNotSupported() => new(
        "Blocking monitor wait and pulse operations require managed threads.");

    private sealed class LockState(object target, LockState? next)
    {
        internal object? Target { get; set; } = target;
        internal LockState? Next { get; set; } = next;
        internal int RecursionCount { get; set; } = 1;
    }
}
