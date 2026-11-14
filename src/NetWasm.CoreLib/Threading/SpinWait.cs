// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// A single reactor cannot yield to another managed thread synchronously. Preserve
// argument validation and immediate predicate checks, and reject actual spinning.
namespace System.Threading;

public struct SpinWait
{
    private int _count;
    public int Count => _count;
    public bool NextSpinWillYield => true;
    public void Reset() => _count = 0;
    public void SpinOnce() => throw new PlatformNotSupportedException(
        "SpinWait native yielding is not supported on this NetWasm platform.");
    public void SpinOnce(int sleep1Threshold)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sleep1Threshold, -1);
        SpinOnce();
    }
    public static void SpinUntil(Func<bool> condition) => SpinUntil(condition, Timeout.Infinite);
    public static bool SpinUntil(Func<bool> condition, TimeSpan timeout)
    {
        var totalMilliseconds = (long)timeout.TotalMilliseconds;
        if (totalMilliseconds < -1 || totalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The timeout must represent a value between -1 and Int32.MaxValue, inclusive.");
        return SpinUntil(condition, (int)totalMilliseconds);
    }
    public static bool SpinUntil(Func<bool> condition, int millisecondsTimeout)
    {
        if (millisecondsTimeout < Timeout.Infinite)
            throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout), millisecondsTimeout, "The timeout must represent a value between -1 and Int32.MaxValue, inclusive.");
        ArgumentNullException.ThrowIfNull(condition);
        if (condition()) return true;
        if (millisecondsTimeout == 0) return false;
        throw new PlatformNotSupportedException(
            "SpinWait native yielding is not supported on this NetWasm platform.");
    }
}
