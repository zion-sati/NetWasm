using System;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Members .NET 8 and 9 added that libraries compiled as .NET 10 code use: Type.IsFunctionPointer and
// Environment.CpuUsage. NetWasm's types are never function pointers, and WASI has no process CPU clock.
public sealed class RecentTypeAndEnvironmentMembersTests
{
    [Test]
    public async Task OrdinaryTypesAreNotFunctionPointers()
    {
        await Assert.That(typeof(int).IsFunctionPointer).IsFalse();
        await Assert.That(typeof(string).IsUnmanagedFunctionPointer).IsFalse();
        await Assert.That(typeof(Action).IsFunctionPointer).IsFalse();
    }

    [Test]
    public async Task ProcessCpuUsageIsUnavailableOnWasi()
    {
#if NETWASM
        await Assert.That(ThrowsPlatformNotSupported(() => _ = Environment.CpuUsage)).IsTrue();
#else
        await Assert.That(Environment.CpuUsage.TotalTime).IsGreaterThanOrEqualTo(TimeSpan.Zero);
#endif
    }

    private static bool ThrowsPlatformNotSupported(Action action)
    {
        try { action(); return false; }
        catch (PlatformNotSupportedException) { return true; }
    }
}
