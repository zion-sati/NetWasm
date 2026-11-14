// From the compiler harness's RuntimeEnvironmentMembersCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
#pragma warning disable CS0618
namespace RuntimeEnvironmentFixture;

public static class EntryPoint
{
    public static int Run(int input)
    {
        if (Environment.Is64BitOperatingSystem != Environment.Is64BitProcess) return 1;

        var domain = AppDomain.CurrentDomain;
        domain.AppendPrivatePath("private");
        domain.ClearPrivatePath();
        domain.ClearShadowCopyPath();
        domain.SetCachePath("cache");
        domain.SetDynamicBase("dynamic");
        domain.SetShadowCopyFiles();
        domain.SetShadowCopyPath("shadow");
        if (domain.MonitoringTotalProcessorTime < TimeSpan.Zero) return 2;
#if !DESKTOP_ORACLE
        if (domain.MonitoringTotalProcessorTime != TimeSpan.Zero) return 3;
#endif

        long before = GC.GetAllocatedBytesForCurrentThread();
        var block = new byte[10000];
        long after = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(block);
#if DESKTOP_ORACLE
        if (after - before < 10000) return 4;
#else
        // One thread: its count is the precise total. The raw harness links no collector, so the count only
        // has to not go back.
        if (after < before || after != GC.GetTotalAllocatedBytes(precise: true)) return 4;
#endif

        var zone = TimeZone.CurrentTimeZone;
        if (zone is null || !ReferenceEquals(zone, TimeZone.CurrentTimeZone)) return 5;
        if (zone.StandardName != TimeZoneInfo.Local.StandardName) return 6;
        var noon = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        if (zone.ToLocalTime(noon) != TimeZoneInfo.ConvertTimeFromUtc(noon, TimeZoneInfo.Local)) return 7;

        var directory = RuntimeEnvironment.GetRuntimeDirectory();
        if (string.IsNullOrEmpty(directory) || directory[^1] != Path.DirectorySeparatorChar) return 8;
#if !DESKTOP_ORACLE
        if (Debugger.Launch()) return 9;
#endif

        // The last P/Invoke error is the value generated marshalling code stores.
        Marshal.SetLastPInvokeError(5);
        if (Marshal.GetLastPInvokeError() != 5 || Marshal.GetLastWin32Error() != 5) return 11;

        using (new MemoryFailPoint(1)) { }
        try { _ = new MemoryFailPoint(0); return 10; } catch (ArgumentOutOfRangeException) { }
        return input + 1;
    }
}
