// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Preserve current-reactor identity and managed helpers. Native thread operations
// are unavailable and must not silently run callbacks on the reactor.
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.ConstrainedExecution;
using System.Security.Principal;
namespace System.Threading;

public sealed partial class Thread : CriticalFinalizerObject
{
    private static Thread? s_current;
    private static AsyncLocal<IPrincipal?>? s_principal;
    private string? _name;
    private Thread(bool currentReactor) { }
    public static Thread CurrentThread => s_current ??= new Thread(currentReactor: true);
    public int ManagedThreadId => Environment.CurrentManagedThreadId;
    public override int GetHashCode() => ManagedThreadId;
    public bool IsAlive => true;
    public bool IsThreadPoolThread => false;
    public ThreadState ThreadState => ThreadState.Running;
    public bool IsBackground { get => false; set => throw Unsupported("background state"); }
    public ThreadPriority Priority { get => throw Unsupported("priority"); set => throw Unsupported("priority"); }
    public string? Name { get => _name; set => _name = value; }
    public CultureInfo CurrentCulture { get => CultureInfo.CurrentCulture; set => CultureInfo.CurrentCulture = value; }
    public CultureInfo CurrentUICulture { get => CultureInfo.CurrentUICulture; set => CultureInfo.CurrentUICulture = value; }
    public ExecutionContext? ExecutionContext => ExecutionContext.Capture();
    public static IPrincipal? CurrentPrincipal
    {
        get
        {
            var principal = s_principal?.Value;
            if (principal is null)
                CurrentPrincipal = principal = AppDomain.CurrentDomain.GetThreadPrincipal();
            return principal;
        }
        set
        {
            if (s_principal is null)
            {
                if (value is null) return;
                s_principal = new AsyncLocal<IPrincipal?>();
            }
            s_principal.Value = value;
        }
    }
    public static AppDomain GetDomain() => AppDomain.CurrentDomain;
    public static int GetDomainID() => 1;
    public static int GetCurrentProcessorId() => 0;
    public Thread(ThreadStart start) { ArgumentNullException.ThrowIfNull(start); throw Unsupported("creation"); }
    public Thread(ParameterizedThreadStart start) { ArgumentNullException.ThrowIfNull(start); throw Unsupported("creation"); }
    public Thread(ThreadStart start, int maxStackSize) { ArgumentNullException.ThrowIfNull(start); ArgumentOutOfRangeException.ThrowIfNegative(maxStackSize); throw Unsupported("creation"); }
    public Thread(ParameterizedThreadStart start, int maxStackSize) { ArgumentNullException.ThrowIfNull(start); ArgumentOutOfRangeException.ThrowIfNegative(maxStackSize); throw Unsupported("creation"); }
    public void Start() => throw Unsupported("start");
    public void Start(object? parameter) => throw Unsupported("start");
    public void Join() => Join(-1);
    public bool Join(TimeSpan timeout) => Join(WaitHandle.ToTimeoutMilliseconds(timeout));
    public bool Join(int millisecondsTimeout) { ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1); throw Unsupported("join"); }
    public static void Sleep(TimeSpan timeout) => Sleep(WaitHandle.ToTimeoutMilliseconds(timeout));
    // NetWasm: a sleep blocks on wasi:clocks/monotonic-clock's wait-for at the platform level, as synchronous I/O
    // does: no spinning (upstream's single-threaded WASI build spins until the time passes) and no event-loop pumping.
    // An infinite sleep throws, since nothing on the one thread can end it; Sleep(0) has no other thread to yield to.
    public static void Sleep(int millisecondsTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
        if (millisecondsTimeout == Timeout.Infinite)
        {
            throw Unsupported("infinite sleep");
        }

        if (millisecondsTimeout > 0)
        {
            System.Runtime.InteropServices.PlatformServices.BlockFor((ulong)millisecondsTimeout * 1_000_000);
        }
    }

    // One thread: no other thread is ever ready to run.
    public static bool Yield() => false;

    // Spinning has no effect another thread could observe, so there is nothing to wait for.
    public static void SpinWait(int iterations)
    {
    }
    public void Interrupt() => throw Unsupported("interrupt");
    public void Abort() => throw Unsupported("abort");
    public void Abort(object? stateInfo) => throw Unsupported("abort");
    public static void ResetAbort() => throw Unsupported("abort reset");
    public void Suspend() => throw Unsupported("suspend");
    public void Resume() => throw Unsupported("resume");
    public void DisableComObjectEagerCleanup() => throw Unsupported("COM cleanup");
    public ApartmentState GetApartmentState() => ApartmentState.Unknown;
    public ApartmentState ApartmentState { get => GetApartmentState(); set => TrySetApartmentState(value); }
    public void SetApartmentState(ApartmentState state) => TrySetApartmentState(state);
    public bool TrySetApartmentState(ApartmentState state)
    {
        if (state != ApartmentState.STA && state != ApartmentState.MTA && state != ApartmentState.Unknown)
            throw new ArgumentOutOfRangeException(nameof(state));
        throw Unsupported("apartment state");
    }
    public CompressedStack GetCompressedStack() => throw Unsupported("compressed stack");
    public void SetCompressedStack(CompressedStack stack) => throw Unsupported("compressed stack");
    public static void BeginCriticalRegion() { }
    public static void EndCriticalRegion() { }
    public static void BeginThreadAffinity() { }
    public static void EndThreadAffinity() { }
    public static void MemoryBarrier() => Interlocked.MemoryBarrier();
    public static LocalDataStoreSlot AllocateDataSlot() => LocalDataStore.AllocateSlot();
    public static LocalDataStoreSlot AllocateNamedDataSlot(string name) => LocalDataStore.AllocateNamedSlot(name);
    public static LocalDataStoreSlot GetNamedDataSlot(string name) => LocalDataStore.GetNamedSlot(name);
    public static void FreeNamedDataSlot(string name) => LocalDataStore.FreeNamedSlot(name);
    public static object? GetData(LocalDataStoreSlot slot) => LocalDataStore.GetData(slot);
    public static void SetData(LocalDataStoreSlot slot, object? data) => LocalDataStore.SetData(slot, data);
    private static PlatformNotSupportedException Unsupported(string operation) => new("Thread " + operation + " is not supported on this NetWasm platform.");
    public static byte VolatileRead(ref byte address) => Volatile.Read(ref address);
    public static void VolatileWrite(ref byte address, byte value) => Volatile.Write(ref address, value);
    [CLSCompliant(false)]
    public static sbyte VolatileRead(ref sbyte address) => Volatile.Read(ref address);
    [CLSCompliant(false)]
    public static void VolatileWrite(ref sbyte address, sbyte value) => Volatile.Write(ref address, value);
    public static short VolatileRead(ref short address) => Volatile.Read(ref address);
    public static void VolatileWrite(ref short address, short value) => Volatile.Write(ref address, value);
    [CLSCompliant(false)]
    public static ushort VolatileRead(ref ushort address) => Volatile.Read(ref address);
    [CLSCompliant(false)]
    public static void VolatileWrite(ref ushort address, ushort value) => Volatile.Write(ref address, value);
    public static int VolatileRead(ref int address) => Volatile.Read(ref address);
    public static void VolatileWrite(ref int address, int value) => Volatile.Write(ref address, value);
    [CLSCompliant(false)]
    public static uint VolatileRead(ref uint address) => Volatile.Read(ref address);
    [CLSCompliant(false)]
    public static void VolatileWrite(ref uint address, uint value) => Volatile.Write(ref address, value);
    public static long VolatileRead(ref long address) => Volatile.Read(ref address);
    public static void VolatileWrite(ref long address, long value) => Volatile.Write(ref address, value);
    [CLSCompliant(false)]
    public static ulong VolatileRead(ref ulong address) => Volatile.Read(ref address);
    [CLSCompliant(false)]
    public static void VolatileWrite(ref ulong address, ulong value) => Volatile.Write(ref address, value);
    public static float VolatileRead(ref float address) => Volatile.Read(ref address);
    public static void VolatileWrite(ref float address, float value) => Volatile.Write(ref address, value);
    public static double VolatileRead(ref double address) => Volatile.Read(ref address);
    public static void VolatileWrite(ref double address, double value) => Volatile.Write(ref address, value);
    public static IntPtr VolatileRead(ref IntPtr address) => Volatile.Read(ref address);
    public static void VolatileWrite(ref IntPtr address, IntPtr value) => Volatile.Write(ref address, value);
    [CLSCompliant(false)]
    public static UIntPtr VolatileRead(ref UIntPtr address) => Volatile.Read(ref address);
    [CLSCompliant(false)]
    public static void VolatileWrite(ref UIntPtr address, UIntPtr value) => Volatile.Write(ref address, value);
    public static object? VolatileRead(ref object? address) => Volatile.Read(ref address);
    public static void VolatileWrite(ref object? address, object? value) => Volatile.Write(ref address, value);
    private static class LocalDataStore
    {
        private static Dictionary<string, LocalDataStoreSlot>? s_nameToSlotMap;

        public static LocalDataStoreSlot AllocateSlot()
        {
            return new LocalDataStoreSlot(new ThreadLocal<object?>());
        }

        private static Dictionary<string, LocalDataStoreSlot> EnsureNameToSlotMap()
        {
            Dictionary<string, LocalDataStoreSlot>? nameToSlotMap = s_nameToSlotMap;
            if (nameToSlotMap != null)
            {
                return nameToSlotMap;
            }

            nameToSlotMap = new Dictionary<string, LocalDataStoreSlot>();
            return Interlocked.CompareExchange(ref s_nameToSlotMap, nameToSlotMap, null) ?? nameToSlotMap;
        }

        public static LocalDataStoreSlot AllocateNamedSlot(string name)
        {
            LocalDataStoreSlot slot = AllocateSlot();
            Dictionary<string, LocalDataStoreSlot> nameToSlotMap = EnsureNameToSlotMap();
            lock (nameToSlotMap)
            {
                nameToSlotMap.Add(name, slot);
            }
            return slot;
        }

        public static LocalDataStoreSlot GetNamedSlot(string name)
        {
            Dictionary<string, LocalDataStoreSlot> nameToSlotMap = EnsureNameToSlotMap();
            lock (nameToSlotMap)
            {
                if (!nameToSlotMap.TryGetValue(name, out LocalDataStoreSlot? slot))
                {
                    slot = AllocateSlot();
                    nameToSlotMap[name] = slot;
                }
                return slot;
            }
        }

        public static void FreeNamedSlot(string name)
        {
            Dictionary<string, LocalDataStoreSlot> nameToSlotMap = EnsureNameToSlotMap();
            lock (nameToSlotMap)
            {
                nameToSlotMap.Remove(name);
            }
        }

        private static ThreadLocal<object?> GetThreadLocal(LocalDataStoreSlot slot)
        {
            ArgumentNullException.ThrowIfNull(slot);

            Debug.Assert(slot.Data != null);
            return slot.Data;
        }

        public static object? GetData(LocalDataStoreSlot slot)
        {
            return GetThreadLocal(slot).Value;
        }

        public static void SetData(LocalDataStoreSlot slot, object? value)
        {
            GetThreadLocal(slot).Value = value;
        }
    }
}
