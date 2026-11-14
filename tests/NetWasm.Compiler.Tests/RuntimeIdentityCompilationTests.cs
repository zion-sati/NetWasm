using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class RuntimeIdentityCompilationTests
{
    private const int JavaScriptHostInput = 40;
    private const int WitComponentInput = 41;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void BrowserJavaScriptModulesReportTheBrowser(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("RuntimeIdentityFixture", CreateSource(assets));
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "RuntimeIdentityFixture.EntryPoint",
            "Run", [new("run", "RuntimeIdentityFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/platform",
            UseJavaScriptExportBoundary: true));

        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, JavaScriptHostInput, target, result.StaticDataEnd));
    }

    // Raw core modules have no WIT world and also sit behind the JavaScript export boundary. The
    // raw-module node harness runs wasm32 only.
    [Fact]
    public void RawCoreModulesReportTheBrowser()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("RuntimeIdentityFixture", CreateSource(assets));
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "RuntimeIdentityFixture.EntryPoint",
            "Run", []));

        Assert.Equal(42, CompilerTestSupport.ExecuteWithNode(result.ApplicationModule, assets.Directory,
            JavaScriptHostInput));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void WitComponentsReportWasi(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("RuntimeIdentityFixture", CreateSource(assets));
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "RuntimeIdentityFixture.EntryPoint",
            "Run", [new("run", "RuntimeIdentityFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/platform"));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);

        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, WitComponentInput, target, result.StaticDataEnd));
    }

    // The fixture checks NetWasm's own version, which the CoreLib build reads from this file.
    internal static string CreateSource(TestAssets assets)
    {
        var version = File.ReadAllText(Path.Combine(assets.Root, "eng", "NetWasm.ReleaseVersion.txt")).Trim();
        return Source.Replace("NETWASM_RELEASE_VERSION", version, StringComparison.Ordinal);
    }

    internal const string Source = """
        using System;
        using System.Runtime.InteropServices;
        namespace RuntimeIdentityFixture;

        public static class EntryPoint
        {
            static bool Unsupported(Action action)
            {
                try { action(); return false; }
                catch (PlatformNotSupportedException e) { return e.Message.Length > 0; }
            }

            public static int Run(int input)
            {
                // Host-independent contracts, which desktop .NET shares.
        #pragma warning disable SYSLIB0019
                if (RuntimeEnvironment.GetSystemVersion() != "v" + Environment.Version || RuntimeEnvironment.FromGlobalAccessCache(null!)) return 3;
                if (!Unsupported(() => _ = RuntimeEnvironment.SystemConfigurationFile) || !Unsupported(() => RuntimeEnvironment.GetRuntimeInterfaceAsIntPtr(Guid.Empty, Guid.Empty)) || !Unsupported(() => RuntimeEnvironment.GetRuntimeInterfaceAsObject(Guid.Empty, Guid.Empty))) return 4;
        #pragma warning restore SYSLIB0019
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Create("NoSuchPlatform")) || OperatingSystem.IsOSPlatform("NoSuchPlatform") || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsWatchOS()) return 5;
                try { OperatingSystem.IsOSPlatform(null!); return 6; } catch (ArgumentNullException) { }
        #if !DESKTOP_ORACLE
                // NetWasm's identity: its own version, the wasm architecture and the compiled host.
                bool browser = input == 40;
                string host = browser ? "Browser" : "WASI";
                if (Environment.Version != new Version("NETWASM_RELEASE_VERSION".Split('-', '+')[0]) || RuntimeInformation.FrameworkDescription != "NetWasm NETWASM_RELEASE_VERSION") return 7;
                if (RuntimeInformation.ProcessArchitecture != Architecture.Wasm || RuntimeInformation.OSArchitecture != Architecture.Wasm) return 8;
                if (RuntimeInformation.OSDescription != host || RuntimeInformation.RuntimeIdentifier != (browser ? "browser-wasm" : "wasi-wasm")) return 9;
                if (OperatingSystem.IsBrowser() != browser || OperatingSystem.IsWasi() == browser) return 10;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Create(host.ToUpperInvariant())) != true || RuntimeInformation.IsOSPlatform(OSPlatform.Create(browser ? "wasi" : "browser"))) return 11;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD)) return 12;
                if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD() || OperatingSystem.IsWindowsVersionAtLeast(10)) return 13;
                if (!OperatingSystem.IsOSPlatformVersionAtLeast(host, 1) || OperatingSystem.IsOSPlatformVersionAtLeast(host, 1, 1) || OperatingSystem.IsOSPlatformVersionAtLeast("linux", 0)) return 14;
                if (!RuntimeEnvironment.GetRuntimeDirectory().EndsWith(System.IO.Path.DirectorySeparatorChar)) return 15;
                if (Environment.OSVersion.Platform != PlatformID.Other || Environment.OSVersion.Version != new Version(1, 0, 0, 0)) return 16;
                return input + (browser ? 2 : 1);
        #else
                return input + 1;
        #endif
            }
        }
        """;
}
