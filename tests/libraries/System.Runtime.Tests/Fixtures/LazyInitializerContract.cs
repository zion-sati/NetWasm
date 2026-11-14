// From the compiler harness's LazyInitializerContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Threading;
using System.Reflection;
namespace LazyInitializerFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        Box? box = null;
        var first = LazyInitializer.EnsureInitialized(ref box);
        if (first.Value != 7 || !ReferenceEquals(first, LazyInitializer.EnsureInitialized(ref box))) return 1;
        if (!ReferenceEquals(first, LazyInitializer.EnsureInitialized(ref box, (Func<Box>)null!))) return 2;
        var calls = 0;
        box = null;
        try { LazyInitializer.EnsureInitialized(ref box, () => { calls++; throw new ArgumentException(); }); return 3; }
        catch (ArgumentException) { }
        if (box != null || calls != 1) return 4;
        try { LazyInitializer.EnsureInitialized(ref box, () => (Box)null!); return 5; }
        catch (InvalidOperationException) { }
        var winner = new Box();
        var chosen = LazyInitializer.EnsureInitialized(ref box, () => { box = winner; return new Box(); });
        if (!ReferenceEquals(chosen, winner)) return 6;
        object? gate = null;
        var initialized = false;
        var value = 9;
        if (LazyInitializer.EnsureInitialized(ref value, ref initialized, ref gate) != 0 || !initialized || gate == null) return 7;
        var heldGate = gate;
        value = 8;
        if (LazyInitializer.EnsureInitialized(ref value, ref initialized, ref gate, (Func<int>)null!) != 8 || !ReferenceEquals(heldGate, gate)) return 8;
        initialized = false; gate = null;
        string? nullable = "before";
        if (LazyInitializer.EnsureInitialized(ref nullable, ref initialized, ref gate, () => (string?)null) != null || !initialized || gate == null) return 9;
        initialized = false; gate = null; value = 12;
        try { LazyInitializer.EnsureInitialized(ref value, ref initialized, ref gate, () => throw new ArgumentException()); return 10; }
        catch (ArgumentException) { }
        if (initialized || value != 12 || gate == null) return 11;
        if (LazyInitializer.EnsureInitialized(ref value, ref initialized, ref gate, () => 19) != 19 || !initialized) return 12;
        box = null; gate = null;
        try { LazyInitializer.EnsureInitialized(ref box, ref gate, () => (Box)null!); return 13; }
        catch (InvalidOperationException) { }
        if (box != null || gate == null) return 14;
        first = LazyInitializer.EnsureInitialized(ref box, ref gate, () => winner);
        if (!ReferenceEquals(first, winner) || !ReferenceEquals(LazyInitializer.EnsureInitialized(ref box, ref gate, (Func<Box>)null!), winner)) return 15;
        NoDefault? missing = null;
        try { LazyInitializer.EnsureInitialized(ref missing); return 16; }
        catch (MissingMemberException ex) { if (ex is MissingMethodException) return 17; }
        initialized = false; gate = null;
        try { LazyInitializer.EnsureInitialized(ref missing, ref initialized, ref gate); return 18; }
        catch (MissingMemberException ex) { if (ex is MissingMethodException) return 19; }
        if (initialized || gate == null) return 20;
        Throws? throwing = null;
        try { LazyInitializer.EnsureInitialized(ref throwing); return 21; }
        catch (TargetInvocationException ex) { if (ex.InnerException is not ArgumentException) return 22; }
        box = null;
        try { LazyInitializer.EnsureInitialized(ref box, (Func<Box>)null!); return 23; }
        catch (NullReferenceException) { }
        initialized = true; gate = null; box = winner;
        if (!ReferenceEquals(LazyInitializer.EnsureInitialized(ref box, ref initialized, ref gate), winner) || gate != null) return 24;
        return input + 1;
    }
}
public sealed class Box { public int Value = 7; }
public sealed class NoDefault { public NoDefault(int value) { } }
public sealed class Throws { public Throws() { throw new ArgumentException(); } }
