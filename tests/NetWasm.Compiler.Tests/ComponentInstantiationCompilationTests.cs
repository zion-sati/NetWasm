// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.ManagedExecutableAsyncMainCompilationTests;

namespace NetWasm.Compiler.Tests;

// A component's instantiation runs no managed code. jco calls its initialize export while it
// instantiates the component, and the Component Model traps any wait there ("cannot block a
// synchronous task before returning"), so the managed work NetWasm used to do there runs where the
// program's own code runs: module initializers in the first entry, and local time and symbols at
// first use in a component without a process entry.
public sealed class ComponentInstantiationCompilationTests
{
    // A module initializer runs once, before Main, as on desktop .NET, for a synchronous and an
    // asynchronous (callback-lifted) Main. It runs in wasi:cli/run, not during instantiation, where
    // jco traps its console write ("cannot block a synchronous task before returning"); Wasmtime
    // completes that write without waiting, so the jco half is in run-console-stall.sh.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModuleInitializersRunBeforeMainWhereTheyMayWait(bool asynchronous)
    {
        using var assets = TestAssets.Create();
        var source = $$"""
            using System.Runtime.CompilerServices;
            namespace ManagedExecutableAsyncMainFixture;
            internal static class Startup
            {
                internal static int Runs;

                [ModuleInitializer]
                internal static void Initialize()
                {
                    Runs++;
                    System.Console.WriteLine("initializer");
                }
            }
            internal static class Program
            {
                public static {{(asynchronous ? "async System.Threading.Tasks.Task" : "void")}} Main()
                {
                    {{(asynchronous ? "await System.Threading.Tasks.Task.Yield();" : "")}}
                    System.Console.WriteLine("main " + Startup.Runs);
                }
            }
            """;
        var component = await PackageCommand(assets, true, source, new(
            ManagedExecutableParameterShape.None,
            ManagedExecutableReturnShape.Void,
            asynchronous ? ManagedExecutableCompletionShape.Asynchronous : ManagedExecutableCompletionShape.Synchronous));

        var (exitCode, output, error) = await RunWasmtimeWithOutput(assets.Root, component);

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}");
        Assert.Equal("initializer\nmain 1\n", output);
    }

    // A library component (no process entry) whose program can reach local time. With TZ naming a
    // zone, instantiation ran the local-time preflight, which opens the .tz-info sidecar: it failed
    // every export when the sidecar is absent, as here, and would trap on the wait under jco when it
    // is present. An export that never reads local time now runs as on desktop .NET, and the module
    // initializer runs once, in the first export, before its body.
    [Theory]
    [InlineData(null)]
    [InlineData("Australia/Sydney")]
    public async Task ALibraryComponentReadsLocalTimeOnlyWhenItsCodeDoes(string? timeZone)
    {
        using var assets = TestAssets.Create();
        var component = await PackageLibrary(assets);
        string[] environment = timeZone is null ? [] : ["--env", $"TZ={timeZone}"];

        var (exitCode, output, error) = await RunWasmtimeWithOutput(
            assets.Root, [.. environment, "--invoke", "plain(1)", component]);

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}");
        Assert.Equal("102", output.Trim());
        // Without TZ, local time is UTC; with a zone and no time zone data, the export that reads
        // local time fails in its own code, after instantiation.
        var (localExitCode, localOutput, localError) = await RunWasmtimeWithOutput(
            assets.Root, [.. environment, "--invoke", "local-offset()", component]);
        Assert.Equal(timeZone is null, localExitCode == 0);
        Assert.Equal(timeZone is null ? "0" : "", localOutput.Trim());
        Assert.Equal(timeZone is not null, localError.Contains("thrown Wasm exception", StringComparison.Ordinal));
    }

    private const string LibraryWorld = """
        package netwasm:probe@1.0.0;

        world local {
            import wasi:cli/environment@0.3.2;
            import wasi:filesystem/types@0.3.2;
            import wasi:filesystem/preopens@0.3.2;

            export plain: func(value: u32) -> u32;
            export local-offset: async func() -> s32;
        }
        """;

    private const string LibrarySource = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;

        namespace NetWasm.Wit.Netwasm.Probe._1._0._0;

        internal static class Startup
        {
            internal static uint Runs;

            [ModuleInitializer]
            internal static void Initialize() => Runs++;
        }

        public static partial class LocalExports
        {
            public static partial uint Plain(uint value) => value + Startup.Runs * 100 + 1;

            public static async partial Task<int> LocalOffset()
            {
                await Task.Yield();
                return (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes;
            }

            public static int Run(int value) => value;

            public static void Main()
            {
            }
        }
        """;

    private static async Task<string> PackageLibrary(TestAssets assets)
    {
        var wit = Path.Combine(assets.Directory, "wit");
        Directory.CreateDirectory(wit);
        File.WriteAllText(Path.Combine(wit, "world.wit"), LibraryWorld);
        foreach (var package in new[] { "cli", "clocks", "filesystem", "random", "sockets" })
        {
            CopyWitDirectory(
                Path.Combine(assets.Root, "wit", "wasi-0.3.2", package),
                Path.Combine(wit, "deps", $"wasi-{package}"));
        }
        using var services = new ServiceCollection().AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var document = services.GetRequiredService<IWitDocumentReader>().Read(wit);
        var world = document.SelectWorld("netwasm:probe/local@1.0.0");
        var bindings = NetWasm.Wit.Bindings.WitBindingCompositionRoot.Create().Generate(document, world);
        var assembly = CompileExecutable(assets, true, LibrarySource, bindings);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "NetWasm.Wit.Netwasm.Probe._1._0._0.LocalExports", "Run", [],
            WasmTarget.Wasm32, WitPath: wit, WitWorld: "netwasm:probe/local@1.0.0"));
        var application = Path.Combine(assets.Directory, "application.wasm");
        var runtime = Path.Combine(assets.Directory, "runtime.wasm");
        var layout = Path.Combine(assets.Directory, "runtime-layout.json");
        var component = Path.Combine(assets.Directory, "library.component.wasm");
        File.WriteAllBytes(application, compilation.ApplicationModule);
        File.WriteAllText(layout, JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            target = "wasm32",
            applicationStaticDataEnd = compilation.StaticDataEnd,
        }));
        await RunComponentTool("bash", assets.Root,
            Path.Combine(assets.Root, "eng", "build-netwasm-runtime.sh"),
            "--runtime-layout", layout, "--output", runtime,
            "--target", "wasm32", "--configuration", "release");
        services.GetRequiredService<IComponentPackager>().Package(new(
            application, wit, services.GetRequiredService<IWitWorldSpecifierFormatter>().Format(world), component,
            ComponentTarget.Wasm32Wasi02, runtime, Optimization: FinalWasmOptimization.None));
        return component;
    }
}
