using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Libraries built for .NET Standard guard their native calls with RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
// (Serilog's console sink) or OperatingSystem.IsOSPlatform("windows"). NetWasm is none of those platforms, so the
// checks fold before reachability and the guarded P/Invokes, whose shapes NetWasm does not marshal, are never
// compiled. A platform the check cannot know at compile time keeps its run-time test.
public sealed class KnownPlatformCheckCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void WindowsOnlyNativeCallsBehindPlatformChecksAreLeftOut(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("PlatformCheckFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "PlatformCheckFixture.EntryPoint",
            "Run", [new("run", "PlatformCheckFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Runtime.InteropServices;

        namespace PlatformCheckFixture;

        public static class Windows
        {
            [DllImport("kernel32", SetLastError = true)]
            public static extern IntPtr GetStdHandle(int handle);

            [DllImport("libc", EntryPoint = "printf", CallingConvention = CallingConvention.StdCall)]
            public static extern int Print(string format);
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Windows.GetStdHandle(-11) == IntPtr.Zero) return 1;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
                {
                    return Windows.Print("unreachable");
                }
                if (OperatingSystem.IsOSPlatform("Windows")) return Windows.Print("unreachable");
                // WASI is the platform here, decided when the program runs.
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Create("WASI")) || !OperatingSystem.IsOSPlatform("wasi")) return 2;
                return input + 1;
            }
        }
        """;
}
