// From the compiler harness's ReaderWriterLockSlimContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Threading;
namespace ReaderWriterFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        using var rw = new ReaderWriterLockSlim();
        if (rw.RecursionPolicy != LockRecursionPolicy.NoRecursion || rw.CurrentReadCount != 0 || rw.WaitingReadCount != 0 || rw.WaitingWriteCount != 0 || rw.WaitingUpgradeCount != 0) return 1;
        rw.EnterReadLock();
        if (!rw.IsReadLockHeld || rw.CurrentReadCount != 1 || rw.RecursiveReadCount != 1) return 2;
        if (!Recursion(() => rw.EnterReadLock()) || !Recursion(() => rw.TryEnterWriteLock(0)) || !Recursion(() => rw.TryEnterUpgradeableReadLock(0))) return 3;
        try { rw.Dispose(); return 4; } catch (SynchronizationLockException) { }
        rw.ExitReadLock();
        rw.EnterUpgradeableReadLock();
        if (!rw.IsUpgradeableReadLockHeld || rw.CurrentReadCount != 0 || rw.RecursiveUpgradeCount != 1) return 5;
        if (!rw.TryEnterWriteLock(TimeSpan.Zero) || !rw.IsWriteLockHeld || rw.RecursiveWriteCount != 1) return 6;
        if (!Recursion(() => rw.EnterWriteLock()) || !Recursion(() => rw.EnterReadLock()) || !Recursion(() => rw.EnterUpgradeableReadLock())) return 7;
        rw.ExitWriteLock(); rw.EnterReadLock();
        if (rw.CurrentReadCount != 1 || !Recursion(() => rw.EnterWriteLock())) return 8;
        rw.ExitReadLock(); rw.ExitUpgradeableReadLock();
        if (!rw.TryEnterReadLock(0)) return 9; rw.ExitReadLock();
        if (!rw.TryEnterReadLock(TimeSpan.FromTicks(-19999))) return 10; rw.ExitReadLock();
        if (!rw.TryEnterUpgradeableReadLock(TimeSpan.Zero)) return 11; rw.ExitUpgradeableReadLock();
        if (!rw.TryEnterUpgradeableReadLock(0)) return 12; rw.ExitUpgradeableReadLock();
        if (!rw.TryEnterWriteLock(0)) return 13; rw.ExitWriteLock();
        using var recursive = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        recursive.EnterUpgradeableReadLock(); recursive.EnterUpgradeableReadLock();
        recursive.EnterReadLock(); recursive.EnterReadLock();
        recursive.EnterWriteLock(); recursive.EnterWriteLock();
        if (recursive.CurrentReadCount != 1 || recursive.RecursiveReadCount != 2 || recursive.RecursiveUpgradeCount != 2 || recursive.RecursiveWriteCount != 2) return 14;
        recursive.ExitWriteLock(); recursive.ExitWriteLock();
        recursive.ExitReadLock(); recursive.ExitReadLock();
        recursive.ExitUpgradeableReadLock(); recursive.ExitUpgradeableReadLock();
        recursive.EnterWriteLock(); recursive.EnterReadLock(); recursive.EnterUpgradeableReadLock();
        if (!recursive.IsReadLockHeld || !recursive.IsWriteLockHeld || !recursive.IsUpgradeableReadLockHeld) return 15;
        recursive.ExitUpgradeableReadLock(); recursive.ExitReadLock(); recursive.ExitWriteLock();
        recursive.EnterReadLock(); if (!Recursion(() => recursive.EnterWriteLock()) || !Recursion(() => recursive.EnterUpgradeableReadLock())) return 16; recursive.ExitReadLock();
        try { rw.ExitReadLock(); return 17; } catch (SynchronizationLockException) { }
        try { rw.ExitWriteLock(); return 18; } catch (SynchronizationLockException) { }
        try { rw.ExitUpgradeableReadLock(); return 19; } catch (SynchronizationLockException) { }
        using var invalidPolicy = new ReaderWriterLockSlim((LockRecursionPolicy)99);
        if (invalidPolicy.RecursionPolicy != LockRecursionPolicy.NoRecursion) return 20;
        rw.Dispose(); rw.Dispose();
        try { rw.TryEnterReadLock(-2); return 21; } catch (ArgumentOutOfRangeException) { }
        try { rw.TryEnterWriteLock(TimeSpan.FromDays(30)); return 22; } catch (ArgumentOutOfRangeException) { }
        try { rw.EnterReadLock(); return 23; } catch (ObjectDisposedException) { }
        try { rw.EnterWriteLock(); return 24; } catch (ObjectDisposedException) { }
        try { rw.EnterUpgradeableReadLock(); return 25; } catch (ObjectDisposedException) { }
        if (rw.IsReadLockHeld || rw.IsWriteLockHeld || rw.IsUpgradeableReadLockHeld) return 26;
        return input + 1;
    }
    private static bool Recursion(Action action)
    {
        try { action(); return false; }
        catch (LockRecursionException) { return true; }
    }
}
