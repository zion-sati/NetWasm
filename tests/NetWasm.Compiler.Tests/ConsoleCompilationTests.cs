using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ConsoleCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void ConsoleOutputForwardersAndRedirectionExecute(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.IO;
            using System.Text;

            namespace ConsoleSurfaceFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var output = new RecordingWriter();
                    var error = new RecordingWriter();
                    Console.SetOut(output);
                    Console.SetError(error);

                    Console.WriteLine();
                    Console.WriteLine(true);
                    Console.WriteLine('a');
                    Console.WriteLine(new[] { 'a', 'b' });
                    Console.WriteLine(new[] { 'a', 'b', 'c' }, 1, 2);
                    Console.WriteLine(1.25m);
                    Console.WriteLine(2.5d);
                    Console.WriteLine(3.5f);
                    Console.WriteLine(4);
                    Console.WriteLine(5u);
                    Console.WriteLine(6L);
                    Console.WriteLine(7UL);
                    Console.WriteLine((object?)8);
                    Console.WriteLine("nine");
                    Console.WriteLine("ten".AsSpan());
                    Console.WriteLine("{0}", 11);
                    Console.WriteLine("{0}{1}", 1, 2);
                    Console.WriteLine("{0}{1}{2}", 1, 2, 3);
                    Console.WriteLine("{0}", new object?[] { 12 });
                    Console.WriteLine("{0}", new ReadOnlySpan<object?>(new object?[] { 13 }));

                    Console.Write(true);
                    Console.Write('b');
                    Console.Write(new[] { 'c', 'd' });
                    Console.Write(new[] { 'e', 'f', 'g' }, 1, 2);
                    Console.Write(4.5d);
                    Console.Write(5.5m);
                    Console.Write(6.5f);
                    Console.Write(7);
                    Console.Write(8u);
                    Console.Write(9L);
                    Console.Write(10UL);
                    Console.Write((object?)11);
                    Console.Write("twelve");
                    Console.Write("thirteen".AsSpan());
                    Console.Write("{0}", 14);
                    Console.Write("{0}{1}", 1, 5);
                    Console.Write("{0}{1}{2}", 1, 2, 3);
                    Console.Write("{0}", new object?[] { 15 });
                    Console.Write("{0}", new ReadOnlySpan<object?>(new object?[] { 16 }));

                    return ReferenceEquals(Console.Out, output) &&
                        output.Writes > 0
                            ? input + 1
                            : 0;
                }

                private sealed class RecordingWriter : TextWriter
                {
                    public int Writes;
                    public override Encoding Encoding => Encoding.UTF8;
                    public override void Write(char value) => Writes++;
                    public override void Write(char[] buffer, int index, int count) => Writes++;
                    public override void Write(ReadOnlySpan<char> buffer) => Writes++;
                    public override void Write(string? value) => Writes++;
                }
            }
            """;

        var assembly = optimize
            ? assets.CompileOptimizedSource($"ConsoleSurface_{target}_{optimize}", source)
            : assets.CompileSource($"ConsoleSurface_{target}_{optimize}", source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConsoleSurfaceFixture.EntryPoint",
            "Run",
            [],
            target,
            WitPath: Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        var reachableNames = result.Program.Methods.Values
            .Select(method => method.Method.Definition.Name)
            .ToArray();
        Assert.Contains("SetOut", reachableNames);
        Assert.Contains("SetError", reachableNames);
        Assert.Contains("Write", reachableNames);
        Assert.Contains("WriteLine", reachableNames);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ConsoleWriteLineRetainsTargetWidthSafeWasiOutput(
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            $"ConsoleOutput{target}",
            """
            namespace ConsoleOutputFixture;

            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    global::System.Console.WriteLine(value);
                    return value;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConsoleOutputFixture.EntryPoint",
            "Run",
            [],
            target,
            WitPath: Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        var reachableMethods = result.Program.Methods.Values
            .Select(method => method.Method.Definition.Name)
            .ToArray();
        Assert.True(reachableMethods.Count(name => name == "WriteLine") >= 2);
        Assert.Contains("CreateStandardOutputWriter", reachableMethods);
        Assert.Contains("WriteToStream", reachableMethods);
        // Standard output is a WASI 0.3 stream: write-via-stream takes its readable end and each
        // blocking write is an async-lowered stream.write.
        Assert.Contains(result.Program.WitImportMethods, method =>
            method.WitImport is
            {
                InterfaceName: "wasi:cli/stdout@0.3.2",
                FunctionName: "write-via-stream",
            });
        Assert.Contains(result.Program.WitImportMethods, method =>
            method.WitImport is
            {
                InterfaceName: "wasi:cli/stdout@0.3.2",
                FunctionName: "[async-lower][stream-write-0]write-via-stream",
            });
        Assert.True(
            result.Program.Finalizers.IsEmpty,
            $"Console output unexpectedly retained {result.Program.Finalizers.Count} finalizer descriptors.");
        Assert.DoesNotContain(
            NetWasmRuntimeFeatureIds.Finalization,
            result.RuntimeFeatures);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ConsoleErrorRetainsOnlyTheStandardErrorProvider(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            $"ConsoleError{target}",
            """
            namespace ConsoleErrorFixture;

            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    global::System.Console.Error.WriteLine("failure");
                    return value;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConsoleErrorFixture.EntryPoint",
            "Run",
            [],
            target,
            WitPath: Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        Assert.Contains(result.Program.WitImportMethods, method =>
            method.WitImport is
            {
                InterfaceName: "wasi:cli/stderr@0.3.2",
                FunctionName: "write-via-stream",
            });
        Assert.DoesNotContain(result.Program.WitImportMethods, method =>
            method.WitImport!.InterfaceName == "wasi:cli/stdout@0.3.2");
        // The platform's other facades are not reached through its construction.
        Assert.DoesNotContain(result.Program.WitImportMethods, method =>
            method.WitImport!.InterfaceName.StartsWith("wasi:filesystem/", StringComparison.Ordinal) ||
            method.WitImport!.InterfaceName.StartsWith("wasi:random/", StringComparison.Ordinal));
    }

    // Standard input and terminal detection are platform services of their own: a program pays
    // for wasi:cli/stdin only when it reads, and for a terminal interface only when it asks
    // whether that stream is redirected.
    [Theory]
    [InlineData("ReadLine", "global::System.Console.ReadLine()?.Length ?? -1", "wasi:cli/stdin@0.3.2", "read-via-stream")]
    [InlineData("Read", "global::System.Console.Read()", "wasi:cli/stdin@0.3.2", "read-via-stream")]
    [InlineData("Stream", "global::System.Console.OpenStandardInput(16).ReadByte()", "wasi:cli/stdin@0.3.2", "read-via-stream")]
    [InlineData("InputRedirected", "global::System.Console.IsInputRedirected ? 1 : 0", "wasi:cli/terminal-stdin@0.3.2", "get-terminal-stdin")]
    [InlineData("ErrorRedirected", "global::System.Console.IsErrorRedirected ? 1 : 0", "wasi:cli/terminal-stderr@0.3.2", "get-terminal-stderr")]
    public void ConsoleInputAndTerminalsImportOnlyWhatTheProgramUses(
        string name, string expression, string interfaceName, string functionName)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ConsoleInput" + name,
            $$"""
            namespace ConsoleInputFixture;

            public static class EntryPoint
            {
                public static int Run(int value) => value + ({{expression}});
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConsoleInputFixture.EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        var imports = result.Program.WitImportMethods.Select(method => method.WitImport!).ToArray();
        Assert.Contains(imports, import => import.InterfaceName == interfaceName && import.FunctionName == functionName);
        var stdin = interfaceName == "wasi:cli/stdin@0.3.2";
        Assert.Equal(stdin, imports.Any(import => import.InterfaceName == "wasi:cli/stdin@0.3.2"));
        Assert.Equal(!stdin, imports.Any(import => import.InterfaceName.StartsWith("wasi:cli/terminal-", StringComparison.Ordinal)));
        Assert.DoesNotContain(imports, import => import.InterfaceName == "wasi:cli/stdout@0.3.2");
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, "cm32p2")]
    [InlineData(WasmTarget.Wasm64, "cm64p2")]
    public void ManagedExecutableRetainsRunAndCanonicalExportsWithWasiImports(
        WasmTarget target,
        string canonicalPrefix)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            $"ConsoleExecutable{target}",
            """
            namespace ConsoleExecutableFixture;

            public static class EntryPoint
            {
                public static void Main(string[] arguments)
                {
                    global::System.Console.WriteLine(42);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConsoleExecutableFixture.EntryPoint",
            "Main",
            [],
            target,
            WitPath: Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable));

        var exports = WasmModuleInspection.ReadExportNames(result.ApplicationModule);
        Assert.Contains("run", exports);
        Assert.Contains($"{canonicalPrefix}_memory", exports);
        Assert.Contains($"{canonicalPrefix}_realloc", exports);
        Assert.Contains($"{canonicalPrefix}_initialize", exports);
        Assert.Contains(result.Program.WitImportMethods, method =>
            method.WitImport is
            {
                InterfaceName: "wasi:cli/environment@0.3.2",
                FunctionName: "get-arguments",
            });
        Assert.True(
            result.Program.Finalizers.IsEmpty,
            $"Console executable unexpectedly retained {result.Program.Finalizers.Count} finalizer descriptors.");
        Assert.DoesNotContain(
            NetWasmRuntimeFeatureIds.Finalization,
            result.RuntimeFeatures);
    }

    [Fact]
    public void ParameterlessManagedExecutableDoesNotPayForCommandLineArguments()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ParameterlessConsoleExecutable",
            """
            namespace ConsoleExecutableFixture;

            public static class EntryPoint
            {
                public static void Main()
                {
                    global::System.Console.WriteLine(42);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConsoleExecutableFixture.EntryPoint",
            "Main",
            [],
            WasmTarget.Wasm32,
            WitPath: Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable));

        Assert.DoesNotContain(result.Program.WitImportMethods, method =>
            method.WitImport is
            {
                InterfaceName: "wasi:cli/environment@0.3.2",
                FunctionName: "get-arguments",
            });
    }
}
