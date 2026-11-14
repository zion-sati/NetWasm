// From the compiler harness's NativeSynchronizationContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Runtime.CompilerServices;
using System.Threading;
namespace NativeSynchronizationFixture;
public static class EntryPoint
{
    // On one thread a wait that would block throws; where threads run, the case is skipped.
    private static bool Blocks(Func<object?> wait)
    {
        if (RuntimeFeature.IsMultithreadingSupported) return true;
        try { wait(); return false; } catch (PlatformNotSupportedException) { return true; }
    }

    public static int Run(int input)
    {
        var result = Portable(input); if (result != 42) return result;
        using (var auto = new AutoResetEvent(true))
        {
            if (!auto.WaitOne(0) || auto.WaitOne(0)) return 20;
            if (!auto.Set() || !auto.WaitOne() || auto.WaitOne(0)) return 21;
            if (!Blocks(() => auto.WaitOne()) || !Blocks(() => auto.WaitOne(10))) return 22;
            if (!auto.Set() || !auto.WaitOne(TimeSpan.FromSeconds(5))) return 23;
        }
        using (var manual = new ManualResetEvent(false))
        {
            if (manual.WaitOne(0) || !Blocks(() => manual.WaitOne())) return 24;
            manual.Set();
            if (!manual.WaitOne(0) || !manual.WaitOne() || !manual.WaitOne(1000)) return 25;
            if (!manual.Reset() || manual.WaitOne(0)) return 26;
        }
        using (var semaphore = new Semaphore(1, 2))
        {
            if (!semaphore.WaitOne(0) || semaphore.WaitOne(0) || !Blocks(() => semaphore.WaitOne())) return 27;
            if (semaphore.Release() != 0 || semaphore.Release() != 1) return 28;
            try { semaphore.Release(); return 29; } catch (SemaphoreFullException) { }
            if (!semaphore.WaitOne() || !semaphore.WaitOne() || semaphore.WaitOne(0)) return 30;
            try { semaphore.Release(3); return 31; } catch (SemaphoreFullException) { }
        }
        using (var mutex = new Mutex(true))
        {
            if (!mutex.WaitOne(0) || !mutex.WaitOne()) return 32;
            mutex.ReleaseMutex(); mutex.ReleaseMutex(); mutex.ReleaseMutex();
            try { mutex.ReleaseMutex(); return 33; } catch (ApplicationException) { }
            if (!mutex.WaitOne(0)) return 34;
            mutex.ReleaseMutex();
        }
        using (var named = new Mutex(false, "netwasm-wait-fixture", out var createdNew))
        {
            if (!createdNew) return 35;
            using var opened = Mutex.OpenExisting("netwasm-wait-fixture");
            if (!opened.WaitOne(0)) return 36;
            opened.ReleaseMutex();
            if (Mutex.TryOpenExisting("netwasm-wait-fixture-missing", out _)) return 37;
        }
        using (var unset = new ManualResetEvent(false))
        using (var set = new AutoResetEvent(true))
        {
            var handles = new WaitHandle[] { unset, set };
            if (WaitHandle.WaitAny(handles) != 1 || WaitHandle.WaitAny(handles, 0) != WaitHandle.WaitTimeout) return 50;
            if (!Blocks(() => WaitHandle.WaitAny(handles)) || !Blocks(() => WaitHandle.WaitAll(handles))) return 51;
            unset.Set(); set.Set();
            if (!WaitHandle.WaitAll(handles, 0) || WaitHandle.WaitAll(handles, 0)) return 52;
            try { WaitHandle.WaitAll(new WaitHandle[] { unset, unset }, 0); return 53; } catch (DuplicateWaitObjectException) { }
            if (!WaitHandle.SignalAndWait(set, unset) || !set.WaitOne(0)) return 54;
            if (!Blocks(() => WaitHandle.SignalAndWait(unset, set))) return 55;
        }
        try { _ = new EventWaitHandle(false, EventResetMode.AutoReset, "netwasm-wait-event"); return 56; }
        catch (PlatformNotSupportedException) { }
        try { _ = new Semaphore(0, 1, "netwasm-wait-semaphore"); return 57; }
        catch (PlatformNotSupportedException) { }
        var disposed = new ManualResetEvent(true);
        disposed.Dispose();
        try { disposed.WaitOne(0); return 58; } catch (ObjectDisposedException) { }
        try { disposed.Set(); return 59; } catch (ObjectDisposedException) { }
        return input + 1;
    }

    public static int Portable(int input)
    {
        if ((int)EventResetMode.AutoReset != 0 || (int)EventResetMode.ManualReset != 1) return 1;
        try { new EventWaitHandle(false, (EventResetMode)2); return 2; } catch (ArgumentException e) { if (e.ParamName != "mode") return 3; }
        try { new EventWaitHandle(false, (EventResetMode)(-1), "event"); return 4; } catch (ArgumentException e) { if (e.ParamName != "mode") return 5; }
        try { new EventWaitHandle(false, (EventResetMode)99, null, out _); return 6; } catch (ArgumentException) { }
        try { new Semaphore(-1, 0); return 7; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "initialCount") return 8; }
        try { new Semaphore(0, 0, "semaphore"); return 9; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "maximumCount") return 10; }
        try { new Semaphore(2, 1, null, out _); return 11; } catch (ArgumentException e) { if (e is ArgumentOutOfRangeException || e.ParamName != null) return 12; }
        try { Mutex.OpenExisting(null!); return 13; } catch (ArgumentNullException e) { if (e.ParamName != "name") return 14; }
        try { Mutex.OpenExisting(""); return 15; } catch (ArgumentException e) { if (e.ParamName != "name") return 16; }
        try { Mutex.TryOpenExisting(null!, out _); return 17; } catch (ArgumentNullException) { }
        try { Mutex.TryOpenExisting("", out _); return 18; } catch (ArgumentException) { }
        return input + 1;
    }
}
