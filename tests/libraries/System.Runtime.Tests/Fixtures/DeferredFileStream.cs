// From the compiler harness's DeferredFileStreamCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
#pragma warning disable CS0618
using System;
using System.IO;
using Microsoft.Win32.SafeHandles;
namespace DeferredStreamFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        using var borrowed = new SafeFileHandle(new IntPtr(42), false);
        using var zero = new SafeFileHandle(IntPtr.Zero, false);
        using var invalid = new SafeFileHandle(new IntPtr(-1), true);
        if (borrowed.IsInvalid || zero.IsInvalid || !invalid.IsInvalid) return 1;
        // FileStream's constructors and owned handles are 0.12's file system over WASI 0.3, run by
        // ManagedExecutableAsyncMainCompilationTests and the file TUnit suites.
        if (borrowed.IsClosed || borrowed.DangerousGetHandle() != new IntPtr(42)) return 7;
        borrowed.Dispose(); borrowed.Dispose(); zero.Dispose(); invalid.Dispose();
        if (!borrowed.IsClosed || !zero.IsClosed || !invalid.IsClosed) return 8;
        using var memory = new MemoryStream(); memory.WriteByte(42); memory.Position = 0;
        return memory.ReadByte() == 42 ? input + 1 : 9;
    }
}
