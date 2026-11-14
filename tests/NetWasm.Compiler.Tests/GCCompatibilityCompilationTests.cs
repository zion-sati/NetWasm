using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class GCCompatibilityCompilationTests
{
    [Fact]
    public void CompilerExecutesSupportedGcContract()
    {
        using var assets = TestAssets.Create();
        var coreLib = assets.CompileCoreLibVariant("GCCompatibilityCoreLib", string.Empty);
        var assembly = assets.CompileSourceAgainstCoreLib(
            "GCCompatibilityFixture",
            Source,
            coreLib);
        var compilation = NetWasmCompiler.Compile(
            new CompilerOptions(
                assembly,
                [coreLib],
                "GCCompatibilityFixture.EntryPoint",
                "Run",
                [],
                WasmTarget.Wasm32));

        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 0));
    }

    private const string Source = """
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GCCompatibilityFixture;

public static class EntryPoint
{
    public static int Run(int _)
    {
        if (GC.MaxGeneration != 0 || GC.CollectionCount(0) < 0)
        {
            return 1;
        }

        GC.Collect();
        GC.Collect(0);
        GC.Collect(0, GCCollectionMode.Default);
        GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        GC.Collect(0, GCCollectionMode.Optimized, blocking: true, compacting: false);

        object value = new();
        object other = new();
        int identity = value.GetHashCode();
        if (identity == 0 || identity != value.GetHashCode() || identity == other.GetHashCode())
        {
            return 8;
        }

        var overridden = new OverrideHash();
        int runtimeIdentity = RuntimeHelpers.GetHashCode(overridden);
        if (runtimeIdentity == 0 || runtimeIdentity != RuntimeHelpers.GetHashCode(overridden) ||
            RuntimeHelpers.GetHashCode(null) != 0 || OverrideHash.Calls != 0)
        {
            return 9;
        }

        if (overridden.GetHashCode() != 123 || OverrideHash.Calls != 1)
        {
            return 10;
        }

        Action prepared = static () => { };
        RuntimeHelpers.PrepareDelegate(prepared);
        if (RuntimeHelpers.OffsetToStringData != IntPtr.Size + sizeof(int) ||
            RuntimeHelpers.IsReferenceOrContainsReferences<int>() ||
            !RuntimeHelpers.IsReferenceOrContainsReferences<object>())
        {
            return 11;
        }

        ReadOnlyMemory<char> arrayBacked = new char[] { 'x' };
        string? text;
        int start;
        int length;
        if (MemoryMarshal.TryGetString(arrayBacked, out text, out start, out length) ||
            text is not null || start != 0 || length != 0)
        {
            return 12;
        }

        var weak = new WeakReference(value);
        if (GC.GetGeneration(value) != 0 || GC.GetGeneration(weak) != 0)
        {
            return 2;
        }

        var overriddenWeak = new OverrideWeakReference(value);
        if (GC.GetGeneration(overriddenWeak) != 0 || OverrideWeakReference.TargetReads != 0)
        {
            return 14;
        }

        try
        {
            GC.GetGeneration((WeakReference)null!);
            return 15;
        }
        catch (NullReferenceException)
        {
        }

        try
        {
            GC.GetGeneration(new WeakReference(null));
            return 16;
        }
        catch (ArgumentNullException)
        {
        }

        int collectionCount = GC.CollectionCount(0);
        if (GC.GetTotalMemory(false) < 0 || GC.GetTotalMemory(true) < 0 ||
            GC.CollectionCount(0) <= collectionCount || GC.GetTotalAllocatedBytes() < 0)
        {
            return 3;
        }

        try
        {
            GC.GetTotalPauseDuration();
            return 13;
        }
        catch (PlatformNotSupportedException)
        {
        }

        try
        {
            GC.RemoveMemoryPressure(0);
            return 17;
        }
        catch (ArgumentOutOfRangeException exception) when (
            exception.ParamName == "bytesAllocated")
        {
        }

        try
        {
            GC.AddMemoryPressure(0);
            return 18;
        }
        catch (ArgumentOutOfRangeException exception) when (
            exception.ParamName == "bytesAllocated")
        {
        }

        // Reported pressure is accounted as Mono accounts it; pressure past its budget runs a
        // collection, and removing it never does.
        var beforePressure = GC.CollectionCount(0);
        GC.AddMemoryPressure(1);
        GC.RemoveMemoryPressure(1);
        if (GC.CollectionCount(0) != beforePressure)
        {
            return 20;
        }
        for (var step = 0; step < 4; step++)
        {
            GC.AddMemoryPressure(4 * 1024 * 1024);
        }
        if (GC.CollectionCount(0) == beforePressure)
        {
            return 21;
        }

        // As Mono's: the collector's current counters; every collection is a full blocking one.
        var memory = GC.GetGCMemoryInfo();
        // (This harness's host reports no heap size; the linked runtime's collector does.)
        if (memory.HeapSizeBytes < 0 || memory.FragmentedBytes < 0 || memory.FragmentedBytes > memory.HeapSizeBytes ||
            memory.Index != GC.CollectionCount(0) || GC.GetGCMemoryInfo(GCKind.Ephemeral).Index != 0 ||
            AppDomain.MonitoringSurvivedProcessMemorySize < 0)
        {
            return 4;
        }

        try
        {
            GC.GetGCMemoryInfo((GCKind)4);
            return 19;
        }
        catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "kind")
        {
        }

        GC.WaitForPendingFinalizers();
        GC.KeepAlive(value);

        try
        {
            GC.Collect(0, GCCollectionMode.Default, blocking: false);
            return 5;
        }
        catch (PlatformNotSupportedException)
        {
        }

        try
        {
            GC.Collect(0, GCCollectionMode.Forced, blocking: true, compacting: true);
            return 6;
        }
        catch (PlatformNotSupportedException)
        {
        }

        // Every non-negative generation runs the single-generation collection, and every
        // generation's count covers every collection.
        int beforeHigher = GC.CollectionCount(2);
        GC.Collect(1);
        GC.Collect(2);
        GC.Collect(3, GCCollectionMode.Forced, blocking: true, compacting: false);
        if (GC.CollectionCount(2) != beforeHigher + 3 ||
            GC.CollectionCount(1) != GC.CollectionCount(0) ||
            GC.CollectionCount(3) != GC.CollectionCount(0))
        {
            return 7;
        }

        try
        {
            GC.Collect(-1);
            return 20;
        }
        catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "generation")
        {
        }

        try
        {
            GC.CollectionCount(-1);
            return 21;
        }
        catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "generation")
        {
        }

        return 0;
    }
}

public sealed class OverrideHash
{
    public static int Calls;

    public override int GetHashCode()
    {
        Calls++;
        return 123;
    }
}

public sealed class OverrideWeakReference(object target) : WeakReference(target)
{
    public static int TargetReads;

    public override object? Target
    {
        get
        {
            TargetReads++;
            return null;
        }
        set => base.Target = value;
    }
}
""";
}
