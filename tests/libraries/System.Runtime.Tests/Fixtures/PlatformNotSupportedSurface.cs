// From the compiler harness's PlatformNotSupportedSurfaceCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.IO.IsolatedStorage;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
namespace PlatformNotSupportedSurfaceFixture;

sealed class Bag : DynamicObject { }

public static class EntryPoint
{
    static int Twice(int value) => value * 2;

    static bool Unsupported(Action action)
    {
#if DESKTOP_ORACLE
        return true;
#else
        try { action(); return false; }
        catch (PlatformNotSupportedException e) { return e.Message.Length > 0; }
#endif
    }

    public static int Run(int input)
    {
        // Pulled-in enums keep upstream's values.
        if ((int)PackingSize.Size128 != 128 || (int)AssemblyBuilderAccess.RunAndCollect != 9 || (int)ParallelExecutionMode.ForceParallelism != 1) return 1;
        if ((int)ParallelMergeOptions.FullyBuffered != 3 || (int)PipeDirection.InOut != 3 || (int)PipeTransmissionMode.Message != 1) return 2;
        if ((int)MemoryMappedFileAccess.ReadWriteExecute != 5 || (int)CompressionLevel.SmallestSize != 3 || (int)ZipArchiveMode.Update != 2) return 3;

        // Delegates keep their ordinary behaviour alongside the reflective members.
        Func<int, int> twice = Twice;
        Func<int, int> combined = (Func<int, int>)Delegate.Combine(twice, (Func<int, int>)(v => v + 1));
        if (twice(4) != 8 || combined(4) != 5 || combined.GetInvocationList().Length != 2) return 5;

        // Whole unsupported types bind; their entry points throw.
#pragma warning disable SYSLIB0050, SYSLIB0051
        if (!Unsupported(() => AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Dynamic"), AssemblyBuilderAccess.Run))) return 6;
        if (!Unsupported(() => new DynamicMethod("m", typeof(int), Type.EmptyTypes))) return 7;
        if (!Unsupported(() => new TypeDelegator(typeof(int)))) return 8;
        if (!Unsupported(() => new Bag())) return 11;
        // IsolatedStorageFileStream is upstream's (NetWasm.System.IO.IsolatedStorage.Tests).
        if (!Unsupported(() => MemoryMappedFile.CreateNew("map", 16))) return 13;
        if (!Unsupported(() => new CustomAttributeBuilder(null!, Array.Empty<object>()))) return 14;

        // Unsupported members of supported types bind and throw.
        if (!Unsupported(() => Activator.CreateInstance(typeof(Bag)))) return 15;
        if (!Unsupported(() => Delegate.CreateDelegate(typeof(Func<int, int>), typeof(EntryPoint), "Twice"))) return 16;
        if (!Unsupported(() => _ = twice.Method)) return 17;
        if (!Unsupported(() => _ = new InvalidOperationException().TargetSite)) return 18;
        if (!Unsupported(() => new ArgumentException("a").GetObjectData(null!, default))) return 19;
        if (!Unsupported(() => Expression.Property(Expression.Constant("s"), "Length"))) return 20;
        if (!Unsupported(() => CompareInfo.GetCompareInfo(0, (Assembly)null!))) return 22;
#pragma warning restore SYSLIB0050, SYSLIB0051
        return input + 1;
    }
}
