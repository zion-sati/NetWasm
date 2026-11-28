// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Debugging;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A Debug command component carries what Wasmtime reads: a trap's backtrace (here an out-of-bounds store) names the
// managed frames and, from the DWARF line table, the C# file and line each frame stopped at.
public sealed class DebugInformationWasmtimeTests
{
    private const string Source = """
        using System;

        namespace DebugFixture;

        internal static unsafe class Program
        {
            private static int Main()
            {
                var value = Environment.ProcessorCount;
                Fail(value);
                return 0;
            }

            private static void Fail(int value)
            {
                if (value > 0)
                    *(int*)(nint)0x7ffffff0 = value;
            }
        }
        """;

    // Every project compiles its async methods with runtime async: the trap here is in the code the lowering resumes
    // into after Task.Yield, which the line table still maps to the method and its C# line.
    private const string AsyncSource = """
        using System;
        using System.Threading.Tasks;

        namespace DebugFixture;

        internal static class Program
        {
            private static async Task<int> Main()
            {
                var value = Environment.ProcessorCount;
                await Fail(value);
                return 0;
            }

            private static async Task Fail(int value)
            {
                await Task.Yield();
                if (value > 0)
                    Store(value);
            }

            private static unsafe void Store(int value) => *(int*)(nint)0x7ffffff0 = value;
        }
        """;

    [Fact]
    public async Task ATrapAfterAnAwaitNamesTheResumedMethodWithItsSourceLine()
    {
        using var assets = TestAssets.Create();
        var component = await PackageDebugProgram(assets, debug: true, AsyncSource, runtimeAsync: true);

        var (exitCode, error) = await RunWasmtime(assets.Root, component);

        Assert.NotEqual(0, exitCode);
        Assert.True(Regex.IsMatch(error, @"DebugFixture\.Program\.Store\s+at \S*DebugFixture\.cs:22:"), error);
        Assert.True(Regex.IsMatch(error, @"DebugFixture\.Program\.Fail\s+at \S*DebugFixture\.cs:19:"), error);
        // CoreLib's frames, whose documents are Source Link URLs, are named with no file under the project directory.
        Assert.Contains("RuntimeAsync.Resume", error, StringComparison.Ordinal);
        Assert.DoesNotContain("://", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrapBacktraceNamesManagedFramesWithTheirSourceLines()
    {
        using var assets = TestAssets.Create();
        var component = await PackageDebugProgram(assets, debug: true);

        var (exitCode, error) = await RunWasmtime(assets.Root, component);

        Assert.NotEqual(0, exitCode);
        Assert.True(error.Contains("DebugFixture.Program.Fail", StringComparison.Ordinal), error);
        Assert.Contains("DebugFixture.Program.Main", error, StringComparison.Ordinal);
        // The store in Fail, then the call in Main.
        Assert.Matches(@"DebugFixture\.Program\.Fail\s+at \S*Program\.cs:17:", error);
        Assert.Matches(@"DebugFixture\.Program\.Main\s+at \S*Program\.cs:10:", error);
    }

    // LLDB under Wasmtime stops at a C# line breakpoint, which binds once Wasmtime has compiled the module, and its
    // backtrace shows the managed frames with their lines. NETWASM_LLDB names the debugger, or lldb on PATH.
    [Fact]
    public async Task LldbStopsAtACSharpLineAndShowsTheManagedBacktrace()
    {
        var lldb = Environment.GetEnvironmentVariable("NETWASM_LLDB") is { Length: > 0 } configured
            ? configured
            : (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)
                .Select(directory => Path.Combine(directory, "lldb")).FirstOrDefault(File.Exists);
        Assert.SkipWhen(lldb is null, "LLDB is not installed; set NETWASM_LLDB to its path.");
        using var assets = TestAssets.Create();
        var component = await PackageDebugProgram(assets, debug: true);

        var start = new ProcessStartInfo(lldb!)
        {
            WorkingDirectory = assets.Directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList =
            {
                "-b",
                "-o", "settings set plugin.jit-loader.gdb.enable on",
                "-o", "breakpoint set --file Program.cs --line 17",
                "-o", "run",
                "-o", "bt 2",
                "--", "wasmtime", "run", "-C", "cache=n", "-D", "debug-info=y", "-O", "opt-level=0", component,
            },
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("LLDB did not start.");
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var session = await output + await error;

        Assert.Contains("stop reason = breakpoint 1.1", session, StringComparison.Ordinal);
        Assert.Matches(@"frame #0: .*DebugFixture\.Program\.Fail\(\) at Program\.cs:17", session);
        Assert.Matches(@"frame #1: .*DebugFixture\.Program\.Main\(\) at Program\.cs:10", session);
    }

    [Fact]
    public async Task ProductComponentBacktraceNamesNothing()
    {
        using var assets = TestAssets.Create();
        var component = await PackageDebugProgram(assets, debug: false);

        var (exitCode, error) = await RunWasmtime(assets.Root, component);

        Assert.NotEqual(0, exitCode);
        Assert.DoesNotContain("DebugFixture.Program", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Program.cs", error, StringComparison.Ordinal);
    }

    private static async Task<string> PackageDebugProgram(
        TestAssets assets, bool debug, string programSource = Source, bool runtimeAsync = false)
    {
        // Runtime async needs the SDK's compiler (the tests' Roslyn package predates it), which names the file after
        // the assembly.
        var assembly = runtimeAsync
            ? assets.CompileSourceWithCompilerAndPdb("DebugFixture", programSource, ["runtime-async=on"], allowUnsafe: true)
            : CompileWithRoslyn(assets, programSource);
        // A library has no entry point token: async Main is entered by name, and completes asynchronously.
        var entry = runtimeAsync ? MainByName(assembly) : ReadEntryPoint(assembly);
        var compiled = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], entry.TypeName, entry.MethodName, [], WasmTarget.Wasm32,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EntryMethodToken: entry.Token,
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable,
            Optimization: FinalWasmOptimization.O0,
            EmitStackTrace: debug,
            ProjectDirectory: assets.Directory));
        var application = Path.Combine(assets.Directory, "application.wasm");
        var symbols = Path.Combine(assets.Directory, "application.wasm.method-symbols");
        var runtime = Path.Combine(assets.Directory, "runtime.wasm");
        var layout = Path.Combine(assets.Directory, "runtime-layout.json");
        var component = Path.Combine(assets.Directory, "debug.component.wasm");
        File.WriteAllBytes(application, compiled.ApplicationModule);
        if (debug) File.WriteAllBytes(symbols, compiled.MethodSymbols!.Bytes);
        RuntimeLayoutFiles.Write(layout, compiled);
        await Run("bash", assets.Root, Path.Combine(assets.Root, "eng", "build-netwasm-runtime.sh"),
            "--runtime-layout", layout, "--output", runtime, "--target", "wasm32", "--configuration", "release");
        var toolchainWit = Path.Combine(assets.Root, "src", "NetWasm.Toolchain", "wit");
        using var services = new ServiceCollection().AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var world = services.GetRequiredService<IWitDocumentReader>().Read(toolchainWit)
            .SelectWorld("netwasm:command/command@1.0.0");
        services.GetRequiredService<IComponentPackager>().Package(new(
            application, toolchainWit, services.GetRequiredService<IWitWorldSpecifierFormatter>().Format(world), component,
            ComponentTarget.Wasm32Wasi02, runtime,
            new(ManagedExecutableParameterShape.None, ManagedExecutableReturnShape.ExitCode,
                runtimeAsync ? ManagedExecutableCompletionShape.Asynchronous : ManagedExecutableCompletionShape.Synchronous),
            FinalWasmOptimization.O0)
        {
            DebugInformation = debug ? new(symbols, assets.Directory) : null,
        });
        return component;
    }

    private static async Task<(int ExitCode, string Error)> RunWasmtime(string workingDirectory, string component)
    {
        var start = new ProcessStartInfo("wasmtime")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList = { "run", "-C", "cache=n", "-D", "debug-info=y", component },
            Environment = { ["WASMTIME_BACKTRACE_DETAILS"] = "1" },
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Wasmtime did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await ProcessExits.WithinLimit(process, "wasmtime running " + component);
        return (process.ExitCode, await output + await error);
    }

    private static async Task Run(string executable, string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{executable} did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await output + await error);
    }

    private static string CompileWithRoslyn(TestAssets assets, string programSource)
    {
        var source = Path.Combine(assets.Directory, "Program.cs");
        File.WriteAllText(source, programSource);
        var assembly = Path.Combine(assets.Directory, "DebugFixture.dll");
        var compilation = CSharpCompilation.Create(
            "DebugFixture",
            [CSharpSyntaxTree.ParseText(programSource, path: source, encoding: Encoding.UTF8)],
            assets.Framework.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: true));
        using (var peStream = File.Create(assembly))
        using (var pdbStream = File.Create(Path.ChangeExtension(assembly, ".pdb")))
        {
            var emitted = compilation.Emit(peStream, pdbStream,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        }
        return assembly;
    }

    private static (string TypeName, string MethodName, int Token) MainByName(string assemblyPath)
    {
        using var portableExecutable = new PEReader(File.OpenRead(assemblyPath));
        var metadata = portableExecutable.GetMetadataReader();
        return ("DebugFixture.Program", "Main", MetadataTokens.GetToken(metadata.MethodDefinitions.Single(handle =>
            metadata.GetString(metadata.GetMethodDefinition(handle).Name) == "Main")));
    }

    private static (string TypeName, string MethodName, int Token) ReadEntryPoint(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var portableExecutable = new PEReader(stream);
        var handle = MetadataTokens.EntityHandle(portableExecutable.PEHeaders.CorHeader!.EntryPointTokenOrRelativeVirtualAddress);
        var metadata = portableExecutable.GetMetadataReader();
        var method = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
        var type = metadata.GetTypeDefinition(method.GetDeclaringType());
        var @namespace = metadata.GetString(type.Namespace);
        var name = metadata.GetString(type.Name);
        return (string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}", metadata.GetString(method.Name),
            MetadataTokens.GetToken(handle));
    }
}
