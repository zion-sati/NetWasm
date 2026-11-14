using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Full-GC notifications and no-GC regions behave as Mono's: the arguments are checked, then NotImplementedException.
public sealed class GcRegionAndNotificationCompilationTests
{
    [Fact]
    public void NoGcRegionsAndFullGcNotificationsThrowAsMonosDo()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("GcRegionFixture", """
            using System;
            namespace GcRegionFixture;

            public static class EntryPoint
            {
                private static bool Throws<T>(Action action) where T : Exception
                {
                    try { action(); return false; } catch (T) { return true; }
                }

                public static int Run(int input)
                {
                    if (!Throws<NotImplementedException>(() => GC.TryStartNoGCRegion(1024))) return 1;
                    if (!Throws<NotImplementedException>(() => GC.TryStartNoGCRegion(1024, 512, true))) return 2;
                    if (!Throws<NotImplementedException>(GC.EndNoGCRegion)) return 3;
                    if (!Throws<ArgumentOutOfRangeException>(() => GC.RegisterForFullGCNotification(0, 50))) return 4;
                    if (!Throws<ArgumentOutOfRangeException>(() => GC.RegisterForFullGCNotification(50, 100))) return 5;
                    if (!Throws<NotImplementedException>(() => GC.RegisterForFullGCNotification(50, 50))) return 6;
                    if (!Throws<ArgumentOutOfRangeException>(() => GC.WaitForFullGCApproach(-2))) return 7;
                    if (!Throws<NotImplementedException>(() => GC.WaitForFullGCComplete(TimeSpan.FromMilliseconds(5)))) return 8;
                    if (!Throws<NotImplementedException>(GC.CancelFullGCNotification)) return 9;
                    return input + 1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "GcRegionFixture.EntryPoint", "Run", []));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }
}
