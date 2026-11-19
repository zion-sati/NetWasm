// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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

    // A dead arm's references are never resolved, so an assembly only another operating system's code uses need not
    // be there (DnsClient reads the Windows registry behind OperatingSystem.IsWindows()). The library the fixture was
    // built against is left out of the compilation; Debug and Release IL, every form of check.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferencesBehindOtherPlatformsNeedNotResolve(bool optimize)
    {
        using var assets = TestAssets.Create();
        var windowsOnly = assets.CompileSource("WindowsOnly", WindowsOnlySource);
        var assembly = optimize
            ? assets.CompileOptimizedSource("GuardedReferenceFixture", GuardedReferenceSource, windowsOnly)
            : assets.CompileSource("GuardedReferenceFixture", GuardedReferenceSource, windowsOnly);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "GuardedReferenceFixture.EntryPoint",
            "Run", [new("run", "GuardedReferenceFixture.EntryPoint", "Run")], Target: WasmTarget.Wasm32,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // A check decided when the program runs keeps both arms, so their references must resolve.
    [Fact]
    public void ReferencesBehindRunTimeChecksStillResolve()
    {
        using var assets = TestAssets.Create();
        var windowsOnly = assets.CompileSource("WindowsOnly", WindowsOnlySource);
        var assembly = assets.CompileSource("RunTimeGuardFixture", """
            using System;
            using WindowsOnly;

            namespace RunTimeGuardFixture;

            public static class EntryPoint
            {
                public static int Run(int input) =>
                    OperatingSystem.IsBrowser() ? RegistryKey.LocalMachine.GetSubKeyNames().Length : input + 1;
            }
            """, windowsOnly);

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(new(assembly, [assets.CoreLib],
            "RunTimeGuardFixture.EntryPoint", "Run", [new("run", "RunTimeGuardFixture.EntryPoint", "Run")],
            Target: WasmTarget.Wasm32, WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0")));

        Assert.Equal(DiagnosticCode.AssemblyResolution, exception.Diagnostic.Code);
        Assert.Contains("WindowsOnly", exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    private const string WindowsOnlySource = """
        namespace WindowsOnly;

        public sealed class RegistryKey : System.IDisposable
        {
            public static RegistryKey LocalMachine { get; } = new();

            public RegistryKey? OpenSubKey(string name) => this;

            public string[] GetSubKeyNames() => [];

            public void Dispose() { }
        }
        """;

    private const string GuardedReferenceSource = """
        using System;
        using System.Runtime.InteropServices;
        using WindowsOnly;

        namespace GuardedReferenceFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                input += Policy();
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) input += RegistryKey.LocalMachine.GetSubKeyNames().Length;
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) input += RegistryKey.LocalMachine.GetSubKeyNames().Length;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD)) input += RegistryKey.LocalMachine.GetSubKeyNames().Length;
                if (OperatingSystem.IsOSPlatform("windows")) input += RegistryKey.LocalMachine.GetSubKeyNames().Length;
                return input + 1;
            }

            // DnsClient's shape: everything after the check is the other platform's, a protected block included.
            private static int Policy()
            {
                if (!OperatingSystem.IsWindows()) return 0;
                using var key = RegistryKey.LocalMachine.OpenSubKey("Software");
                var total = 0;
                foreach (var name in key!.GetSubKeyNames()) total += name.Length;
                return total;
            }
        }
        """;

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
