// From the compiler harness's ContextContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Threading;
using System.Runtime.Serialization;
#pragma warning disable SYSLIB0050, SYSLIB0051
namespace ContextFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var result = Portable(input);
        if (result != 42) return result;
        // Wait and WaitHelper are upstream's: a wait on the handles, ignoring the context.
        var context = new SynchronizationContext();
        using var signaled = new ManualResetEvent(true);
        using var pending = new ManualResetEvent(false);
        var one = signaled.SafeWaitHandle.DangerousGetHandle();
        var two = pending.SafeWaitHandle.DangerousGetHandle();
        if (context.Wait(new[] { one }, false, 0) != 0) return 20;
        if (context.Wait(new[] { two, one }, false, 0) != 1) return 21;
        if (Derived.WaitBase(new[] { two, one }, true, 0) != WaitHandle.WaitTimeout) return 22;
        pending.Set();
        if (Derived.WaitBase(new[] { two, one }, true, -1) != 0) return 23;
        return input + 1;
    }
    public static int Portable(int input)
    {
        var context = new SynchronizationContext();
        try { context.Wait(null!, false, 0); return 1; }
        catch (ArgumentNullException ex) { if (ex.ParamName != "waitHandles") return 2; }
        try { Derived.WaitBase(null!, true, -1); return 3; }
        catch (ArgumentNullException ex) { if (ex.ParamName != "waitHandles") return 4; }
        var derived = new Derived();
        SynchronizationContext polymorphic = derived;
        if (polymorphic.Wait(null!, true, 7) != 42 || derived.Calls != 1) return 5;
        derived.RequireNotification();
        if (!derived.IsWaitNotificationRequired() || context.IsWaitNotificationRequired()) return 6;
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(derived);
            var captured = ExecutionContext.Capture()!;
            if (captured is not ISerializable) return 7;
            try { captured.GetObjectData(null!, default); return 8; }
            catch (PlatformNotSupportedException) { }
            try { ((ISerializable)captured).GetObjectData(null!, default); return 9; }
            catch (PlatformNotSupportedException) { }
            var local = new AsyncLocal<int>(); local.Value = 7;
            captured = ExecutionContext.Capture()!; local.Value = 9;
            var seen = 0;
            ExecutionContext.Run(captured, _ => { seen = local.Value; local.Value = 11; SynchronizationContext.SetSynchronizationContext(null); }, null);
            if (seen != 7 || local.Value != 9 || !ReferenceEquals(SynchronizationContext.Current, derived)) return 10;
            var sent = 0; context.Send(value => sent = (int)value!, 42);
            if (sent != 42) return 11;
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        return input + 1;
    }
}
public sealed class Derived : SynchronizationContext
{
    public int Calls;
    public override int Wait(IntPtr[] handles, bool all, int timeout) { Calls++; return handles == null && all && timeout == 7 ? 42 : 0; }
    public void RequireNotification() => SetWaitNotificationRequired();
    public static int WaitBase(IntPtr[] handles, bool all, int timeout) => WaitHelper(handles, all, timeout);
}
