using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A plain core module imports only its world's functions: no WASI and no Component Model
// built-ins, whatever .NET APIs the program uses; platform services it does not bind throw.
public sealed class PlainCoreModuleCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void APluginImportsOnlyItsWorld(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var result = Compile(assets, """
            using System;
            using System.Runtime.InteropServices.WebAssembly;

            public static class EntryPoint
            {
                [WitImport("increment")]
                private static extern int Increment(int value);

                // The console, its input, clocks and random numbers compile; without bindings they
                // throw.
                [WitExport("run")]
                public static int Run(int value)
                {
                    if (value < 0)
                    {
                        Console.WriteLine(DateTime.UtcNow);
                        return Guid.NewGuid().GetHashCode() + Environment.TickCount +
                            (Console.ReadLine()?.Length ?? 0) + (Console.IsInputRedirected ? 1 : 0);
                    }
                    return Increment(value);
                }

                // A post-return export is not a WIT function export.
                [WitPostReturn("run")]
                public static void PostRun(int result)
                {
                }
            }
            """, target);

        var imports = result.Program.WitImportMethods.Select(method => method.WitImport!).ToArray();
        Assert.Equal("increment", Assert.Single(imports).FunctionName);
        var reached = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method.DeclaringType.FullName)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("System.Runtime.InteropServices.UnsupportedPlatform", reached);
        Assert.DoesNotContain("System.Runtime.InteropServices.WebAssembly.WasiPlatform", reached);
        Assert.DoesNotContain("System.Runtime.InteropServices.WasiUtcClock", reached);
        Assert.DoesNotContain("System.Runtime.InteropServices.Input.WasiStandardInputReader", reached);
        Assert.DoesNotContain("System.Runtime.InteropServices.ConsoleServices.WasiTerminalDetector", reached);
        Assert.DoesNotContain("System.Runtime.InteropServices.Output.WasiStreamBlockingOutputWriter", reached);
    }

    [Fact]
    public void ABoundServiceReplacesTheUnsupportedDefault()
    {
        using var assets = TestAssets.Create();
        var result = Compile(assets, """
            using System;
            using System.Runtime.InteropServices.WebAssembly;

            [assembly: NetWasmPlatform(typeof(EntryPoint.HostPlatform))]

            public static class EntryPoint
            {
                [WitImport("increment")]
                private static extern int Increment(int value);

                public sealed class HostOutput : PlatformStandardStreams
                {
                    public override void WriteOutput(ReadOnlySpan<byte> bytes) => Increment(bytes.Length);
                }

                public sealed class HostPlatform : NetWasmPlatform
                {
                    public override IStandardStreams StandardStreams => new HostOutput();
                }

                [WitExport("run")]
                public static int Run(int value)
                {
                    Console.Write("x");
                    return value;
                }
            }
            """, WasmTarget.Wasm32);

        var reached = result.Program.ConstructedMethods.Values.Concat(result.Program.Methods.Values)
            .Select(body => body.Method.DeclaringType.FullName)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("EntryPoint+HostOutput", reached);
        Assert.DoesNotContain(result.Program.WitImportMethods, method =>
            method.WitImport!.InterfaceName.StartsWith("wasi:", StringComparison.Ordinal));
    }

    // A plain core module has no event loop: its ready work runs as it is scheduled, and a
    // delay is its platform's Timers, which only a program without WASI may provide.
    [Fact]
    public void DelaysUseTheTimerBinding()
    {
        using var assets = TestAssets.Create();
        var result = Compile(assets, """
            using System;
            using System.Runtime.InteropServices.WebAssembly;
            using System.Threading.Tasks;

            [assembly: NetWasmPlatform(typeof(EntryPoint.TimedPlatform))]

            public static class EntryPoint
            {
                [WitImport("increment")]
                private static extern int Increment(int value);

                public sealed class HostTimers : ITimerScheduler
                {
                    public IDisposable Schedule(Action callback, TimeSpan delay) => throw new NotSupportedException();
                }

                public sealed class TimedPlatform : NetWasmPlatform
                {
                    public override ITimerScheduler Timers => new HostTimers();
                }

                [WitExport("run")]
                public static int Run(int value)
                {
                    _ = Task.Delay(value).ContinueWith(_ => Increment(value));
                    return value;
                }
            }
            """, WasmTarget.Wasm32);

        var reached = result.Program.ConstructedMethods.Values.Concat(result.Program.Methods.Values)
            .Select(body => body.Method.DeclaringType.FullName)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("EntryPoint+HostTimers", reached);
        Assert.Contains("System.Runtime.InteropServices.CoreModuleScheduler", reached);
        Assert.Equal("increment", Assert.Single(result.Program.WitImportMethods).WitImport!.FunctionName);
    }

    [Fact]
    public void ReachingWasiOrAComponentBuiltInFailsNamingTheMethod()
    {
        using var assets = TestAssets.Create();
        var wasi = Assert.Throws<CompilerException>(() => Compile(assets, """
            using System.Runtime.InteropServices.WebAssembly;

            public static class EntryPoint
            {
                [WitImport("wasi:random/random@0.3.2", "get-random-u64")]
                private static extern long Random();

                [WitExport("run")]
                public static int Run(int value) => (int)Random() + value;
            }
            """, WasmTarget.Wasm32));
        Assert.Equal(DiagnosticCode.ComponentContract, wasi.Diagnostic.Code);
        Assert.Contains("the WASI import 'wasi:random/random@0.3.2#get-random-u64'", wasi.Message);
        Assert.Contains("provide the service there", wasi.Message);
        Assert.Contains("Random", wasi.Diagnostic.Method);

        // HTTP is not a platform service: HttpClient takes a handler of the program's own.
        var http = Assert.Throws<CompilerException>(() => Compile(assets, """
            using System.Runtime.InteropServices.WebAssembly;

            public static class EntryPoint
            {
                [WitImport("wasi:http/client@0.3.2", "send")]
                private static extern long Send();

                [WitExport("run")]
                public static int Run(int value) => (int)Send() + value;
            }
            """, WasmTarget.Wasm32));
        Assert.Contains("give HttpClient an HttpMessageHandler of your own", http.Message);

        var builtIn = Assert.Throws<CompilerException>(() => Compile(assets, """
            using System.Runtime.InteropServices.WebAssembly;

            public static class EntryPoint
            {
                [ComponentBuiltIn("$root", "[waitable-set-new]")]
                private static extern int NewSet();

                [WitExport("run")]
                public static int Run(int value) => NewSet() + value;
            }
            """, WasmTarget.Wasm32));
        Assert.Contains("the Component Model built-in '[waitable-set-new]'", builtIn.Message);
    }

    [Fact]
    public void AnAsyncExportFailsNamingTheMethod()
    {
        using var assets = TestAssets.Create();
        var exception = Assert.Throws<CompilerException>(() => Compile(assets, """
            using System.Runtime.InteropServices.WebAssembly;

            public static class EntryPoint
            {
                [WitImport("increment")]
                private static extern int Increment(int value);

                [WitExport("run")]
                public static int Run(int value) => Increment(value);

                [ComponentExport("[async-lift]run-later")]
                public static int RunLater(int value) => value;
            }
            """, WasmTarget.Wasm32));

        Assert.Contains("it exports the async function '[async-lift]run-later'", exception.Message);
        Assert.Contains("RunLater", exception.Diagnostic.Method);
    }

    [Fact]
    public void ValidatorRejectsMissingInputs()
    {
        var validator = new NetWasm.Compiler.Pipeline.PlainCoreModuleValidator();
        var world = new NetWasm.Compiler.Analysis.PlatformWorldCapabilities(false, false, false)
        {
            NativeIntegerSize = 4,
            JavaScriptHost = false,
            Wasi = false,
        };
        Assert.Throws<ArgumentNullException>(() => { validator.Validate(null!, [], world, null!, null!); });
    }

    [Fact]
    public void APlainCoreModuleIsALibraryOfASelectedWorld()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("NoWorld", """
            using System.Runtime.InteropServices.WebAssembly;

            public static class EntryPoint
            {
                [WitImport("increment")]
                private static extern int Increment(int value);

                [WitExport("run")]
                public static int Run(int value) => Increment(value);
            }
            """);

        foreach (var options in new[]
        {
            new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
                EntryPointKind: CompilerEntryPointKind.Library) { PlainCoreModule = true },
            new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
                WitPath: WitPath(assets)) { PlainCoreModule = true },
        })
        {
            var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));
            Assert.Contains("a plain core module is a library build of a selected WIT world", exception.Message);
        }
    }

    private static CompilationResult Compile(TestAssets assets, string source, WasmTarget target) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assets.CompileSource("Plugin" + target, source),
            [assets.CoreLib],
            string.Empty,
            string.Empty,
            [],
            target,
            WitPath: WitPath(assets),
            EntryPointKind: CompilerEntryPointKind.Library)
        {
            PlainCoreModule = true,
        });

    private static string WitPath(TestAssets assets) => Path.Combine(
        assets.Root, "tests", "NetWasm.Compiler.Tests", "Fixtures", "scalar-component.wit");
}
