using System.Text.RegularExpressions;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ManagedExecutableAsyncMainCompilationTests
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
    public void CompilerResumesAsynchronousManagedEntryPoint(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assemblyPath = CompileExecutable(assets, optimize);
        var entryPoint = ReadEntryPoint(assemblyPath);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assemblyPath,
            [assets.CoreLib],
            entryPoint.TypeName,
            entryPoint.MethodName,
            [],
            target,
            WitPath: Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EntryMethodToken: entryPoint.Token,
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable));

        var asyncBinding = Assert.Single(
            compilation.Program.JavaScriptAsyncBindings);
        Assert.Equal(compilation.Program.EntryPoint!.Key, asyncBinding.Key);
        Assert.True(asyncBinding.Value.Return.HasResult);

        CompilerTestSupport.ValidateWithNode(
            compilation.ApplicationModule,
            assets.Directory);
        var result = CompilerTestSupport.ExecuteWithStandardWasiNode(
            compilation.ApplicationModule,
            assets.Directory,
            0,
            target,
            compilation.StaticDataEnd,
            callbackEntry: true,
            entryExportName: "run");

        Assert.Equal(0, result);
    }

    public static TheoryData<bool, bool, bool, int> ComponentConfigurations
    {
        get
        {
            var configurations = new TheoryData<bool, bool, bool, int>();
            foreach (var optimize in new[] { false, true })
                foreach (var returnsExitCode in new[] { false, true })
                    foreach (var hasArguments in new[] { false, true })
                        foreach (var status in new[] { 1, 2, 3 })
                            configurations.Add(optimize, returnsExitCode, hasArguments, status);
            return configurations;
        }
    }

    // An asynchronous Main is a callback-lifted wasi:cli/run (and diagnostics' run) whatever it
    // returns or throws. Wasmtime drives it: a completed Main returns ok, or exits with a non-zero
    // exit code through wasi:cli/exit, and a fault or cancellation is written and returns err.
    [Theory]
    [MemberData(nameof(ComponentConfigurations))]
    public async Task AsynchronousMainCompletesAsACallbackLiftedRun(
        bool optimize, bool returnsExitCode, bool hasArguments, int expectedStatus)
    {
        using var assets = TestAssets.Create();
        var completion = expectedStatus switch
        {
            1 => returnsExitCode ? "return 17;" : "return;",
            2 => "throw new System.InvalidOperationException();",
            3 => "throw new System.OperationCanceledException();",
            _ => throw new ArgumentOutOfRangeException(nameof(expectedStatus)),
        };
        var source = $$"""
            using System.Threading.Tasks;
            namespace ManagedExecutableAsyncMainFixture;
            internal static class Program
            {
                public static async {{(returnsExitCode ? "Task<int>" : "Task")}} Main({{(hasArguments ? "string[] arguments" : "")}})
                {
                    {{(hasArguments ? "if (arguments.Length != 0) throw new System.InvalidOperationException();" : "")}}
                    await Task.Delay(20);
                    await Task.Yield();
                    {{completion}}
                }
            }
            """;
        var component = await PackageCommand(assets, optimize, source, new(
            hasArguments ? ManagedExecutableParameterShape.StringArray : ManagedExecutableParameterShape.None,
            returnsExitCode ? ManagedExecutableReturnShape.ExitCode : ManagedExecutableReturnShape.Void,
            ManagedExecutableCompletionShape.Asynchronous));
        using var services = new ServiceCollection().AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var wasmTools = services.GetRequiredService<IWasmTools>();
        var validation = wasmTools.Run("validate", component, "--features", "all");
        Assert.True(validation.ExitCode == 0, validation.StandardError);
        var printed = wasmTools.Run("print", component);
        Assert.Equal(0, printed.ExitCode);
        Assert.DoesNotMatch(@"\(import\s+""(?:wasi_snapshot_preview1|wasi_unstable)""", printed.StandardOutput);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(
            printed.StandardOutput, @"\(canon lift [^\n]*async \(callback"));

        var (exitCode, error) = await RunWasmtime(assets.Root, component);

        // A Task<int> Main's code is the exit code, as on desktop .NET; a fault exits 1.
        var expectedExitCode = expectedStatus == 1 ? returnsExitCode ? 17 : 0 : 1;
        Assert.True(exitCode == expectedExitCode, $"exit code {exitCode}: {error}");
        Assert.Equal(expectedStatus != 1, error.Contains("Unhandled exception #", StringComparison.Ordinal));
    }

    // How a program ends, as desktop .NET ends it on Linux and macOS: an int Main's result and
    // Environment.ExitCode are the exit code, truncated to a process status's eight bits;
    // Environment.Exit ends the process at once, whatever the code; and FailFast reports on standard
    // error and ends the process as an abort does, with status 134, before any catch can run.
    public static TheoryData<string, bool, string, int, string, string> ProcessExitCases => new()
    {
        { "int", false, "System.Console.WriteLine(\"main\"); return 3;", 3, "main\n", "" },
        { "int", false, "return 259;", 3, "", "" },
        { "int", false, "System.Environment.ExitCode = 9; return 0;", 0, "", "" },
        { "void", false, "System.Environment.ExitCode = 7;", 7, "", "" },
        { "void", false, "System.Console.WriteLine(\"before\"); System.Environment.Exit(5); System.Console.WriteLine(\"after\");", 5, "before\n", "" },
        { "void", false, "System.Environment.ExitCode = 9; System.Environment.Exit(0);", 0, "", "" },
        { "void", false, "try { System.Environment.FailFast(\"boom\"); } catch { System.Console.WriteLine(\"caught\"); }", 134, "", "Process terminated.\nboom\n" },
        // The exception reads as .NET's Exception.ToString does, its type as type#<id> since a program
        // carries no type names (issue 225); desktop .NET writes "System.InvalidOperationException: inner".
        { "void", false, "System.Environment.FailFast(null, new System.InvalidOperationException(\"inner\"));", 134, "",
            "Process terminated.\nRuntime operation halted by call to System.Environment.FailFast(). (0x80131623)\ntype#<id>: inner\n" },
        { "System.Threading.Tasks.Task", true, "await System.Threading.Tasks.Task.Yield(); System.Environment.ExitCode = 6;", 6, "", "" },
        { "System.Threading.Tasks.Task", true, "await System.Threading.Tasks.Task.Yield(); System.Environment.Exit(8);", 8, "", "" },
    };

    [Theory]
    [MemberData(nameof(ProcessExitCases))]
    public async Task ProgramsEndWithTheExitCodeDesktopDotNetReports(
        string returnType, bool asynchronous, string body, int expectedExitCode, string expectedOutput, string expectedError)
    {
        using var assets = TestAssets.Create();
        var source = $$"""
            namespace ManagedExecutableAsyncMainFixture;
            internal static class Program
            {
                public static {{(asynchronous ? "async " : "")}}{{returnType}} Main()
                {
                    {{body}}
                }
            }
            """;
        var component = await PackageCommand(assets, true, source, new(
            ManagedExecutableParameterShape.None,
            returnType == "int" ? ManagedExecutableReturnShape.ExitCode : ManagedExecutableReturnShape.Void,
            asynchronous ? ManagedExecutableCompletionShape.Asynchronous : ManagedExecutableCompletionShape.Synchronous));

        var (exitCode, output, error) = await RunWasmtimeWithOutput(assets.Root, component);

        Assert.True(exitCode == expectedExitCode, $"exit code {exitCode}: {error}");
        Assert.Equal(expectedOutput, output);
        Assert.Equal(expectedError, Regex.Replace(error, @"type#\d+", "type#<id>"));
    }

    // Generated WASI 0.3 bindings in a command: a sent stream (stdout's write-via-stream) and
    // its future result, an element stream (a directory's entries) and a received byte stream
    // (a file's contents, opened through the async open-at method), driven by an async Main.
    [Fact]
    public async Task GeneratedStreamAndFutureBindingsRunInWasmtime()
    {
        using var assets = TestAssets.Create();
        var toolchainWit = Path.Combine(assets.Root, "src", "NetWasm.Toolchain", "wit");
        using var services = new ServiceCollection().AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var document = services.GetRequiredService<IWitDocumentReader>().Read(toolchainWit);
        var bindings = NetWasm.Wit.Bindings.WitBindingCompositionRoot.Create()
            .Generate(document, document.SelectWorld("wasi:cli/imports@0.3.2"));
        var assembly = CompileExecutable(assets, true, StreamProgram, bindings);
        var entry = ReadEntryPoint(assembly);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], entry.TypeName, entry.MethodName, [], WasmTarget.Wasm32,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EntryMethodToken: entry.Token,
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable));
        var application = Path.Combine(assets.Directory, "application.wasm");
        var runtime = Path.Combine(assets.Directory, "runtime.wasm");
        var layout = Path.Combine(assets.Directory, "runtime-layout.json");
        var component = Path.Combine(assets.Directory, "streams.component.wasm");
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
        var world = document.SelectWorld("netwasm:command/command@1.0.0");
        services.GetRequiredService<IComponentPackager>().Package(new(
            application, toolchainWit,
            services.GetRequiredService<IWitWorldSpecifierFormatter>().Format(world), component,
            ComponentTarget.Wasm32Wasi02, runtime,
            new(ManagedExecutableParameterShape.None, ManagedExecutableReturnShape.ExitCode,
                ManagedExecutableCompletionShape.Asynchronous),
            FinalWasmOptimization.None));
        var data = Path.Combine(assets.Directory, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "greeting.txt"), "streamed file contents\n");
        File.WriteAllText(Path.Combine(data, "second.txt"), "x");

        var (exitCode, output, error) = await RunWasmtimeWithOutput(assets.Root, "--dir", $"{data}::/data", component);

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}");
        Assert.Equal(
            "sent through a generated stream binding\n" +
            "preopen /data: [greeting.txt, second.txt]; file: streamed file contents\n",
            output);
    }

    // Names an exception the same way on NetWasm and desktop .NET: by type test and nameof, since a NetWasm program
    // carries no type names unless it is built with them (issue 225). The fixture's own types come first.
    private static string ExceptionKindSource(params string[] fixtureTypes)
    {
        string[] types =
        [
            .. fixtureTypes,
            "System.Threading.Tasks.TaskCanceledException", "System.OperationCanceledException",
            "System.ObjectDisposedException", "System.InvalidOperationException",
            "System.ArgumentOutOfRangeException", "System.ArgumentNullException", "System.ArgumentException",
            "System.PlatformNotSupportedException", "System.NotSupportedException", "System.AggregateException",
            "System.TimeoutException", "System.FormatException", "System.Threading.Tasks.TaskSchedulerException",
        ];
        var arms = string.Concat(types.Select(type => $"            {type} => nameof({type}),\n"));
        return $$"""

            internal static class ExceptionKind
            {
                public static string Of(System.Exception? exception) => exception switch
                {
                    null => "",
            {{arms}}            _ when exception.GetType() == typeof(System.Exception) => nameof(System.Exception),
                    _ => "an exception the fixture does not name",
                };
            }
            """;
    }

    private const string StreamProgram = """
        using System;
        using System.IO;
        using System.Text;
        using System.Threading.Tasks;
        using NetWasm.Wit.Wasi.Cli._0._3._2;

        internal static class Program
        {
            private static async Task<int> Main()
            {
                var written = await StdoutImports.WriteViaStream(
                    new MemoryStream(Encoding.UTF8.GetBytes("sent through a generated stream binding\n")));
                var (directory, path) = PreopensImports.GetDirectories()[0];
                var (entries, listed) = WasiFilesystemTypesImports.ReadDirectory(directory);
                var names = new System.Collections.Generic.List<string>();
                await foreach (var entry in entries)
                {
                    names.Add(entry.Name);
                }
                names.Sort(StringComparer.Ordinal);
                await listed;
                var opened = await WasiFilesystemTypesImports.OpenAt(directory, default, "greeting.txt", default, default);
                var (contents, read) = WasiFilesystemTypesImports.ReadViaStream(opened.Ok, 0);
                using var reader = new StreamReader(contents);
                var text = await reader.ReadToEndAsync();
                var readResult = await read;
                Console.WriteLine($"preopen {path}: [{string.Join(", ", names)}]; file: {text.Trim()}");
                return written.IsOk && readResult.IsOk ? 0 : 1;
            }
        }
        """;

    // A synchronous Main reads piped standard input line by line through Console.ReadLine (more
    // than one read buffer of it, with multi-byte UTF-8 split across copies), then finds the end
    // of input again through Console.Read and a standard input stream; neither stream is a
    // terminal.
    [Fact]
    public async Task ConsoleReadsPipedStandardInputInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.Text;
            namespace ConsoleInputFixture;
            internal static class Program
            {
                public static int Main()
                {
                    var count = 0;
                    var characters = 0;
                    string? first = null;
                    string? last = null;
                    string? line;
                    while ((line = Console.ReadLine()) is not null)
                    {
                        first ??= line;
                        last = line;
                        count++;
                        characters += line.Length;
                    }

                    using var input = Console.OpenStandardInput();
                    Console.WriteLine($"{count} {characters} [{first}] [{last}]");
                    Console.WriteLine($"end: {Console.Read()} {input.Read(new byte[4], 0, 4)} {input.CanRead} {input.CanWrite}");
                    Console.WriteLine($"redirected: {Console.IsInputRedirected} {Console.IsOutputRedirected}");
                    return 0;
                }
            }
            """);
        var entry = ReadEntryPoint(assembly);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], entry.TypeName, entry.MethodName, [], WasmTarget.Wasm32,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EntryMethodToken: entry.Token,
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable));
        var application = Path.Combine(assets.Directory, "application.wasm");
        var runtime = Path.Combine(assets.Directory, "runtime.wasm");
        var layout = Path.Combine(assets.Directory, "runtime-layout.json");
        var component = Path.Combine(assets.Directory, "input.component.wasm");
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
        var toolchainWit = Path.Combine(assets.Root, "src", "NetWasm.Toolchain", "wit");
        using var services = new ServiceCollection().AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var world = services.GetRequiredService<IWitDocumentReader>().Read(toolchainWit)
            .SelectWorld("netwasm:command/command@1.0.0");
        services.GetRequiredService<IComponentPackager>().Package(new(
            application, toolchainWit,
            services.GetRequiredService<IWitWorldSpecifierFormatter>().Format(world), component,
            ComponentTarget.Wasm32Wasi02, runtime,
            new(ManagedExecutableParameterShape.None, ManagedExecutableReturnShape.ExitCode,
                ManagedExecutableCompletionShape.Synchronous),
            FinalWasmOptimization.None));
        var lines = Enumerable.Range(1, 1500).Select(index => $"line {index} caf\u00e9").ToArray();
        var input = string.Join("\r\n", lines.Take(2)) + "\n" + string.Join("\n", lines.Skip(2));

        var (exitCode, output, error) = await RunWasmtimeWithInput(assets.Root, input, component);

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}");
        Assert.Equal(
            $"1500 {lines.Sum(line => line.Length)} [line 1 caf\u00e9] [line 1500 caf\u00e9]\n" +
            "end: -1 0 True False\n" +
            "redirected: True True\n",
            output);
    }

    // File, FileInfo, Directory, FileStream and the current directory on a preopened directory:
    // creating, writing, appending, reading, seeking, copying, moving, enumerating, timestamps,
    // recursive deletion, relative paths, and the exceptions .NET throws on Unix.
    // The console writes text as .NET's UTF-8 console encoding does: two-, three- and four-byte
    // characters, an unpaired surrogate as U+FFFD, and texts longer than the 4,096 characters the
    // writer encodes at once: a surrogate pair that no longer fits the piece's 12,288 bytes, an
    // unpaired surrogate at the end, and three-byte characters that fill each piece exactly.
    [Fact]
    public async Task ConsoleWritesUtf8AsDotNetDoes()
    {
        string[] texts =
        [
            "ascii", "caf\u00e9 \u4e2d", "\uD83D\uDE00", "\uD800", "a\uDC00b", "\uD800\uD83D\uDE00", "",
            new string('x', 12287) + "\uD83D\uDE00" + new string('y', 5000) + "\uD83D",
            new string('\u4e2d', 5000),
        ];
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            namespace ConsoleUtf8Fixture;
            internal static class Program
            {
                private static int Main()
                {
                    string[] texts =
                    [
                        "ascii", "caf\u00e9 \u4e2d", "\uD83D\uDE00", "\uD800", "a\uDC00b", "\uD800\uD83D\uDE00", "",
                        new string('x', 12287) + "\uD83D\uDE00" + new string('y', 5000) + "\uD83D",
                        new string('\u4e2d', 5000),
                    ];
                    foreach (var text in texts) Console.WriteLine(text);
                    Console.Write(texts[1].AsSpan());
                    Console.Write(new[] { '-', 'a', '\u00e9', '-' }, 1, 2);
                    Console.Write(texts[7]);
                    return 0;
                }
            }
            """);
        var component = await PackageProgram(assets, assembly, ManagedExecutableCompletionShape.Synchronous);

        var (exitCode, output, error) = await RunWasmtimeWithOutputBytes(assets.Root, component);

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}");
        var expected = string.Concat(texts.Select(text => text + "\n")) + texts[1] + "a\u00e9" + texts[7];
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(expected), output);
    }

    // A void Main exits with the Environment.ExitCode it sets, as on .NET.
    [Fact]
    public async Task VoidMainExitsWithTheExitCodeItSetsInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            namespace ExitCodeFixture;
            internal static class Program
            {
                private static void Main()
                {
                    Console.WriteLine(1);
                    Environment.ExitCode = 3;
                }
            }
            """);
        var component = await PackageProgram(
            assets, assembly, ManagedExecutableCompletionShape.Synchronous, ManagedExecutableReturnShape.Void);

        var (exitCode, output, error) = await RunWasmtimeWithOutput(assets.Root, component);

        Assert.True(exitCode == 3, $"exit code {exitCode}: {error}");
        Assert.Equal("1\n", output);
    }

    // A void Main that never sets Environment.ExitCode reads 0 and ends through wasi:cli/run's
    // result, as an int Main returning 0 does, so its component imports no wasi:cli/exit; one that
    // sets the exit code still imports it.
    [Theory]
    [InlineData("", false)]
    [InlineData("Environment.ExitCode = 3;", true)]
    public async Task VoidMainImportsProcessExitOnlyWhenItSetsTheExitCode(string statement, bool importsExit)
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, $$"""
            using System;
            namespace ExitImportFixture;
            internal static class Program
            {
                private static void Main()
                {
                    Console.WriteLine(42);
                    {{statement}}
                }
            }
            """);
        var component = await PackageProgram(assets, assembly, ManagedExecutableCompletionShape.Synchronous,
            ManagedExecutableReturnShape.Void, FinalWasmOptimization.Oz);

        var wit = await RunComponentTool("wasm-tools", assets.Root, "component", "wit", component);

        Assert.Equal(importsExit, wit.Contains("import wasi:cli/exit@0.3.2;", StringComparison.Ordinal));
    }

    // A Main that reads its arguments receives the command line after the program name, as on .NET.
    [Fact]
    public async Task MainThatReadsItsArgumentsReceivesThemInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            namespace ArgumentsFixture;
            internal static class Program
            {
                private static int Main(string[] args)
                {
                    Console.WriteLine(string.Join("|", args));
                    return args.Length;
                }
            }
            """);
        var component = await PackageProgram(assets, assembly, ManagedExecutableCompletionShape.Synchronous);

        var (exitCode, output, error) = await RunWasmtimeWithOutput(assets.Root, component, "first", "second word");

        Assert.True(exitCode == 2, $"exit code {exitCode}: {error}");
        Assert.Equal("first|second word\n", output);
    }

    [Fact]
    public async Task FileApisWorkOnAPreopenedDirectoryInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.IO;
            namespace FileFixture;
            internal static class Program
            {
                public static int Main()
                {
                    var root = "/data/work";
                    Directory.CreateDirectory(Path.Combine(root, "a", "b"));
                    File.WriteAllText(Path.Combine(root, "a", "note.txt"), "caf\u00e9");
                    File.AppendAllText(Path.Combine(root, "a", "note.txt"), " au lait");
                    Console.WriteLine($"text: {File.ReadAllText(Path.Combine(root, "a", "note.txt"))}");
                    Console.WriteLine($"length: {new FileInfo(Path.Combine(root, "a", "note.txt")).Length}");

                    using (var stream = new FileStream(Path.Combine(root, "data.bin"), FileMode.Create, FileAccess.ReadWrite))
                    {
                        var bytes = new byte[10000];
                        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)i;
                        stream.Write(bytes);
                        stream.Seek(5000, SeekOrigin.Begin);
                        var buffer = new byte[3];
                        stream.ReadExactly(buffer);
                        Console.WriteLine($"stream: {stream.Length} {stream.Position} {buffer[0]} {buffer[2]}");
                        stream.SetLength(100);
                    }

                    Console.WriteLine($"truncated: {new FileInfo(Path.Combine(root, "data.bin")).Length}");
                    File.Copy(Path.Combine(root, "a", "note.txt"), Path.Combine(root, "copy.txt"));
                    File.Move(Path.Combine(root, "copy.txt"), Path.Combine(root, "a", "b", "moved.txt"));
                    var listed = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
                    for (var i = 0; i < listed.Length; i++) listed[i] = Path.GetRelativePath(root, listed[i]);
                    Array.Sort(listed, StringComparer.Ordinal);
                    Console.WriteLine($"files: {string.Join(",", listed)}");
                    var dirs = new System.Collections.Generic.List<string>();
                    foreach (var directory in Directory.EnumerateDirectories(root)) dirs.Add(Path.GetFileName(directory));
                    Console.WriteLine($"dirs: {string.Join(",", dirs)}");
                    var texts = Directory.GetFiles(Path.Combine(root, "a"), "*.txt");
                    for (var i = 0; i < texts.Length; i++) texts[i] = Path.GetFileName(texts[i]);
                    Console.WriteLine($"txt: {string.Join(",", texts)}");

                    var when = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                    File.SetLastWriteTimeUtc(Path.Combine(root, "data.bin"), when);
                    Console.WriteLine($"time: {File.GetLastWriteTimeUtc(Path.Combine(root, "data.bin")) == when}");

                    Environment.CurrentDirectory = Path.Combine(root, "a");
                    Console.WriteLine($"cwd: {Directory.GetCurrentDirectory()} {Path.GetFullPath("b/moved.txt")} {File.Exists("note.txt")}");

                    try { File.ReadAllText(Path.Combine(root, "missing.txt")); }
                    catch (FileNotFoundException) { Console.WriteLine("missing: FileNotFoundException"); }
                    try { File.ReadAllText(Path.Combine(root, "nowhere", "x.txt")); }
                    catch (DirectoryNotFoundException) { Console.WriteLine("nowhere: DirectoryNotFoundException"); }
                    try { File.Copy(Path.Combine(root, "data.bin"), Path.Combine(root, "a", "note.txt")); }
                    catch (IOException e) when (e is not FileNotFoundException) { Console.WriteLine("exists: IOException"); }

                    File.Delete(Path.Combine(root, "data.bin"));
                    Directory.Delete(root, recursive: true);
                    Console.WriteLine($"gone: {File.Exists(Path.Combine(root, "data.bin"))} {Directory.Exists(root)} {Directory.Exists("/data")}");
                    return 0;
                }
            }
            """);
        var data = Path.Combine(assets.Directory, "data");
        Directory.CreateDirectory(data);

        var (exitCode, output, error) = await RunFileProgramInWasmtime(assets, assembly, "--dir", $"{data}::/data");

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}");
        Assert.Equal(
            "text: caf\u00e9 au lait\n" +
            "length: 13\n" +
            "stream: 10000 5003 136 138\n" +
            "truncated: 100\n" +
            "files: a/b/moved.txt,a/note.txt,data.bin\n" +
            "dirs: a\n" +
            "txt: note.txt\n" +
            "time: True\n" +
            "cwd: /data/work/a /data/work/a/b/moved.txt True\n" +
            "missing: FileNotFoundException\n" +
            "nowhere: DirectoryNotFoundException\n" +
            "exists: IOException\n" +
            "gone: False False True\n",
            output);
        Assert.Empty(Directory.EnumerateFileSystemEntries(data));
    }

    // The edge cases of upstream's file APIs, against desktop .NET's output for the same program
    // on Linux: temporary files, symbolic links (a recursive delete removes a link, not what it
    // points to), "." and "..", enumeration options, non-ASCII names, a file larger than one
    // stream read, FileStream modes, DeleteOnClose, moves, File.Replace and the exceptions.
    [Fact]
    public async Task FileApiEdgeCasesMatchDesktopDotNetInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.Collections.Generic;
            using System.IO;

            namespace FileEdgeFixture;

            internal static class Program
            {
                private static string root = "";

                private static string Rel(string path) => Path.GetRelativePath(root, path);

                private static string Try(Action action)
                {
                    try
                    {
                        action();
                        return "ok";
                    }
                    catch (Exception e)
                    {
                        // The exception's name, from its type: a program carries no type names (issue 225).
                        return e switch
                        {
                            FileNotFoundException => "FileNotFoundException",
                            DirectoryNotFoundException => "DirectoryNotFoundException",
                            PathTooLongException => "PathTooLongException",
                            IOException => "IOException",
                            UnauthorizedAccessException => "UnauthorizedAccessException",
                            PlatformNotSupportedException => "PlatformNotSupportedException",
                            NotSupportedException => "NotSupportedException",
                            ArgumentException => "ArgumentException",
                            _ => "other",
                        };
                    }
                }

                private static string Sorted(IEnumerable<string> paths)
                {
                    var list = new List<string>();
                    foreach (var path in paths) list.Add(Rel(path));
                    list.Sort(StringComparer.Ordinal);
                    return string.Join(",", list);
                }

                public static int Main()
                {
                    root = Environment.GetEnvironmentVariable("FILE_ROOT") ?? "/data/work";
                    var keep = Path.Combine(Path.GetDirectoryName(root)!, "keep");
                    Directory.CreateDirectory(keep);
                    File.WriteAllText(Path.Combine(keep, "kept.txt"), "kept");
                    var a = Path.Combine(root, "a");
                    Directory.CreateDirectory(a);
                    File.WriteAllText(Path.Combine(a, "note.txt"), "hello");

                    Console.WriteLine($"dots: {File.ReadAllText(root + "/./a/../a/note.txt")} {Rel(Path.GetFullPath(root + "/a/./b/../note.txt"))} {Rel(Directory.GetParent(a)!.FullName)} {new DirectoryInfo(a).Parent!.Name}");
                    Console.WriteLine($"entries: {Sorted(Directory.EnumerateFileSystemEntries(root))}");

                    var temp = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
                    var tempFile = Path.GetTempFileName();
                    Console.WriteLine($"temp: {Path.GetDirectoryName(tempFile) == temp} {Path.GetExtension(tempFile)} {new FileInfo(tempFile).Length} {File.Exists(tempFile)}");
                    var tempDirectory = Directory.CreateTempSubdirectory("nw");
                    Console.WriteLine($"tempdir: {tempDirectory.Exists} {tempDirectory.Name.StartsWith("nw", StringComparison.Ordinal)} {Path.GetDirectoryName(tempDirectory.FullName) == temp}");
                    File.Delete(tempFile);
                    tempDirectory.Delete();
                    Console.WriteLine($"tempgone: {File.Exists(tempFile)} {Directory.Exists(tempDirectory.FullName)}");

                    var link = Path.Combine(root, "link.txt");
                    File.CreateSymbolicLink(link, "a/note.txt");
                    var info = new FileInfo(link);
                    Console.WriteLine($"link: {info.LinkTarget} {File.ReadAllText(link)} {info.Length} {(info.Attributes & FileAttributes.ReparsePoint) != 0} {Rel(File.ResolveLinkTarget(link, true)!.FullName)}");
                    var directoryLink = Path.Combine(root, "alink");
                    Directory.CreateSymbolicLink(directoryLink, "a");
                    Console.WriteLine($"dirlink: {Directory.Exists(directoryLink)} {File.Exists(Path.Combine(directoryLink, "note.txt"))} {Sorted(Directory.EnumerateFiles(directoryLink))}");
                    Directory.CreateSymbolicLink(Path.Combine(root, "keeplink"), "../keep");
                    Console.WriteLine($"recursive: {Sorted(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))}");

                    File.WriteAllText(Path.Combine(a, "Upper.TXT"), "");
                    File.WriteAllText(Path.Combine(a, "x.tmp"), "");
                    File.WriteAllText(Path.Combine(a, ".hidden"), "");
                    Console.WriteLine($"pattern: {Sorted(Directory.EnumerateFiles(a, "*.t?t"))} | {Sorted(Directory.EnumerateFiles(a, "*.txt", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }))} | {Sorted(Directory.EnumerateFiles(a, "*", new EnumerationOptions { AttributesToSkip = FileAttributes.Hidden }))}");
                    Console.WriteLine($"attributes: {(File.GetAttributes(Path.Combine(a, ".hidden")) & FileAttributes.Hidden) != 0} {File.GetAttributes(a)} {File.GetAttributes(Path.Combine(a, "x.tmp"))}");

                    var unicode = Path.Combine(a, "naïve-文件.bin");
                    var big = new byte[200_000];
                    for (var i = 0; i < big.Length; i++) big[i] = (byte)(i * 7);
                    File.WriteAllBytes(unicode, big);
                    var back = File.ReadAllBytes(unicode);
                    var same = back.Length == big.Length;
                    for (var i = 0; same && i < back.Length; i++) same = back[i] == big[i];
                    Console.WriteLine($"unicode: {Path.GetFileName(Directory.GetFiles(a, "na*")[0])} {back.Length} {same}");

                    var log = Path.Combine(root, "log.txt");
                    Console.WriteLine($"modes: {Try(() => new FileStream(log, FileMode.Open).Dispose())} {Try(() => File.WriteAllText(log, "one"))} {Try(() => new FileStream(log, FileMode.CreateNew).Dispose())}");
                    using (var stream = new FileStream(log, FileMode.Append))
                    {
                        stream.Write("two"u8);
                        Console.WriteLine($"append: {stream.Position} {stream.CanSeek} {Try(() => stream.Seek(0, SeekOrigin.Begin))} {File.ReadAllText(log)}");
                    }

                    using (var stream = new FileStream(log, FileMode.Open, FileAccess.Read))
                    {
                        Console.WriteLine($"readonly: {stream.CanWrite} {Try(() => stream.WriteByte(1))} {stream.ReadByte()} {stream.Seek(0, SeekOrigin.End)} {stream.ReadByte()}");
                    }

                    using (var stream = new FileStream(log, FileMode.Truncate))
                    {
                        Console.WriteLine($"truncate: {stream.Length}");
                    }

                    var gone = Path.Combine(root, "gone.txt");
                    using (var stream = new FileStream(gone, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose))
                    {
                        stream.WriteByte(1);
                    }

                    Console.WriteLine($"deleteonclose: {File.Exists(gone)}");
                    File.WriteAllLines(log, ["x", "y", "z"]);
                    var lines = new List<string>();
                    foreach (var line in File.ReadLines(log)) lines.Add(line);
                    File.WriteAllTextAsync(log, "async").GetAwaiter().GetResult();
                    Console.WriteLine($"lines: {string.Join("|", lines)} {File.ReadAllTextAsync(log).GetAwaiter().GetResult()}");

                    var b = Path.Combine(root, "b");
                    Directory.Move(a, b);
                    var c = Path.Combine(root, "c");
                    new DirectoryInfo(b).MoveTo(c);
                    File.Move(Path.Combine(c, "note.txt"), Path.Combine(c, "Upper.TXT"), overwrite: true);
                    File.WriteAllText(Path.Combine(c, "new.txt"), "new");
                    File.Replace(Path.Combine(c, "new.txt"), Path.Combine(c, "Upper.TXT"), Path.Combine(c, "backup.txt"));
                    Console.WriteLine($"moves: {Directory.Exists(a)} {Sorted(Directory.EnumerateFiles(c))} {File.ReadAllText(Path.Combine(c, "Upper.TXT"))} {File.ReadAllText(Path.Combine(c, "backup.txt"))}");
                    Console.WriteLine($"dangling: {File.Exists(link)} {new FileInfo(link).Exists} {Try(() => File.ReadAllText(link))} {Directory.Exists(directoryLink)}");

                    Console.WriteLine($"errors: {Try(() => Directory.Delete(c))} {Try(() => File.Delete(Path.Combine(root, "nowhere", "x")))} {Try(() => Directory.CreateDirectory(Path.Combine(c, "Upper.TXT", "sub")))} {Try(() => File.WriteAllText(c, "x"))} {Try(() => Environment.CurrentDirectory = Path.Combine(root, "nope"))} {Try(() => File.ReadAllText(c))}");
                    Console.WriteLine($"outside: {File.Exists("/elsewhere/x")} {Directory.Exists("/elsewhere")}");
                    var written = new FileInfo(Path.Combine(c, "Upper.TXT"));
                    Console.WriteLine($"times: {written.LastWriteTimeUtc.Year >= 2026} {written.CreationTimeUtc.Year >= 2026} {written.LastAccessTimeUtc.Year >= 2026}");

                    Directory.Delete(root, recursive: true);
                    Console.WriteLine($"cleaned: {Directory.Exists(root)} {File.ReadAllText(Path.Combine(keep, "kept.txt"))}");
                    return 0;
                }
            }
            """);
        var data = Path.Combine(assets.Directory, "data");
        var temp = Path.Combine(assets.Directory, "tmp");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(temp);

        var (exitCode, output, error) = await RunFileProgramInWasmtime(
            assets, assembly, "--dir", $"{data}::/data", "--dir", $"{temp}::/tmp");

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}\n{output}");
        Assert.Equal(
            "dots: hello a/note.txt . work\n" +
            "entries: a\n" +
            "temp: True .tmp 0 True\n" +
            "tempdir: True True True\n" +
            "tempgone: False False\n" +
            "link: a/note.txt hello 10 True a/note.txt\n" +
            "dirlink: True True alink/note.txt\n" +
            "recursive: a/note.txt,alink/note.txt,keeplink/kept.txt,link.txt\n" +
            "pattern: a/note.txt | a/Upper.TXT,a/note.txt | a/Upper.TXT,a/note.txt,a/x.tmp\n" +
            "attributes: True Directory Normal\n" +
            "unicode: na\u00efve-\u6587\u4ef6.bin 200000 True\n" +
            "modes: FileNotFoundException ok IOException\n" +
            "append: 6 True IOException onetwo\n" +
            "readonly: False NotSupportedException 111 6 -1\n" +
            "truncate: 0\n" +
            "deleteonclose: False\n" +
            "lines: x|y|z async\n" +
            "moves: False c/.hidden,c/Upper.TXT,c/backup.txt,c/na\u00efve-\u6587\u4ef6.bin,c/x.tmp new hello\n" +
            "dangling: True True FileNotFoundException False\n" +
            "errors: IOException DirectoryNotFoundException DirectoryNotFoundException UnauthorizedAccessException DirectoryNotFoundException UnauthorizedAccessException\n" +
            "outside: False False\n" +
            "times: True True True\n" +
            "cleaned: False kept\n",
            output);
        Assert.Equal(["keep"], Directory.EnumerateFileSystemEntries(data).Select(Path.GetFileName));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
    }

    // The platform's network over wasi:sockets 0.3: TCP over loopback with the connection and its
    // acceptance concurrent, both directions and a half-close; UDP datagrams; a refused
    // connection; name lookup. Socket and Dns build on these calls.
    [Fact]
    public async Task ThePlatformsNetworkConnectsSendsAndResolvesInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.Net;
            using System.Net.Sockets;
            using System.Runtime.InteropServices.WebAssembly;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;
            namespace NetworkFixture;
            internal static class Program
            {
                public static async Task<int> Main()
                {
                    var network = new WasiPlatform().Network;
                    var loopback = new IPEndPoint(IPAddress.Loopback, 0);
                    Check(network.Open(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp, out var listener));
                    Check(network.Bind(listener, loopback));
                    Check(network.Listen(listener, 4));
                    Check(network.GetLocalEndPoint(listener, out var bound));
                    Check(network.Open(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp, out var client));
                    var accepting = network.AcceptAsync(listener, CancellationToken.None).AsTask();
                    Check(await network.ConnectAsync(client, bound!, CancellationToken.None));
                    var accepted = await accepting;
                    Check(accepted.Error);
                    var hello = await network.SendAsync(client, Encoding.UTF8.GetBytes("hello"), CancellationToken.None);
                    var buffer = new byte[64];
                    var received = await network.ReceiveAsync(accepted.Socket, buffer, CancellationToken.None);
                    Console.WriteLine($"tcp: {hello.BytesTransferred} {Encoding.UTF8.GetString(buffer, 0, received.BytesTransferred)} {accepted.RemoteEndPoint!.Address}");
                    Check(network.Send(accepted.Socket, Encoding.UTF8.GetBytes("world"), out var sent));
                    Check(network.Receive(client, buffer, out var count));
                    Check(network.Shutdown(client, SocketShutdown.Send));
                    var end = await network.ReceiveAsync(accepted.Socket, buffer, CancellationToken.None);
                    Console.WriteLine($"reply: {sent} {Encoding.UTF8.GetString(buffer, 0, count)} end={end.Error}:{end.BytesTransferred}");

                    // A pending accept ends when its token is cancelled.
                    using (var cancel = new CancellationTokenSource())
                    {
                        var waiting = network.AcceptAsync(listener, cancel.Token).AsTask();
                        cancel.Cancel();
                        Console.WriteLine($"cancelled: {(await waiting).Error}");
                    }

                    network.Close(accepted.Socket);
                    network.Close(client);
                    network.Close(listener);
                    Check(network.Open(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp, out var refused));
                    Console.WriteLine($"refused: {network.Connect(refused, bound!)}");
                    network.Close(refused);

                    Check(network.Open(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, out var left));
                    Check(network.Open(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, out var right));
                    Check(network.Bind(left, loopback));
                    Check(network.Bind(right, loopback));
                    Check(network.GetLocalEndPoint(right, out var rightEnd));
                    Check(network.SendTo(left, Encoding.UTF8.GetBytes("ping"), rightEnd, out _));
                    var datagram = await network.ReceiveFromAsync(right, buffer, CancellationToken.None);
                    Console.WriteLine($"udp: {Encoding.UTF8.GetString(buffer, 0, datagram.BytesTransferred)} {datagram.RemoteEndPoint!.Address}");
                    network.Close(left);
                    network.Close(right);

                    Check(network.ResolveAddresses("localhost", out var addresses));
                    Console.WriteLine($"dns: {Array.Exists(addresses, IPAddress.IsLoopback)}");
                    Console.WriteLine($"unsupported: {network.Open(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified, out _)}");
                    return 0;
                }

                private static void Check(SocketError error)
                {
                    if (error != SocketError.Success) throw new InvalidOperationException(error.ToString());
                }
            }
            """);

        var (exitCode, output, error) = await RunProgramInWasmtime(assets, assembly,
            ManagedExecutableCompletionShape.Asynchronous, "-S", "inherit-network,allow-ip-name-lookup");

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}\n{output}");
        Assert.Equal(
            "tcp: 5 hello 127.0.0.1\n" +
            "reply: 5 world end=Success:0\n" +
            "cancelled: OperationAborted\n" +
            "refused: ConnectionRefused\n" +
            "udp: ping 127.0.0.1\n" +
            "dns: True\n" +
            "unsupported: AddressFamilyNotSupported\n",
            output);
    }

    // Socket, TcpListener, TcpClient, NetworkStream, UdpClient and Dns over WasiPlatform's network,
    // with .NET 11 RC3's output on Linux except that Unix domain sockets are unavailable and a
    // dual-mode socket cannot bind the IPv6 wildcard (WASI has no dual-stack socket; dual-mode
    // connects and sends to IPv4 work).
    [Fact]
    public async Task SocketsStreamsAndDnsRunInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.IO;
            using System.Net;
            using System.Net.Sockets;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;
            namespace SocketsFixture;
            internal static class Program
            {
                public static async Task<int> Main()
                {
                    var listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start();
                    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    var accepting = listener.AcceptTcpClientAsync();
                    using var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    using var server = await accepting;
                    var clientStream = client.GetStream();
                    var serverStream = server.GetStream();
                    await clientStream.WriteAsync(Encoding.UTF8.GetBytes("hello"));
                    var buffer = new byte[64];
                    var read = await serverStream.ReadAsync(buffer);
                    Console.WriteLine($"stream: {Encoding.UTF8.GetString(buffer, 0, read)} {((IPEndPoint)server.Client.RemoteEndPoint!).Address} {client.Connected}");
                    Console.WriteLine($"dual: {client.Client.AddressFamily} {client.Client.DualMode} {((IPEndPoint)client.Client.RemoteEndPoint!).Address}");

                    using (var writer = new StreamWriter(serverStream, leaveOpen: true) { AutoFlush = true })
                    {
                        writer.WriteLine("line one");
                    }
                    using (var reader = new StreamReader(clientStream, leaveOpen: true))
                    {
                        Console.WriteLine($"line: {reader.ReadLine()}");
                    }

                    client.Client.Send(Encoding.UTF8.GetBytes("sync"));
                    var count = server.Client.Receive(buffer);
                    Console.WriteLine($"sync: {Encoding.UTF8.GetString(buffer, 0, count)}");

                    using (var cancel = new CancellationTokenSource())
                    {
                        var pending = client.Client.ReceiveAsync(buffer, SocketFlags.None, cancel.Token).AsTask();
                        cancel.Cancel();
                        try { await pending; } catch (OperationCanceledException) { Console.WriteLine("receive: cancelled"); }
                    }

                    client.Client.Shutdown(SocketShutdown.Send);
                    Console.WriteLine($"end: {await serverStream.ReadAsync(buffer)}");

                    var orphan = listener.AcceptSocketAsync();
                    listener.Stop();
                    try { await orphan; } catch (Exception e) { Console.WriteLine($"stopped: {Describe(e)}"); }
                    try
                    {
                        using var refused = new TcpClient();
                        await refused.ConnectAsync(IPAddress.Loopback, port);
                    }
                    catch (Exception e) { Console.WriteLine($"refused: {Describe(e)}"); }

                    var second = new TcpListener(IPAddress.Loopback, 0);
                    second.Start();
                    using (var named = new TcpClient("localhost", ((IPEndPoint)second.LocalEndpoint).Port))
                    using (var peer = second.AcceptTcpClient())
                    {
                        named.GetStream().Write(Encoding.UTF8.GetBytes("by name"));
                        count = peer.GetStream().Read(buffer);
                        Console.WriteLine($"named: {Encoding.UTF8.GetString(buffer, 0, count)}");
                    }
                    second.Stop();

                    using var left = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                    using var right = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                    var rightEnd = (IPEndPoint)right.Client.LocalEndPoint!;
                    await left.SendAsync(Encoding.UTF8.GetBytes("ping"), rightEnd);
                    var datagram = await right.ReceiveAsync();
                    Console.WriteLine($"udp: {Encoding.UTF8.GetString(datagram.Buffer)} {datagram.RemoteEndPoint.Equals(left.Client.LocalEndPoint)}");
                    left.Send(Encoding.UTF8.GetBytes("pong"), rightEnd);
                    IPEndPoint? from = null;
                    Console.WriteLine($"udp sync: {Encoding.UTF8.GetString(right.Receive(ref from))} {from!.Address}");
                    using (var dualUdp = new Socket(SocketType.Dgram, ProtocolType.Udp))
                    {
                        dualUdp.SendTo(Encoding.UTF8.GetBytes("mapped"), rightEnd);
                        Console.WriteLine($"udp dual: {Encoding.UTF8.GetString(right.Receive(ref from))} {from!.AddressFamily} {((IPEndPoint)dualUdp.LocalEndPoint!).AddressFamily}");
                    }

                    var addresses = await Dns.GetHostAddressesAsync("localhost");
                    Console.WriteLine($"dns: {Array.Exists(addresses, IPAddress.IsLoopback)}");
                    var v4 = Dns.GetHostAddresses("localhost", AddressFamily.InterNetwork);
                    Console.WriteLine($"dns v4: {v4.Length > 0 && Array.TrueForAll(v4, a => a.AddressFamily == AddressFamily.InterNetwork)}");
                    Console.WriteLine($"entry: {Dns.GetHostEntry("127.0.0.1").AddressList[0]}");
                    try { await Dns.GetHostAddressesAsync("no-such-host.invalid"); }
                    catch (Exception e) { Console.WriteLine($"unknown: {Describe(e)}"); }

                    var endPoint = new IPEndPoint(IPAddress.Parse("192.168.1.2"), 8080);
                    Console.WriteLine($"address: {endPoint.Create(endPoint.Serialize())} {new DnsEndPoint("example.com", 80)}");
                    try
                    {
                        using var wildcard = new Socket(SocketType.Stream, ProtocolType.Tcp);
                        wildcard.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
                        Console.WriteLine($"dual wildcard: bound {wildcard.DualMode}");
                    }
                    catch (SocketException e) { Console.WriteLine($"dual wildcard: {e.SocketErrorCode}"); }
                    using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try { probe.Blocking = false; Console.WriteLine("non-blocking: set"); }
                    catch (PlatformNotSupportedException) { Console.WriteLine("non-blocking: unsupported"); }
                    try { new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified).Dispose(); Console.WriteLine("unix: opened"); }
                    catch (Exception e) { Console.WriteLine($"unix: {Describe(e)}"); }
                    return 0;
                }

                private static string Describe(Exception e) => e is SocketException s ? s.SocketErrorCode.ToString() : ExceptionKind.Of(e);
            }
            """ + ExceptionKindSource("System.Net.Sockets.SocketException"));

        var (exitCode, output, error) = await RunProgramInWasmtime(assets, assembly,
            ManagedExecutableCompletionShape.Asynchronous, "-S", "inherit-network,allow-ip-name-lookup");

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}\n{output}");
        Assert.Equal(
            "stream: hello 127.0.0.1 True\n" +
            "dual: InterNetworkV6 True ::ffff:127.0.0.1\n" +
            "line: line one\n" +
            "sync: sync\n" +
            "receive: cancelled\n" +
            "end: 0\n" +
            "stopped: OperationAborted\n" +
            "refused: ConnectionRefused\n" +
            "named: by name\n" +
            "udp: ping True\n" +
            "udp sync: pong 127.0.0.1\n" +
            "udp dual: mapped InterNetwork InterNetworkV6\n" +
            "dns: True\n" +
            "dns v4: True\n" +
            "entry: 127.0.0.1\n" +
            "unknown: HostNotFound\n" +
            "address: 192.168.1.2:8080 Unspecified/example.com:80\n" +
            "dual wildcard: OperationNotSupported\n" +
            "non-blocking: set\n" +
            "unix: AddressFamilyNotSupported\n",
            output);
    }

    // ClientWebSocket against a WebSocket.CreateFromStream server over TcpListener in one program:
    // the upgrade, text, binary and fragmented messages, the close handshake, a server that is not
    // a WebSocket server and a refused connection, with desktop .NET's output on Linux; wss:// is
    // not supported (no TLS).
    [Fact]
    public async Task WebSocketsUpgradeAndExchangeMessagesInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.Collections.Generic;
            using System.IO;
            using System.Net;
            using System.Net.Sockets;
            using System.Net.WebSockets;
            using System.Security.Cryptography;
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;
            namespace WebSocketFixture;
            internal static class Program
            {
                public static async Task<int> Main()
                {
                    var listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start();
                    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    var serving = ServeAsync(listener);
                    using var client = new ClientWebSocket();
                    client.Options.AddSubProtocol("other");
                    client.Options.AddSubProtocol("chat");
                    client.Options.SetRequestHeader("X-Fixture", "yes");
                    await client.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/echo?room=1"), CancellationToken.None);
                    Console.WriteLine($"client: {client.State} {client.SubProtocol}");

                    await client.SendAsync(Encoding.UTF8.GetBytes("hello"), WebSocketMessageType.Text, true, CancellationToken.None);
                    Console.WriteLine($"reply: {await ReceiveText(client)}");
                    await client.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, CancellationToken.None);
                    Console.WriteLine($"reply: {await ReceiveText(client)}");
                    var large = new byte[100_000];
                    for (var index = 0; index < large.Length; index++) large[index] = (byte)index;
                    await client.SendAsync(large, WebSocketMessageType.Binary, true, CancellationToken.None);
                    Console.WriteLine($"reply: {await ReceiveText(client)}");

                    await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    Console.WriteLine($"closed: {client.State} {client.CloseStatus} {client.CloseStatusDescription}");
                    Console.WriteLine($"server: {await serving}");

                    var plain = new TcpListener(IPAddress.Loopback, 0);
                    plain.Start();
                    var plainPort = ((IPEndPoint)plain.LocalEndpoint).Port;
                    var answering = AnswerPlainAsync(plain);
                    try
                    {
                        using var rejected = new ClientWebSocket();
                        await rejected.ConnectAsync(new Uri($"ws://127.0.0.1:{plainPort}/"), CancellationToken.None);
                    }
                    catch (WebSocketException e) { Console.WriteLine($"plain: {e.WebSocketErrorCode} {e.Message}"); }
                    await answering;
                    plain.Stop();
                    listener.Stop();

                    try
                    {
                        using var refused = new ClientWebSocket();
                        await refused.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
                    }
                    catch (WebSocketException e) { Console.WriteLine($"refused: {e.WebSocketErrorCode} {e.Message}"); }
                    try
                    {
                        using var secure = new ClientWebSocket();
                        await secure.ConnectAsync(new Uri($"wss://127.0.0.1:{port}/"), CancellationToken.None);
                    }
                    catch (PlatformNotSupportedException) { Console.WriteLine("wss: unsupported"); }
                    return 0;
                }

                private static async Task<string> ServeAsync(TcpListener listener)
                {
                    using var connection = await listener.AcceptTcpClientAsync();
                    var stream = connection.GetStream();
                    var headers = await ReadHeadersAsync(stream);
                    var key = headers["sec-websocket-key"];
                    var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                    var response = $"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\nSec-WebSocket-Protocol: chat\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
                    using var server = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: "chat", keepAliveInterval: TimeSpan.Zero);
                    var seen = $"{headers["request"]} protocols={headers["sec-websocket-protocol"]} fixture={headers["x-fixture"]}";
                    var buffer = new byte[200_000];
                    while (true)
                    {
                        var total = 0;
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await server.ReceiveAsync(new ArraySegment<byte>(buffer, total, buffer.Length - total), CancellationToken.None);
                            total += result.Count;
                        }
                        while (!result.EndOfMessage);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await server.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, server.CloseStatusDescription, CancellationToken.None);
                            return $"{seen} close={result.CloseStatus}:{result.CloseStatusDescription} {server.State}";
                        }

                        var text = result.MessageType == WebSocketMessageType.Text
                            ? Encoding.UTF8.GetString(buffer, 0, total)
                            : $"{total} bytes, last {buffer[total - 1]}";
                        await server.SendAsync(Encoding.UTF8.GetBytes($"echo: {text}"), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }

                private static async Task AnswerPlainAsync(TcpListener listener)
                {
                    using var connection = await listener.AcceptTcpClientAsync();
                    var stream = connection.GetStream();
                    await ReadHeadersAsync(stream);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"));
                }

                private static async Task<Dictionary<string, string>> ReadHeadersAsync(Stream stream)
                {
                    var bytes = new List<byte>();
                    var one = new byte[1];
                    while (bytes.Count < 4 || bytes[^1] != '\n' || bytes[^2] != '\r' || bytes[^3] != '\n' || bytes[^4] != '\r')
                    {
                        if (await stream.ReadAsync(one) == 0) break;
                        bytes.Add(one[0]);
                    }
                    var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n");
                    var headers = new Dictionary<string, string> { ["request"] = lines[0] };
                    foreach (var line in lines[1..])
                    {
                        var colon = line.IndexOf(':');
                        if (colon > 0) headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
                    }
                    return headers;
                }

                private static async Task<string> ReceiveText(WebSocket socket)
                {
                    var buffer = new byte[1024];
                    var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                    return Encoding.UTF8.GetString(buffer, 0, result.Count);
                }
            }
            """);

        var (exitCode, output, error) = await RunProgramInWasmtime(assets, assembly,
            ManagedExecutableCompletionShape.Asynchronous, "-S", "inherit-network,allow-ip-name-lookup");

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}\n{output}");
        Assert.Equal(
            "client: Open chat\n" +
            "reply: echo: hello\n" +
            "reply: echo: 3 bytes, last 3\n" +
            "reply: echo: 100000 bytes, last 159\n" +
            "closed: Closed NormalClosure bye\n" +
            "server: GET /echo?room=1 HTTP/1.1 protocols=other, chat fixture=yes close=NormalClosure:bye Closed\n" +
            "plain: NotAWebSocket The server returned status code '200' when status code '101' was expected.\n" +
            "refused: Faulted Unable to connect to the remote server\n" +
            "wss: unsupported\n",
            output);
    }

    // A non-blocking socket (Blocking false) sends and receives what is ready and reports WouldBlock otherwise, as
    // on Linux, and blocks again when Blocking is set back. Connecting needs readiness the platform network does
    // not report, so a non-blocking connect throws (Linux reports WouldBlock and leaves it in progress).
    [Fact]
    public async Task NonBlockingSocketsMoveWhatIsReadyInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.Net;
            using System.Net.Sockets;
            using System.Text;
            using System.Threading.Tasks;
            namespace NonBlockingFixture;
            internal static class Program
            {
                public static async Task<int> Main()
                {
                    var listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start();
                    var accept = listener.AcceptSocketAsync();
                    using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    await client.ConnectAsync(listener.LocalEndpoint);
                    using var server = await accept;
                    client.Blocking = false;
                    Console.WriteLine("blocking: " + client.Blocking);
                    var buffer = new byte[16];
                    try { client.Receive(buffer); Console.WriteLine("receive: data"); }
                    catch (SocketException e) { Console.WriteLine("receive: " + e.SocketErrorCode); }
                    await server.SendAsync(Encoding.ASCII.GetBytes("hello"));
                    var received = 0;
                    for (var attempt = 0; attempt < 200 && received == 0; attempt++)
                    {
                        try { received = client.Receive(buffer); }
                        catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock) { await Task.Delay(10); }
                    }
                    Console.WriteLine("received: " + Encoding.ASCII.GetString(buffer, 0, received));
                    Console.WriteLine("sent: " + client.Send(Encoding.ASCII.GetBytes("world")));
                    var count = await server.ReceiveAsync(buffer);
                    Console.WriteLine("server: " + Encoding.ASCII.GetString(buffer, 0, count));
                    client.Blocking = true;
                    await server.SendAsync(Encoding.ASCII.GetBytes("!"));
                    Console.WriteLine("blocking receive: " + client.Receive(buffer));
                    using var other = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { Blocking = false };
                    try { other.Connect(listener.LocalEndpoint); Console.WriteLine("connect: connected"); }
                    catch (PlatformNotSupportedException) { Console.WriteLine("connect: unsupported"); }
                    catch (SocketException e) { Console.WriteLine("connect: " + e.SocketErrorCode); }
                    listener.Stop();
                    return 0;
                }
            }
            """);

        var (exitCode, output, error) = await RunProgramInWasmtime(assets, assembly,
            ManagedExecutableCompletionShape.Asynchronous, "-S", "inherit-network,allow-ip-name-lookup");

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}\n{output}");
        Assert.Equal(
            "blocking: False\n" +
            "receive: WouldBlock\n" +
            "received: hello\n" +
            "sent: 5\n" +
            "server: world\n" +
            "blocking receive: 1\n" +
            "connect: unsupported\n",
            output);
    }

    // SmtpClient as on Linux: pickup-directory delivery over the file system, and SMTP to a server the test runs.
    // The server runs here rather than in the program because upstream's SendMailAsync ends the message with a
    // synchronous exchange (the writer's Close), which on one thread waits for a server outside the program.
    [Fact]
    public async Task SmtpClientDeliversToADirectoryAndAServerInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.IO;
            using System.Net.Mail;
            using System.Threading.Tasks;
            namespace SmtpFixture;
            internal static class Program
            {
                public static async Task<int> Main()
                {
                    var directory = Path.Combine("/work", "pickup");
                    Directory.CreateDirectory(directory);
                    using (var pickup = new SmtpClient { DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory, PickupDirectoryLocation = directory })
                    using (var message = new MailMessage("from@example.test", "to@example.test", "Pickup", "To a directory."))
                    {
                        await pickup.SendMailAsync(message);
                    }
                    var files = Directory.GetFiles(directory, "*.eml");
                    var text = File.ReadAllText(files[0]);
                    Console.WriteLine("pickup: " + files.Length + " " + text.Contains("Subject: Pickup") + " " + text.Contains("To a directory."));

                    var port = int.Parse(Environment.GetEnvironmentVariable("SMTP_PORT")!);
                    using (var client = new SmtpClient("127.0.0.1", port))
                    using (var message = new MailMessage("from@example.test", "to@example.test", "Network", "Over SMTP."))
                    {
                        await client.SendMailAsync(message);
                    }
                    Console.WriteLine("sent");

                    // A port nothing listens on refuses the connection; the client aborts it and disposes cleanly.
                    var closed = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                    closed.Start();
                    var refusedPort = ((System.Net.IPEndPoint)closed.LocalEndpoint).Port;
                    closed.Stop();
                    using (var refused = new SmtpClient("127.0.0.1", refusedPort))
                    using (var message = new MailMessage("from@example.test", "to@example.test", "Refused", "No server."))
                    {
                        try { await refused.SendMailAsync(message); Console.WriteLine("refused: sent"); }
                        catch (SmtpException e) when (e.InnerException is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused })
                        {
                            Console.WriteLine("refused: ConnectionRefused");
                        }
                    }
                    Console.WriteLine("disposed");
                    return 0;
                }
            }
            """);

        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var commands = new List<string>();
            var data = new System.Text.StringBuilder();
            var server = ServeSmtpAsync(listener, commands, data);
            var work = Path.Combine(assets.Directory, "work");
            Directory.CreateDirectory(work);
            var (exitCode, output, error) = await RunProgramInWasmtime(assets, assembly,
                ManagedExecutableCompletionShape.Asynchronous, "-S", "inherit-network,allow-ip-name-lookup",
                "--env", $"SMTP_PORT={port}", "--dir", work + "::/work");
            await server.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            Assert.True(exitCode == 0, $"exit code {exitCode}: {error}\n{output}");
            Assert.Equal("pickup: 1 True True\nsent\nrefused: ConnectionRefused\ndisposed\n", output);
            Assert.Equal(
                ["EHLO LocalHost", "MAIL FROM:<from@example.test>", "RCPT TO:<to@example.test>", "DATA", "QUIT"],
                commands);
            Assert.Contains("Subject: Network", data.ToString());
            Assert.Contains("Over SMTP.", data.ToString());
        }
        finally
        {
            listener.Stop();
        }
    }

    // FtpWebRequest on upstream's FTP client, against a passive-mode FTP server the test runs: a synchronous
    // download, an asynchronous upload, a directory listing, and a missing file's 550.
    [Fact]
    public async Task FtpRequestsDownloadUploadAndListInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.IO;
            using System.Net;
            using System.Text;
            using System.Threading.Tasks;
            namespace FtpFixture;
            internal static class Program
            {
                public static async Task<int> Main()
                {
                    var root = "ftp://127.0.0.1:" + Environment.GetEnvironmentVariable("FTP_PORT") + "/";
                    #pragma warning disable SYSLIB0014
                    var download = (FtpWebRequest)WebRequest.Create(root + "hello.txt");
                    download.Method = WebRequestMethods.Ftp.DownloadFile;
                    download.KeepAlive = false;
                    using (var response = (FtpWebResponse)download.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        Console.WriteLine("download: " + reader.ReadToEnd() + " " + response.StatusCode);
                    }

                    var upload = (FtpWebRequest)WebRequest.Create(root + "upload.txt");
                    upload.Method = WebRequestMethods.Ftp.UploadFile;
                    upload.KeepAlive = false;
                    using (var stream = await upload.GetRequestStreamAsync())
                    {
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("uploaded bytes"));
                    }
                    using (var response = (FtpWebResponse)await upload.GetResponseAsync())
                    {
                        Console.WriteLine("upload: " + response.StatusCode);
                    }

                    var list = (FtpWebRequest)WebRequest.Create(root);
                    list.Method = WebRequestMethods.Ftp.ListDirectory;
                    list.KeepAlive = false;
                    using (var response = (FtpWebResponse)await list.GetResponseAsync())
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        Console.WriteLine("list: " + reader.ReadToEnd().Replace("\r\n", ",").TrimEnd(','));
                    }

                    var missing = (FtpWebRequest)WebRequest.Create(root + "missing.txt");
                    missing.Method = WebRequestMethods.Ftp.DownloadFile;
                    missing.KeepAlive = false;
                    try { using var response = missing.GetResponse(); Console.WriteLine("missing: found"); }
                    catch (WebException e) { Console.WriteLine("missing: " + ((FtpWebResponse)e.Response!).StatusCode); }
                    #pragma warning restore SYSLIB0014
                    return 0;
                }
            }
            """);

        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var uploads = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
            using var stop = new CancellationTokenSource();
            var server = ServeFtpAsync(listener, uploads, stop.Token);
            var (exitCode, output, error) = await RunProgramInWasmtime(assets, assembly,
                ManagedExecutableCompletionShape.Asynchronous, "-S", "inherit-network,allow-ip-name-lookup,http",
                "--env", $"FTP_PORT={((System.Net.IPEndPoint)listener.LocalEndpoint).Port}");
            stop.Cancel();

            Assert.True(exitCode == 0, $"exit code {exitCode}: {error}\n{output}");
            Assert.Equal(
                "download: Hello over FTP. ClosingData\n" +
                "upload: ClosingData\n" +
                "list: hello.txt,upload.txt\n" +
                "missing: ActionNotTakenFileUnavailable\n",
                output);
            Assert.Equal("uploaded bytes", uploads["upload.txt"]);
        }
        finally
        {
            listener.Stop();
        }
    }

    // A passive-mode FTP server for one client at a time: login, PWD, TYPE, PASV, RETR, STOR, NLST and QUIT.
    private static async Task ServeFtpAsync(
        System.Net.Sockets.TcpListener listener,
        System.Collections.Concurrent.ConcurrentDictionary<string, string> uploads,
        CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, string> { ["hello.txt"] = "Hello over FTP." };
        while (!cancellationToken.IsCancellationRequested)
        {
            System.Net.Sockets.TcpClient connection;
            try { connection = await listener.AcceptTcpClientAsync(cancellationToken); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            using (connection)
            using (var stream = connection.GetStream())
            using (var reader = new StreamReader(stream, System.Text.Encoding.ASCII))
            using (var writer = new StreamWriter(stream, System.Text.Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true })
            {
                System.Net.Sockets.TcpListener? data = null;
                await writer.WriteLineAsync("220 test FTP");
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    var space = line.IndexOf(' ');
                    var command = (space < 0 ? line : line[..space]).ToUpperInvariant();
                    var argument = space < 0 ? "" : line[(space + 1)..].TrimStart('/');
                    switch (command)
                    {
                        case "USER": await writer.WriteLineAsync("331 Password"); break;
                        case "PASS": await writer.WriteLineAsync("230 Logged in"); break;
                        case "PWD": await writer.WriteLineAsync("257 \"/\" is current"); break;
                        case "TYPE": await writer.WriteLineAsync("200 Type set"); break;
                        case "PASV":
                            data = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                            data.Start();
                            var port = ((System.Net.IPEndPoint)data.LocalEndpoint).Port;
                            await writer.WriteLineAsync($"227 Entering Passive Mode (127,0,0,1,{port >> 8},{port & 255})");
                            break;
                        case "RETR" when files.TryGetValue(argument, out var content):
                            {
                                await writer.WriteLineAsync("150 Opening");
                                using (var transfer = await data!.AcceptTcpClientAsync(cancellationToken))
                                {
                                    await transfer.GetStream().WriteAsync(System.Text.Encoding.ASCII.GetBytes(content), cancellationToken);
                                }
                                data.Stop();
                                await writer.WriteLineAsync("226 Transfer complete");
                                break;
                            }
                        case "RETR":
                            data?.Stop();
                            await writer.WriteLineAsync("550 No such file");
                            break;
                        case "STOR":
                            {
                                await writer.WriteLineAsync("150 Ready");
                                using (var transfer = await data!.AcceptTcpClientAsync(cancellationToken))
                                using (var body = new StreamReader(transfer.GetStream(), System.Text.Encoding.ASCII))
                                {
                                    var text = await body.ReadToEndAsync(cancellationToken);
                                    uploads[argument] = text;
                                    files[argument] = text;
                                }
                                data.Stop();
                                await writer.WriteLineAsync("226 Stored");
                                break;
                            }
                        case "NLST":
                            {
                                await writer.WriteLineAsync("150 Listing");
                                using (var transfer = await data!.AcceptTcpClientAsync(cancellationToken))
                                {
                                    var names = string.Join("\r\n", files.Keys.Order(StringComparer.Ordinal)) + "\r\n";
                                    await transfer.GetStream().WriteAsync(System.Text.Encoding.ASCII.GetBytes(names), cancellationToken);
                                }
                                data.Stop();
                                await writer.WriteLineAsync("226 Listed");
                                break;
                            }
                        case "QUIT": await writer.WriteLineAsync("221 Bye"); goto Done;
                        default: await writer.WriteLineAsync("502 Not implemented"); break;
                    }
                }
            Done:
                data?.Stop();
            }
        }
    }

    // HttpListener on upstream's managed implementation over WASI sockets: the program serves two requests that
    // the test sends with desktop HttpClient (a GET with a header, a POST with a body), as on Linux.
    [Fact]
    public async Task HttpListenerServesRequestsInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.IO;
            using System.Net;
            using System.Text;
            using System.Threading.Tasks;
            namespace ListenerFixture;
            internal static class Program
            {
                public static async Task<int> Main()
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add("http://127.0.0.1:" + Environment.GetEnvironmentVariable("LISTEN_PORT") + "/");
                    listener.Start();
                    for (var served = 0; served < 2; served++)
                    {
                        var context = await listener.GetContextAsync();
                        var request = context.Request;
                        string body;
                        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                        {
                            body = await reader.ReadToEndAsync();
                        }
                        var text = request.HttpMethod + " " + request.Url!.AbsolutePath + " " + request.Headers["X-Test"] + " " + body;
                        var bytes = Encoding.UTF8.GetBytes(text);
                        var response = context.Response;
                        response.StatusCode = 201;
                        response.Headers["X-Reply"] = "yes";
                        response.ContentLength64 = bytes.Length;
                        await response.OutputStream.WriteAsync(bytes);
                        response.Close();
                        Console.WriteLine("served " + text);
                    }
                    listener.Stop();
                    return 0;
                }
            }
            """);

        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var run = RunProgramInWasmtime(assets, assembly,
            ManagedExecutableCompletionShape.Asynchronous, "-S", "inherit-network,allow-ip-name-lookup",
            "--env", $"LISTEN_PORT={port}");

        using var client = new System.Net.Http.HttpClient();
        var url = $"http://127.0.0.1:{port}/items/7";
        System.Net.Http.HttpResponseMessage? first = null;
        for (var attempt = 0; attempt < 300 && first is null && !run.IsCompleted; attempt++)
        {
            try
            {
                using var get = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                get.Headers.Add("X-Test", "one");
                first = await client.SendAsync(get, TestContext.Current.CancellationToken);
            }
            catch (System.Net.Http.HttpRequestException)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
        }
        if (first is null)
        {
            var (code, text, failure) = await run;
            Assert.Fail($"no response; the program exited {code}: {failure}\n{text}");
        }
        Assert.Equal(System.Net.HttpStatusCode.Created, first!.StatusCode);
        Assert.Equal("yes", string.Join(",", first.Headers.GetValues("X-Reply")));
        Assert.Equal("GET /items/7 one ", await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using var post = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url) { Content = new System.Net.Http.StringContent("payload") };
        post.Headers.Add("X-Test", "two");
        using var second = await client.SendAsync(post, TestContext.Current.CancellationToken);
        Assert.Equal("POST /items/7 two payload", await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var (exitCode, output, error) = await run;
        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}\n{output}");
        Assert.Equal("served GET /items/7 one \nserved POST /items/7 two payload\n", output);
    }

    // Thread.Sleep blocks on wasi:clocks/monotonic-clock's wait-for in place: the time passes, and work queued before
    // the sleep runs only at the next await (no event-loop pumping). An infinite sleep throws (nothing on the one
    // thread can end it); Thread.Yield has no other thread to yield to; Thread.SpinWait returns.
    [Fact]
    public async Task ThreadSleepBlocksWithoutRunningTheEventLoopInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.Diagnostics;
            using System.Threading;
            using System.Threading.Tasks;
            namespace SleepFixture;
            internal static class Program
            {
                private static string Throws(Action action)
                {
                    try { action(); return "no"; } catch (Exception e) { return ExceptionKind.Of(e); }
                }

                private static async Task<int> Main()
                {
                    var stopwatch = Stopwatch.StartNew();
                    Thread.Sleep(100);
                    Console.WriteLine("slept at least 100 ms: " + (stopwatch.ElapsedMilliseconds >= 100));
                    var ran = false;
                    ThreadPool.QueueUserWorkItem(_ => ran = true);
                    var delay = Task.Delay(10);
                    Thread.Sleep(TimeSpan.FromMilliseconds(50));
                    Console.WriteLine("queued work ran during the sleep: " + ran + ", delay done: " + delay.IsCompleted);
                    await Task.Yield();
                    await delay;
                    Console.WriteLine("after await: " + ran + ", delay done: " + delay.IsCompleted);
                    Thread.Sleep(0);
                    Console.WriteLine("negative: " + Throws(() => Thread.Sleep(-2)) + ", infinite: " + Throws(() => Thread.Sleep(Timeout.Infinite)));
                    Console.WriteLine("yield: " + Thread.Yield());
                    Thread.SpinWait(100000);
                    Console.WriteLine("spin wait returned");
                    return 0;
                }
            }
            """ + ExceptionKindSource());

        var (exitCode, output, error) = await RunProgramInWasmtime(assets, assembly, ManagedExecutableCompletionShape.Asynchronous);

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}\n{output}");
        Assert.Equal(
            "slept at least 100 ms: True\n" +
            "queued work ran during the sleep: False, delay done: False\n" +
            "after await: True, delay done: True\n" +
            "negative: ArgumentOutOfRangeException, infinite: PlatformNotSupportedException\n" +
            "yield: False\n" +
            "spin wait returned\n",
            output);
    }

    // Task's single-threaded behavior against desktop .NET 11.0.0: continuation inlining, statuses, exception
    // aggregation, cancellation, continuation options, synchronization-context capture, AsyncLocal flow, WhenAny,
    // WaitAsync, ConfigureAwaitOptions, factories, Unwrap, WhenEach, Task.Run on the one thread, attached children and
    // ConcurrentExclusiveSchedulerPair. Observations that depend on desktop's thread hops are left out.
    [Fact]
    public async Task TaskSemanticsMatchDesktopInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            internal static class Program
            {
                private static readonly List<string> Log = new();
                private static void Note(string text) { Log.Add(text); Console.WriteLine(text); }

                private sealed class CountingContext : SynchronizationContext
                {
                    public int Posts;
                    public override void Post(SendOrPostCallback d, object? state) { Posts++; ThreadPool.QueueUserWorkItem(_ => { var previous = Current; SetSynchronizationContext(this); try { d(state); } finally { SetSynchronizationContext(previous); } }); }
                }

                private static readonly AsyncLocal<string?> Ambient = new();

                private static async Task<int> Main()
                {
                    await Step("inline continuations", InlineContinuations);
                    await Step("statuses", Statuses);
                    await Step("exceptions", Exceptions);
                    await Step("cancellation", Cancellation);
                    await Step("continuation options", ContinuationOptions);
                    await Step("context capture", ContextCapture);
                    await Step("async local flow", AsyncLocalFlow);
                    await Step("when any and wait async", WhenAnyAndWaitAsync);
                    await Step("configure await options", ConfigureAwaitOptionsCase);
                    await Step("factory and from async", FactoryCases);
                    await Step("unwrap and when each", UnwrapAndWhenEach);
                    await Step("task run", TaskRun);
                    await Step("attached child", AttachedChild);
                    await Step("scheduler pair", SchedulerPair);
                    return 0;
                }

                private static async Task Step(string name, Func<Task> body)
                {
                    Note("== " + name);
                    try { await body(); }
                    catch (Exception e) { Note("step threw " + ExceptionKind.Of(e) + ": " + e.Message); }
                }

                private static Task InlineContinuations()
                {
                    var plain = new TaskCompletionSource<int>();
                    var marker = "before";
                    var c1 = plain.Task.ContinueWith(_ => Note("default continuation saw " + marker), TaskContinuationOptions.ExecuteSynchronously);
                    plain.SetResult(1);
                    marker = "after";
                    Note("sync continuation ran inline: " + c1.IsCompleted);

                    var async = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var c2 = async.Task.ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);
                    async.SetResult(1);
                    Note("RunContinuationsAsynchronously ran inline: " + c2.IsCompleted);

                    var awaited = new TaskCompletionSource<int>();
                    var resumed = false;
                    async Task Awaiter() { await awaited.Task; resumed = true; }
                    var a = Awaiter();
                    awaited.SetResult(1);
                    Note("await continuation ran inline in SetResult: " + resumed + " " + a.IsCompleted);
                    return Task.WhenAll(c1, c2, a);
                }

                private static async Task Statuses()
                {
                    var tcs = new TaskCompletionSource();
                    Note("tcs: " + tcs.Task.Status);
                    var created = new Task(() => { });
                    Note("new Task: " + created.Status);
                    created.Start();
                    Note("started: " + created.Status);
                    await created;
                    Note("ran: " + created.Status);
                    var faulted = Task.FromException(new InvalidOperationException("x"));
                    Note("from exception: " + faulted.Status + " " + faulted.IsFaulted + " " + faulted.IsCompletedSuccessfully);
                    var canceled = Task.FromCanceled(new CancellationToken(true));
                    Note("from canceled: " + canceled.Status + " " + canceled.IsCanceled);
                    async Task<int> Pending() { await Task.Yield(); return 1; }
                    var pending = Pending();
                    Note("async method pending: " + pending.Status);
                    await pending;
                    var delay = Task.Delay(1);
                    Note("delay: " + delay.Status);
                    await delay;
                    Note("completed task same instance: " + ReferenceEquals(Task.CompletedTask, Task.CompletedTask));
                }

                private static async Task Exceptions()
                {
                    var t1 = Task.FromException(new InvalidOperationException("one"));
                    var t2 = Task.FromException(new ArgumentException("two"));
                    var all = Task.WhenAll(t1, t2);
                    try { await all; } catch (Exception e) { Note("await WhenAll threw " + ExceptionKind.Of(e) + ": " + e.Message); }
                    Note("WhenAll.Exception: " + ExceptionKind.Of(all.Exception!) + " inner " + all.Exception.InnerExceptions.Count + " [" + all.Exception.InnerExceptions[0].Message + ", " + all.Exception.InnerExceptions[1].Message + "]");
                    Note("aggregate message: " + all.Exception.Message);
                    Note("flatten: " + new AggregateException(all.Exception, new Exception("three")).Flatten().InnerExceptions.Count);
                    Note("t1.Exception inner: " + t1.Exception!.InnerException!.Message);
                    try { t1.GetAwaiter().GetResult(); } catch (Exception e) { Note("GetResult threw " + ExceptionKind.Of(e)); }
                    try { t1.Wait(); } catch (Exception e) { Note("Wait threw " + ExceptionKind.Of(e) + " inner " + ExceptionKind.Of(e.InnerException)); }
                    async Task Throws() { await Task.Yield(); throw new FormatException("async"); }
                    var thrown = Throws();
                    try { await thrown; } catch (Exception e) { Note("async throw: " + ExceptionKind.Of(e) + " status " + thrown.Status); }
                    var tcs = new TaskCompletionSource();
                    tcs.SetException(new Exception[] { new Exception("a"), new Exception("b") });
                    Note("tcs two exceptions: " + tcs.Task.Exception!.InnerExceptions.Count);
                    Note("TrySetResult after fault: " + tcs.TrySetResult());
                    try { tcs.SetResult(); } catch (Exception e) { Note("SetResult after fault threw " + ExceptionKind.Of(e)); }
                }

                private static async Task Cancellation()
                {
                    using var cts = new CancellationTokenSource();
                    cts.Cancel();
                    var canceled = Task.FromCanceled<int>(cts.Token);
                    try { await canceled; } catch (Exception e) { Note("await canceled: " + ExceptionKind.Of(e) + " token matches " + (((OperationCanceledException)e).CancellationToken == cts.Token)); }
                    var tcs = new TaskCompletionSource();
                    tcs.SetCanceled(cts.Token);
                    try { await tcs.Task; } catch (OperationCanceledException e) { Note("tcs canceled: " + ExceptionKind.Of(e) + " token matches " + (e.CancellationToken == cts.Token)); }
                    try { await Task.Delay(1000, cts.Token); } catch (Exception e) { Note("delay canceled: " + ExceptionKind.Of(e)); }
                    async Task ThrowsOce() { await Task.Yield(); throw new OperationCanceledException(cts.Token); }
                    var oce = ThrowsOce();
                    try { await oce; } catch (Exception e) { Note("async OCE: " + ExceptionKind.Of(e) + " status " + oce.Status); }
                    using var later = new CancellationTokenSource();
                    var registrations = 0;
                    later.Token.Register(() => registrations++);
                    later.Token.Register(() => registrations += 10);
                    later.Cancel();
                    Note("callbacks ran: " + registrations);
                    using var timed = new CancellationTokenSource(TimeSpan.FromMilliseconds(5));
                    try { await Task.Delay(Timeout.Infinite, timed.Token); } catch (Exception e) { Note("CancelAfter: " + ExceptionKind.Of(e) + " " + timed.IsCancellationRequested); }
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(later.Token, CancellationToken.None);
                    Note("linked canceled: " + linked.IsCancellationRequested);
                    var reset = new CancellationTokenSource();
                    Note("TryReset fresh: " + reset.TryReset());
                }

                private static async Task ContinuationOptions()
                {
                    var faulted = Task.FromException(new Exception("f"));
                    var onlyOk = faulted.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnRanToCompletion);
                    try { await onlyOk; } catch (Exception e) { Note("OnlyOnRanToCompletion on fault: " + onlyOk.Status + " " + ExceptionKind.Of(e)); }
                    var notCanceled = faulted.ContinueWith(t => t.Status.ToString(), TaskContinuationOptions.NotOnCanceled);
                    Note("NotOnCanceled: " + await notCanceled);
                    using var cts = new CancellationTokenSource();
                    var gate = new TaskCompletionSource();
                    var lazy = gate.Task.ContinueWith(_ => { }, cts.Token, TaskContinuationOptions.LazyCancellation, TaskScheduler.Default);
                    var eager = gate.Task.ContinueWith(_ => { }, cts.Token, TaskContinuationOptions.None, TaskScheduler.Default);
                    cts.Cancel();
                    Note("eager canceled before antecedent: " + eager.IsCanceled + ", lazy: " + lazy.IsCanceled);
                    gate.SetResult();
                    try { await lazy; } catch { }
                    Note("lazy after antecedent: " + lazy.Status);
                    var nested = Task.FromResult(5).ContinueWith(t => Task.FromResult(t.Result * 2));
                    Note("nested unwrap: " + await await nested);
                    Note("scheduler inside continuation is default: " + await Task.FromResult(0).ContinueWith(_ => TaskScheduler.Current == TaskScheduler.Default));
                }

                private static async Task ContextCapture()
                {
                    var context = new CountingContext();
                    var previous = SynchronizationContext.Current;
                    SynchronizationContext.SetSynchronizationContext(context);
                    try
                    {
                        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                        async Task<bool> Inner() { await tcs.Task; return SynchronizationContext.Current == context; }
                        async Task<bool> InnerFree() { await tcs.Task.ConfigureAwait(false); return SynchronizationContext.Current == context; }
                        var captured = Inner();
                        var free = InnerFree();
                        tcs.SetResult(1);
                        SynchronizationContext.SetSynchronizationContext(previous);
                        Note("captured resumed on context: " + await captured.ConfigureAwait(false) + ", ConfigureAwait(false) on context: " + await free.ConfigureAwait(false) + ", posts " + context.Posts);
                    }
                    finally { SynchronizationContext.SetSynchronizationContext(previous); }
                    SynchronizationContext.SetSynchronizationContext(context);
                    TaskScheduler fromContext;
                    try { fromContext = TaskScheduler.FromCurrentSynchronizationContext(); }
                    finally { SynchronizationContext.SetSynchronizationContext(previous); }
                    var before = context.Posts;
                    await Task.Factory.StartNew(() => { }, CancellationToken.None, TaskCreationOptions.None, fromContext);
                    Note("FromCurrentSynchronizationContext posts: " + (context.Posts - before));
                    var current = SynchronizationContext.Current;
                    SynchronizationContext.SetSynchronizationContext(null);
                    try { TaskScheduler.FromCurrentSynchronizationContext(); } catch (Exception e) { Note("no context: " + ExceptionKind.Of(e)); }
                    finally { SynchronizationContext.SetSynchronizationContext(current); }
                }

                private static async Task AsyncLocalFlow()
                {
                    Ambient.Value = "outer";
                    async Task<string?> ReadAfterYield() { await Task.Yield(); return Ambient.Value; }
                    Note("flows into async: " + await ReadAfterYield());
                    async Task SetInside() { Ambient.Value = "inner"; await Task.Yield(); }
                    await SetInside();
                    Note("does not flow back: " + Ambient.Value);
                    Task<string?> suppressed;
                    using (ExecutionContext.SuppressFlow()) { suppressed = Task.Factory.StartNew(() => Ambient.Value); }
                    Note("suppressed flow sees: " + (await suppressed ?? "null"));
                    var changes = new List<string>();
                    // Explicit changes only: which context switches notify depends on the threads continuations run on.
                    var tracked = new AsyncLocal<int>(args => { if (!args.ThreadContextChanged) changes.Add(args.PreviousValue + ">" + args.CurrentValue); });
                    tracked.Value = 1;
                    async Task Hop() { tracked.Value = 2; await Task.Yield(); }
                    await Hop();
                    Note("change notifications: " + string.Join(",", changes));
                    Ambient.Value = null;
                }

                private static async Task WhenAnyAndWaitAsync()
                {
                    var slow = new TaskCompletionSource<int>();
                    var winner = await Task.WhenAny(slow.Task, Task.FromResult(7));
                    Note("WhenAny winner result: " + ((Task<int>)winner).Result);
                    try { await slow.Task.WaitAsync(TimeSpan.FromMilliseconds(5)); } catch (Exception e) { Note("WaitAsync timeout: " + ExceptionKind.Of(e)); }
                    using var cts = new CancellationTokenSource();
                    var waiting = slow.Task.WaitAsync(cts.Token);
                    cts.Cancel();
                    try { await waiting; } catch (Exception e) { Note("WaitAsync canceled: " + ExceptionKind.Of(e)); }
                    Note("WhenAll empty: " + Task.WhenAll().Status + ", WhenAny empty throws: " + Throws(() => Task.WhenAny(Array.Empty<Task>())));
                }

                private static async Task ConfigureAwaitOptionsCase()
                {
                    var faulted = Task.FromException(new Exception("x"));
                    await faulted.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                    Note("SuppressThrowing: no throw, status " + faulted.Status);
                    var done = Task.CompletedTask;
                    var forced = done.ConfigureAwait(ConfigureAwaitOptions.ForceYielding).GetAwaiter();
                    Note("ForceYielding IsCompleted: " + forced.IsCompleted);
                    await done.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                    Note("SuppressThrowing on Task<T> throws: " + Throws(() => Task.FromResult(1).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)));
                }

                private static async Task FactoryCases()
                {
                    Note("StartNew: " + await Task.Factory.StartNew(() => 3));
                    var all = await Task.Factory.ContinueWhenAll(new[] { Task.FromResult(1), Task.FromResult(2) }, ts => ts.Length);
                    Note("ContinueWhenAll: " + all);
                    var apm = await Task.Factory.FromAsync((cb, st) => { var r = Task.FromResult(9); cb?.Invoke(r); return r; }, ar => ((Task<int>)ar).Result, null);
                    Note("FromAsync: " + apm);
                    var unstarted = new Task<int>(() => 4);
                    unstarted.RunSynchronously();
                    Note("RunSynchronously: " + unstarted.Result);
                    Note("Start twice throws: " + Throws(() => unstarted.Start()));
                }

                private static async Task UnwrapAndWhenEach()
                {
                    Task<Task<int>> outer = Task.FromResult(Task.FromResult(11));
                    Note("Unwrap: " + await outer.Unwrap());
                    var order = new List<int>();
                    var a = new TaskCompletionSource<int>(); var b = new TaskCompletionSource<int>();
                    b.SetResult(2); a.SetResult(1);
                    await foreach (var t in Task.WhenEach(a.Task, b.Task)) order.Add(t.Result);
                    Note("WhenEach order: " + string.Join(",", order));
                }

                private static async Task TaskRun()
                {
                    Note("Task.Run: " + await Task.Run(() => 5));
                    Note("Task.Run async: " + await Task.Run(async () => { await Task.Yield(); return 6; }));
                }

                private static async Task AttachedChild()
                {
                    Task? child = null;
                    var parent = Task.Factory.StartNew(() => { child = Task.Factory.StartNew(() => { }, TaskCreationOptions.AttachedToParent); });
                    await parent;
                    Note("parent waited for child: " + child!.IsCompleted);

                    var faulting = Task.Factory.StartNew(() =>
                    {
                        Task.Factory.StartNew(() => throw new InvalidOperationException("child"), TaskCreationOptions.AttachedToParent);
                        Task.CompletedTask.ContinueWith(_ => throw new ArgumentException("continuation"), TaskContinuationOptions.AttachedToParent);
                        Task.Factory.StartNew(() => throw new NotSupportedException("detached"));
                        throw new FormatException("parent");
                    });
                    try { await faulting; } catch (Exception e) { Note("await faulted parent: " + ExceptionKind.Of(e)); }
                    var inner = faulting.Exception!.InnerExceptions;
                    var shapes = new List<string>();
                    foreach (var x in inner) shapes.Add(x is AggregateException a ? "Aggregate(" + ExceptionKind.Of(a.InnerExceptions[0]) + ")" : ExceptionKind.Of(x));
                    shapes.Sort(StringComparer.Ordinal);
                    Note("parent faults: " + string.Join(",", shapes));

                    var denied = Task.Run(() => { Task.Factory.StartNew(() => throw new InvalidOperationException("denied"), TaskCreationOptions.AttachedToParent); });
                    await denied;
                    Note("Task.Run denies children: " + denied.Status);

                    using var cts = new CancellationTokenSource();
                    cts.Cancel();
                    var canceledChild = Task.Factory.StartNew(() => { Task.Factory.StartNew(() => { }, cts.Token, TaskCreationOptions.AttachedToParent, TaskScheduler.Default); });
                    await canceledChild;
                    Note("canceled child ignored: " + canceledChild.Status);

                    TaskCompletionSource? pending = null;
                    var waiting = Task.Factory.StartNew(() => { pending = new TaskCompletionSource(TaskCreationOptions.AttachedToParent); });
                    await Task.Delay(50);
                    Note("parent with pending child: " + waiting.Status);
                    pending!.SetResult();
                    await waiting;
                    Note("after child completes: " + waiting.Status);

                    var withResult = Task.Factory.StartNew(() => { Task.Factory.StartNew(() => { }, TaskCreationOptions.AttachedToParent); return 9; });
                    Note("parent result: " + await withResult);
                }

                private static async Task SchedulerPair()
                {
                    var pair = new ConcurrentExclusiveSchedulerPair();
                    var log = new List<string>();
                    var exclusive = Task.Factory.StartNew(() => log.Add("exclusive"), CancellationToken.None, TaskCreationOptions.None, pair.ExclusiveScheduler);
                    var concurrent = Task.Factory.StartNew(() => log.Add("concurrent"), CancellationToken.None, TaskCreationOptions.None, pair.ConcurrentScheduler);
                    await Task.WhenAll(exclusive, concurrent);
                    log.Sort(StringComparer.Ordinal);
                    Note("pair ran: " + string.Join(",", log) + ", exclusive max " + pair.ExclusiveScheduler.MaximumConcurrencyLevel);
                    Note("Current inside exclusive: " + await Task.Factory.StartNew(() => TaskScheduler.Current == pair.ExclusiveScheduler, CancellationToken.None, TaskCreationOptions.None, pair.ExclusiveScheduler));
                    pair.Complete();
                    await pair.Completion;
                    Note("completion: " + pair.Completion.Status);
                    try { Task.Factory.StartNew(() => { }, CancellationToken.None, TaskCreationOptions.None, pair.ExclusiveScheduler); Note("after complete: no"); }
                    catch (Exception e) { Note("after complete: " + ExceptionKind.Of(e) + (e.InnerException is null ? "" : "/" + ExceptionKind.Of(e.InnerException))); }
                    var faulty = new ConcurrentExclusiveSchedulerPair();
                    faulty.Complete();
                    await faulty.Completion;
                    Note("empty pair completes: " + faulty.Completion.Status);
                }

                private static string Throws(Action action)
                {
                    try { action(); return "no"; } catch (Exception e) { return ExceptionKind.Of(e); }
                }
            }
            """ + ExceptionKindSource());

        var (exitCode, output, error) = await RunProgramInWasmtime(assets, assembly, ManagedExecutableCompletionShape.Asynchronous);

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}\n{output}");
        Assert.Equal(
            "== inline continuations\n" +
            "default continuation saw before\n" +
            "sync continuation ran inline: True\n" +
            "RunContinuationsAsynchronously ran inline: False\n" +
            "await continuation ran inline in SetResult: True True\n" +
            "== statuses\n" +
            "tcs: WaitingForActivation\n" +
            "new Task: Created\n" +
            "started: WaitingToRun\n" +
            "ran: RanToCompletion\n" +
            "from exception: Faulted True False\n" +
            "from canceled: Canceled True\n" +
            "async method pending: WaitingForActivation\n" +
            "delay: WaitingForActivation\n" +
            "completed task same instance: True\n" +
            "== exceptions\n" +
            "await WhenAll threw InvalidOperationException: one\n" +
            "WhenAll.Exception: AggregateException inner 2 [one, two]\n" +
            "aggregate message: One or more errors occurred. (one) (two)\n" +
            "flatten: 3\n" +
            "t1.Exception inner: one\n" +
            "GetResult threw InvalidOperationException\n" +
            "Wait threw AggregateException inner InvalidOperationException\n" +
            "async throw: FormatException status Faulted\n" +
            "tcs two exceptions: 2\n" +
            "TrySetResult after fault: False\n" +
            "SetResult after fault threw InvalidOperationException\n" +
            "== cancellation\n" +
            "await canceled: TaskCanceledException token matches True\n" +
            "tcs canceled: TaskCanceledException token matches True\n" +
            "delay canceled: TaskCanceledException\n" +
            "async OCE: OperationCanceledException status Canceled\n" +
            "callbacks ran: 11\n" +
            "CancelAfter: TaskCanceledException True\n" +
            "linked canceled: True\n" +
            "TryReset fresh: True\n" +
            "== continuation options\n" +
            "OnlyOnRanToCompletion on fault: Canceled TaskCanceledException\n" +
            "NotOnCanceled: Faulted\n" +
            "eager canceled before antecedent: True, lazy: False\n" +
            "lazy after antecedent: Canceled\n" +
            "nested unwrap: 10\n" +
            "scheduler inside continuation is default: True\n" +
            "== context capture\n" +
            "captured resumed on context: True, ConfigureAwait(false) on context: False, posts 1\n" +
            "FromCurrentSynchronizationContext posts: 1\n" +
            "no context: InvalidOperationException\n" +
            "== async local flow\n" +
            "flows into async: outer\n" +
            "does not flow back: outer\n" +
            "suppressed flow sees: null\n" +
            "change notifications: 0>1,1>2\n" +
            "== when any and wait async\n" +
            "WhenAny winner result: 7\n" +
            "WaitAsync timeout: TimeoutException\n" +
            "WaitAsync canceled: TaskCanceledException\n" +
            "WhenAll empty: RanToCompletion, WhenAny empty throws: ArgumentException\n" +
            "== configure await options\n" +
            "SuppressThrowing: no throw, status Faulted\n" +
            "ForceYielding IsCompleted: False\n" +
            "SuppressThrowing on Task<T> throws: ArgumentOutOfRangeException\n" +
            "== factory and from async\n" +
            "StartNew: 3\n" +
            "ContinueWhenAll: 2\n" +
            "FromAsync: 9\n" +
            "RunSynchronously: 4\n" +
            "Start twice throws: InvalidOperationException\n" +
            "== unwrap and when each\n" +
            "Unwrap: 11\n" +
            "WhenEach order: 1,2\n" +
            "== task run\n" +
            "Task.Run: 5\n" +
            "Task.Run async: 6\n" +
            "== attached child\n" +
            "parent waited for child: True\n" +
            "await faulted parent: FormatException\n" +
            "parent faults: Aggregate(ArgumentException),Aggregate(InvalidOperationException),FormatException\n" +
            "Task.Run denies children: RanToCompletion\n" +
            "canceled child ignored: RanToCompletion\n" +
            "parent with pending child: WaitingForChildrenToComplete\n" +
            "after child completes: RanToCompletion\n" +
            "parent result: 9\n" +
            "== scheduler pair\n" +
            "pair ran: concurrent,exclusive, exclusive max 1\n" +
            "Current inside exclusive: True\n" +
            "completion: RanToCompletion\n" +
            "after complete: TaskSchedulerException/InvalidOperationException\n" +
            "empty pair completes: RanToCompletion\n",
            output);
    }

    // More of Task against desktop .NET 11.0.0: IDs assigned on first use, custom schedulers (Current, HideScheduler,
    // inheritance, inlining), async void and its synchronization context, IValueTaskSource, faulted and canceled
    // results, WaitAll and WaitAny on completed tasks, wait handles and disposal (cached tasks ignore Dispose), and
    // Task's state members.
    [Fact]
    public async Task TaskSchedulingMatchesDesktopInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using System.Threading.Tasks.Sources;

            internal static class Program
            {
                private static void Note(string text) => Console.WriteLine(text);

                private sealed class RecordingScheduler : TaskScheduler
                {
                    public int Queued, Inlined;
                    private readonly Queue<Task> _tasks = new();
                    protected override void QueueTask(Task task) { Queued++; _tasks.Enqueue(task); ThreadPool.QueueUserWorkItem(_ => TryExecuteTask(_tasks.Dequeue())); }
                    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) { Inlined++; return !taskWasPreviouslyQueued && TryExecuteTask(task); }
                    protected override IEnumerable<Task> GetScheduledTasks() => _tasks;
                    public override int MaximumConcurrencyLevel => 1;
                }

                private sealed class PostingContext : SynchronizationContext
                {
                    public readonly List<string> Seen = new();
                    public override void Post(SendOrPostCallback d, object? state) { Seen.Add("post"); ThreadPool.QueueUserWorkItem(_ => { try { d(state); } catch (Exception e) { Seen.Add("callback threw " + ExceptionKind.Of(e)); } }); }
                    public override void OperationStarted() => Seen.Add("started");
                    public override void OperationCompleted() => Seen.Add("completed");
                }

                private sealed class Source : IValueTaskSource<int>
                {
                    public ManualResetValueTaskSourceCore<int> Core;
                    public int GetResult(short token) => Core.GetResult(token);
                    public ValueTaskSourceStatus GetStatus(short token) => Core.GetStatus(token);
                    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => Core.OnCompleted(continuation, state, token, flags);
                }

                private static async Task<int> Main()
                {
                    await Step("ids", Ids);
                    await Step("schedulers", Schedulers);
                    await Step("async void", AsyncVoid);
                    await Step("value task sources", ValueTaskSources);
                    await Step("faulted results", FaultedResults);
                    await Step("misc", Misc);
                    return 0;
                }

                private static async Task Step(string name, Func<Task> body)
                {
                    Note("== " + name);
                    try { await body(); } catch (Exception e) { Note("step threw " + ExceptionKind.Of(e) + ": " + e.Message); }
                }

                private static Task Ids()
                {
                    var a = new TaskCompletionSource().Task; var b = new TaskCompletionSource().Task; var c = new TaskCompletionSource().Task;
                    var idB = b.Id; var idA = a.Id;
                    Note("ids assigned on first use: " + (idA == idB + 1) + ", distinct " + (c.Id != idA));
                    Note("CurrentId outside a task: " + (Task.CurrentId?.ToString() ?? "null"));
                    return Task.Factory.StartNew(() => Note("CurrentId inside a task equals its Id: " + (Task.CurrentId != null)));
                }

                private static async Task Schedulers()
                {
                    var scheduler = new RecordingScheduler();
                    var inner = await Task.Factory.StartNew(() => TaskScheduler.Current == scheduler, CancellationToken.None, TaskCreationOptions.None, scheduler);
                    Note("Current inside custom scheduler: " + inner + ", queued " + scheduler.Queued);
                    var hidden = await Task.Factory.StartNew(() => TaskScheduler.Current == TaskScheduler.Default, CancellationToken.None, TaskCreationOptions.HideScheduler, scheduler);
                    Note("HideScheduler shows default: " + hidden);
                    var nestedScheduler = await Task.Factory.StartNew(() => Task.Factory.StartNew(() => TaskScheduler.Current == scheduler), CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap();
                    Note("nested StartNew inherits scheduler: " + nestedScheduler);
                    var unstarted = new Task<int>(() => 1);
                    unstarted.RunSynchronously(scheduler);
                    Note("RunSynchronously on custom scheduler: " + unstarted.Result + ", inlined " + scheduler.Inlined);
                    var c = Task.FromResult(1).ContinueWith(_ => TaskScheduler.Current == scheduler, scheduler);
                    Note("ContinueWith on scheduler: " + await c);
                    Note("Default.Id stable: " + (TaskScheduler.Default.Id == TaskScheduler.Default.Id) + ", MaxConcurrency>0: " + (TaskScheduler.Default.MaximumConcurrencyLevel > 0));
                    Note("DenyChildAttach accepted: " + await Task.Factory.StartNew(() => 1, TaskCreationOptions.DenyChildAttach));
                    Note("StartNew bad options throws: " + Throws(() => Task.Factory.StartNew(() => { }, (TaskCreationOptions)0x1000)));
                }

                private static async Task AsyncVoid()
                {
                    var context = new PostingContext();
                    var previous = SynchronizationContext.Current;
                    var done = new TaskCompletionSource();
                    SynchronizationContext.SetSynchronizationContext(context);
                    try
                    {
                        async void Fire() { await Task.Yield(); done.SetResult(); throw new InvalidOperationException("void"); }
                        Fire();
                    }
                    finally { SynchronizationContext.SetSynchronizationContext(previous); }
                    await done.Task;
                    await Task.Delay(10);
                    // Sorted: whether the posted exception runs before OperationCompleted depends on the thread it is posted to.
                    context.Seen.Sort(StringComparer.Ordinal);
                    Note("async void context events: " + string.Join(",", context.Seen));
                }

                private static async Task ValueTaskSources()
                {
                    var source = new Source();
                    var vt = new ValueTask<int>(source, source.Core.Version);
                    Note("pending status: " + source.Core.GetStatus(source.Core.Version) + ", IsCompleted " + vt.IsCompleted);
                    source.Core.SetResult(42);
                    Note("result: " + await vt);
                    Note("stale token throws: " + Throws(() => source.Core.GetResult((short)(source.Core.Version + 1))));
                    source.Core.Reset();
                    Note("version after reset: " + source.Core.Version);
                    var fresh = new ValueTask<int>(source, source.Core.Version);
                    source.Core.SetException(new FormatException("vts"));
                    try { await fresh; } catch (Exception e) { Note("fault: " + ExceptionKind.Of(e)); }
                    Note("double SetResult throws: " + Throws(() => source.Core.SetResult(1)));
                    var asTask = new ValueTask<int>(7).AsTask();
                    Note("completed ValueTask AsTask: " + asTask.Result + " " + asTask.Status);
                    Note("ValueTask.CompletedTask completed: " + ValueTask.CompletedTask.IsCompletedSuccessfully);
                    Note("ValueTask.FromException: " + ValueTask.FromException<int>(new Exception("e")).IsFaulted);
                }

                private static async Task FaultedResults()
                {
                    var faulted = Task.FromException<int>(new ArgumentException("bad"));
                    try { var _ = faulted.Result; } catch (Exception e) { Note("Result on faulted: " + ExceptionKind.Of(e) + " / " + ExceptionKind.Of(e.InnerException!)); }
                    var canceled = Task.FromCanceled<int>(new CancellationToken(true));
                    try { var _ = canceled.Result; } catch (Exception e) { Note("Result on canceled: " + ExceptionKind.Of(e) + " / " + ExceptionKind.Of(e.InnerException!)); }
                    try { Task.WaitAll(faulted, canceled); } catch (AggregateException e) { Note("WaitAll: " + e.InnerExceptions.Count + " " + ExceptionKind.Of(e.InnerExceptions[0]) + "," + ExceptionKind.Of(e.InnerExceptions[1])); }
                    Note("WaitAny completed: " + Task.WaitAny(faulted, canceled));
                    Note("Wait(0) on pending: " + new TaskCompletionSource().Task.Wait(0));
                    try { await Task.WhenAll(canceled); } catch (Exception e) { Note("WhenAll canceled: " + ExceptionKind.Of(e)); }
                    var mixed = Task.WhenAll(canceled, faulted);
                    try { await mixed; } catch { }
                    Note("WhenAll canceled+faulted status: " + mixed.Status);
                    Note("exception observed by Exception getter: " + (faulted.Exception != null));
                }

                private static async Task Misc()
                {
                    var t = Task.FromResult(1);
                    Note("Dispose completed: " + Throws(() => t.Dispose()));
                    Note("Dispose pending throws: " + Throws(() => new TaskCompletionSource().Task.Dispose()));
                    var handed = Task.FromResult(2);
                    var handle = ((IAsyncResult)handed).AsyncWaitHandle;
                    Note("wait handle signaled: " + handle.WaitOne(0));
                    handed.Dispose();
                    Note("wait handle after Dispose: " + Throws(() => _ = ((IAsyncResult)handed).AsyncWaitHandle));
                    var pendingSource = new TaskCompletionSource();
                    var pendingHandle = ((IAsyncResult)pendingSource.Task).AsyncWaitHandle;
                    Note("pending handle signaled: " + pendingHandle.WaitOne(0));
                    pendingSource.SetResult();
                    Note("handle after completion: " + pendingHandle.WaitOne(0));
                    pendingSource.Task.Dispose();
                    Note("disposable task's handle after Dispose: " + Throws(() => _ = ((IAsyncResult)pendingSource.Task).AsyncWaitHandle));
                    var neverHanded = new TaskCompletionSource();
                    neverHanded.SetResult();
                    neverHanded.Task.Dispose();
                    Note("handle first asked after Dispose: " + Throws(() => _ = ((IAsyncResult)neverHanded.Task).AsyncWaitHandle));
                    Note("AsyncState: " + (Task.Factory.StartNew(s => s, "state").AsyncState));
                    Note("CreationOptions: " + new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task.CreationOptions);
                    var tcs = new TaskCompletionSource<int>("st");
                    Note("TCS state: " + tcs.Task.AsyncState);
                    Note("TCS bad options throws: " + Throws(() => new TaskCompletionSource(TaskCreationOptions.LongRunning)));
                    var yielded = new List<int>();
                    var first = Task.Run(async () => { yielded.Add(1); await Task.Yield(); yielded.Add(3); });
                    yielded.Add(2);
                    await first;
                    Note("yield order recorded: " + yielded.Count);
                }

                private static string Throws(Action action)
                {
                    try { action(); return "no"; } catch (Exception e) { return ExceptionKind.Of(e); }
                }
            }
            """ + ExceptionKindSource());

        var (exitCode, output, error) = await RunProgramInWasmtime(assets, assembly, ManagedExecutableCompletionShape.Asynchronous);

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}\n{output}");
        Assert.Equal(
            "== ids\n" +
            "ids assigned on first use: True, distinct True\n" +
            "CurrentId outside a task: null\n" +
            "CurrentId inside a task equals its Id: True\n" +
            "== schedulers\n" +
            "Current inside custom scheduler: True, queued 1\n" +
            "HideScheduler shows default: True\n" +
            "nested StartNew inherits scheduler: True\n" +
            "RunSynchronously on custom scheduler: 1, inlined 1\n" +
            "ContinueWith on scheduler: True\n" +
            "Default.Id stable: True, MaxConcurrency>0: True\n" +
            "DenyChildAttach accepted: 1\n" +
            "StartNew bad options throws: ArgumentOutOfRangeException\n" +
            "== async void\n" +
            "async void context events: callback threw InvalidOperationException,completed,post,post,started\n" +
            "== value task sources\n" +
            "pending status: Pending, IsCompleted False\n" +
            "result: 42\n" +
            "stale token throws: InvalidOperationException\n" +
            "version after reset: 1\n" +
            "fault: FormatException\n" +
            "double SetResult throws: InvalidOperationException\n" +
            "completed ValueTask AsTask: 7 RanToCompletion\n" +
            "ValueTask.CompletedTask completed: True\n" +
            "ValueTask.FromException: True\n" +
            "== faulted results\n" +
            "Result on faulted: AggregateException / ArgumentException\n" +
            "Result on canceled: AggregateException / TaskCanceledException\n" +
            "WaitAll: 2 ArgumentException,TaskCanceledException\n" +
            "WaitAny completed: 0\n" +
            "Wait(0) on pending: False\n" +
            "WhenAll canceled: TaskCanceledException\n" +
            "WhenAll canceled+faulted status: Faulted\n" +
            "exception observed by Exception getter: True\n" +
            "== misc\n" +
            "Dispose completed: no\n" +
            "Dispose pending throws: InvalidOperationException\n" +
            "wait handle signaled: True\n" +
            "wait handle after Dispose: no\n" +
            "pending handle signaled: False\n" +
            "handle after completion: True\n" +
            "disposable task's handle after Dispose: ObjectDisposedException\n" +
            "handle first asked after Dispose: ObjectDisposedException\n" +
            "AsyncState: state\n" +
            "CreationOptions: RunContinuationsAsynchronously\n" +
            "TCS state: st\n" +
            "TCS bad options throws: ArgumentOutOfRangeException\n" +
            "yield order recorded: 3\n",
            output);
    }

    // System.Transactions.Local on upstream's engine: volatile and single durable enlistments, votes, promotable
    // single-phase enlistments, nested scopes, dependent clones, explicit commit and rollback, async flow, and a
    // timeout that the transaction table's timer reaches on the event loop. Promotion to a distributed transaction
    // throws, as on Linux. The expected text is desktop .NET 11.0.0's on Linux.
    [Fact]
    public async Task LocalTransactionsMatchDesktopInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using System.Transactions;

            namespace TransactionsFixture;

            internal sealed class Recorder : IEnlistmentNotification, ISinglePhaseNotification
            {
                private readonly string _name;
                private readonly bool _forceRollback;
                public static readonly List<string> Log = new();
                public Recorder(string name, bool forceRollback = false) { _name = name; _forceRollback = forceRollback; }
                public void Prepare(PreparingEnlistment e) { Log.Add(_name + ".prepare"); if (_forceRollback) e.ForceRollback(); else e.Prepared(); }
                public void Commit(Enlistment e) { Log.Add(_name + ".commit"); e.Done(); }
                public void Rollback(Enlistment e) { Log.Add(_name + ".rollback"); e.Done(); }
                public void InDoubt(Enlistment e) { Log.Add(_name + ".indoubt"); e.Done(); }
                public void SinglePhaseCommit(SinglePhaseEnlistment e) { Log.Add(_name + ".singlephase"); e.Committed(); }
            }

            internal sealed class Promotable : IPromotableSinglePhaseNotification
            {
                public static readonly List<string> Log = new();
                public void Initialize() => Log.Add("pspe.initialize");
                public void SinglePhaseCommit(SinglePhaseEnlistment e) { Log.Add("pspe.singlephase"); e.Committed(); }
                public void Rollback(SinglePhaseEnlistment e) { Log.Add("pspe.rollback"); e.Aborted(); }
                public byte[] Promote() { Log.Add("pspe.promote"); return new byte[] { 1 }; }
            }

            internal static class Program
            {
                private static void Note(string text) => Console.WriteLine(text);
                private static string Flush() { var s = string.Join(",", Recorder.Log); Recorder.Log.Clear(); return s; }
                private static string Throws(Action action)
                {
                    try { action(); return "no"; }
                    catch (Exception e) { return ExceptionKind.Of(e) + (e.InnerException is null ? "" : "/" + ExceptionKind.Of(e.InnerException)); }
                }

                private static async Task<int> Main()
                {
                    Note("defaults: " + TransactionManager.DefaultTimeout + " " + TransactionManager.MaximumTimeout + " current " + (Transaction.Current is null));

                    string completed = "";
                    using (var scope = new TransactionScope())
                    {
                        var tx = Transaction.Current!;
                        tx.TransactionCompleted += (_, e) => completed = e.Transaction!.TransactionInformation.Status.ToString();
                        tx.EnlistVolatile(new Recorder("a"), EnlistmentOptions.None);
                        tx.EnlistVolatile(new Recorder("b"), EnlistmentOptions.None);
                        Note("inside: " + tx.TransactionInformation.Status + " distributed " + (tx.TransactionInformation.DistributedIdentifier == Guid.Empty) + " isolation " + tx.IsolationLevel);
                        scope.Complete();
                    }
                    Note("commit: " + Flush() + " completed " + completed + " current " + (Transaction.Current is null));

                    Transaction? rolled = null;
                    using (new TransactionScope())
                    {
                        rolled = Transaction.Current!.Clone();
                        Transaction.Current!.EnlistVolatile(new Recorder("c"), EnlistmentOptions.None);
                    }
                    Note("no complete: " + Flush() + " " + rolled.TransactionInformation.Status);

                    Note("vote rollback: " + Throws(() =>
                    {
                        using var scope = new TransactionScope();
                        Transaction.Current!.EnlistVolatile(new Recorder("d", forceRollback: true), EnlistmentOptions.None);
                        Transaction.Current!.EnlistVolatile(new Recorder("e"), EnlistmentOptions.None);
                        scope.Complete();
                    }) + " " + Flush());

                    using (var scope = new TransactionScope())
                    {
                        Transaction.Current!.EnlistDurable(Guid.NewGuid(), new Recorder("durable"), EnlistmentOptions.None);
                        Transaction.Current!.EnlistVolatile(new Recorder("v"), EnlistmentOptions.None);
                        scope.Complete();
                    }
                    Note("one durable: " + Flush());

                    Note("two durables: " + Throws(() =>
                    {
                        using var scope = new TransactionScope();
                        Transaction.Current!.EnlistDurable(Guid.NewGuid(), new Recorder("d1"), EnlistmentOptions.None);
                        Transaction.Current!.EnlistDurable(Guid.NewGuid(), new Recorder("d2"), EnlistmentOptions.None);
                        scope.Complete();
                    }) + " " + Flush());

                    using (var scope = new TransactionScope())
                    {
                        Note("pspe enlisted: " + Transaction.Current!.EnlistPromotableSinglePhase(new Promotable()));
                        scope.Complete();
                    }
                    Note("pspe: " + string.Join(",", Promotable.Log));
                    Promotable.Log.Clear();
                    Note("pspe then durable: " + Throws(() =>
                    {
                        using var scope = new TransactionScope();
                        Transaction.Current!.EnlistPromotableSinglePhase(new Promotable());
                        Transaction.Current!.EnlistDurable(Guid.NewGuid(), new Recorder("d3"), EnlistmentOptions.None);
                        scope.Complete();
                    }) + " " + string.Join(",", Promotable.Log) + " " + Flush());
                    Promotable.Log.Clear();

                    using (var outer = new TransactionScope())
                    {
                        var o = Transaction.Current;
                        using (var joined = new TransactionScope(TransactionScopeOption.Required)) { Note("required joins: " + (Transaction.Current == o)); joined.Complete(); }
                        using (var fresh = new TransactionScope(TransactionScopeOption.RequiresNew)) { Note("requires new: " + (Transaction.Current != o)); fresh.Complete(); }
                        using (new TransactionScope(TransactionScopeOption.Suppress)) { Note("suppress: " + (Transaction.Current is null)); }
                        Note("restored: " + (Transaction.Current == o));
                        outer.Complete();
                    }

                    Note("inner not completed: " + Throws(() =>
                    {
                        using var outer = new TransactionScope();
                        using (new TransactionScope()) { }
                        outer.Complete();
                    }));

                    using (var committable = new CommittableTransaction())
                    {
                        committable.EnlistVolatile(new Recorder("ct"), EnlistmentOptions.None);
                        var dependent = committable.DependentClone(DependentCloneOption.BlockCommitUntilComplete);
                        dependent.Complete();
                        committable.Commit();
                        Note("committable: " + Flush() + " " + committable.TransactionInformation.Status);
                    }
                    using (var committable = new CommittableTransaction())
                    {
                        committable.EnlistVolatile(new Recorder("rb"), EnlistmentOptions.None);
                        committable.Rollback(new FormatException("why"));
                        Note("rollback with reason: " + Flush() + " " + committable.TransactionInformation.Status + " commit after: " + Throws(() => committable.Commit()));
                    }

                    using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
                    {
                        var before = Transaction.Current;
                        await Task.Yield();
                        Note("flows across await: " + (Transaction.Current == before));
                        scope.Complete();
                    }

                    var timedOut = "no";
                    try
                    {
                        using var scope = new TransactionScope(TransactionScopeOption.Required, TimeSpan.FromMilliseconds(50), TransactionScopeAsyncFlowOption.Enabled);
                        Transaction.Current!.EnlistVolatile(new Recorder("t"), EnlistmentOptions.None);
                        await Task.Delay(1500);
                        Note("status after timeout: " + Transaction.Current!.TransactionInformation.Status);
                        scope.Complete();
                    }
                    catch (Exception e) { timedOut = ExceptionKind.Of(e) + "/" + ExceptionKind.Of(e.InnerException); }
                    Note("timeout: " + timedOut + " " + Flush());

                    Note("bad timeout: " + Throws(() => new TransactionScope(TransactionScopeOption.Required, TimeSpan.FromSeconds(-2))));
                    Note("set current: " + Throws(() => { using var c = new CommittableTransaction(); Transaction.Current = c; Transaction.Current = null; }));
                    return 0;
                }
            }
            """ + ExceptionKindSource("System.Transactions.TransactionAbortedException"));

        var (exitCode, output, error) = await RunProgramInWasmtime(assets, assembly, ManagedExecutableCompletionShape.Asynchronous);

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}\n{output}");
        Assert.Equal(
            "defaults: 00:01:00 00:10:00 current True\n" +
            "inside: Active distributed True isolation Serializable\n" +
            "commit: a.prepare,b.prepare,a.commit,b.commit completed Committed current True\n" +
            "no complete: c.rollback Aborted\n" +
            "vote rollback: TransactionAbortedException d.prepare,e.rollback\n" +
            "one durable: v.prepare,durable.singlephase,v.commit\n" +
            "two durables: PlatformNotSupportedException d1.rollback\n" +
            "pspe enlisted: True\n" +
            "pspe: pspe.initialize,pspe.singlephase\n" +
            "pspe then durable: InvalidOperationException/ArgumentException pspe.initialize,pspe.promote,pspe.rollback \n" +
            "required joins: True\n" +
            "requires new: True\n" +
            "suppress: True\n" +
            "restored: True\n" +
            "inner not completed: TransactionAbortedException\n" +
            "committable: ct.singlephase Committed\n" +
            "rollback with reason: rb.rollback Aborted commit after: TransactionAbortedException/FormatException\n" +
            "flows across await: True\n" +
            "status after timeout: Aborted\n" +
            "timeout: TransactionAbortedException/TimeoutException t.rollback\n" +
            "bad timeout: ArgumentOutOfRangeException\n" +
            "set current: no\n",
            output);
    }

    // Synchronous HTTP blocks on wasi:http as NetWasm's synchronous file and socket I/O does: HttpClient.Send (a GET,
    // and a POST whose body is copied once the send has started), HttpWebRequest.GetResponse and WebClient's
    // DownloadString, UploadString (a body of several stream chunks), DownloadFile and UploadFile, from a synchronous
    // Main, against an HTTP server the test runs.
    [Fact]
    public async Task SynchronousHttpSendsInWasmtime()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExecutable(assets, true, """
            using System;
            using System.IO;
            using System.Net;
            using System.Net.Http;
            using System.Text;
            namespace SyncHttpFixture;
            internal static class Program
            {
                public static int Main()
                {
                    var root = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("HTTP_PORT") + "/";
                    using var client = new HttpClient();
                    using (var response = client.Send(new HttpRequestMessage(HttpMethod.Get, root + "get")))
                    using (var reader = new StreamReader(response.Content.ReadAsStream()))
                    {
                        Console.WriteLine("send: " + (int)response.StatusCode + " " + reader.ReadToEnd());
                    }
                    var post = new HttpRequestMessage(HttpMethod.Post, root + "post") { Content = new StringContent("posted body") };
                    using (var response = client.Send(post))
                    using (var reader = new StreamReader(response.Content.ReadAsStream()))
                    {
                        Console.WriteLine("post: " + reader.ReadToEnd());
                    }
                    #pragma warning disable SYSLIB0014
                    var request = WebRequest.CreateHttp(root + "request");
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        Console.WriteLine("request: " + (int)response.StatusCode + " " + reader.ReadToEnd());
                    }
                    using (var web = new WebClient())
                    {
                        Console.WriteLine("webclient: " + web.DownloadString(root + "webclient"));
                        Console.WriteLine("upload: " + web.UploadString(root + "upload", new string('x', 20000)));
                        web.DownloadFile(root + "file", "/work/downloaded.txt");
                        Console.WriteLine("downloadfile: " + File.ReadAllText("/work/downloaded.txt"));
                        File.WriteAllText("/work/upload.txt", "file body");
                        Console.WriteLine("uploadfile: " + Encoding.UTF8.GetString(web.UploadFile(root + "upfile", "/work/upload.txt")));
                    }
                    #pragma warning restore SYSLIB0014
                    return 0;
                }
            }
            """);

        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        using var stop = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                System.Net.HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (Exception) { return; }
                string body;
                using (var reader = new StreamReader(context.Request.InputStream))
                {
                    body = await reader.ReadToEndAsync();
                }
                var text = context.Request.HttpMethod + " " + context.Request.Url!.AbsolutePath
                    + (body.Length == 0 ? "" : body.Length > 100 ? " " + body.Length + " bytes" : " " + body);
                if (context.Request.ContentType?.StartsWith("multipart/form-data", StringComparison.Ordinal) == true)
                {
                    var start = body.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
                    var end = body.IndexOf("\r\n--", start, StringComparison.Ordinal);
                    text = context.Request.HttpMethod + " " + context.Request.Url!.AbsolutePath + " multipart " + body[start..end];
                }
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }, TestContext.Current.CancellationToken);

        var work = Path.Combine(assets.Directory, "work");
        Directory.CreateDirectory(work);
        var (exitCode, output, error) = await RunProgramInWasmtime(assets, assembly,
            ManagedExecutableCompletionShape.Synchronous, "-S", "inherit-network,allow-ip-name-lookup,http",
            "--env", $"HTTP_PORT={port}", "--dir", work + "::/work");
        stop.Cancel();
        listener.Stop();

        Assert.True(exitCode == 0, $"exit code {exitCode}: {error}\n{output}");
        Assert.Equal(
            "send: 200 GET /get\n" +
            "post: POST /post posted body\n" +
            "request: 200 GET /request\n" +
            "webclient: GET /webclient\n" +
            "upload: POST /upload 20000 bytes\n" +
            "downloadfile: GET /file\n" +
            "uploadfile: POST /upfile multipart " + "file body" + "\n",
            output);
    }

    // One SMTP session: a greeting, 250 for every command, the message up to its terminating dot, and QUIT.
    private static async Task ServeSmtpAsync(
        System.Net.Sockets.TcpListener listener, List<string> commands, System.Text.StringBuilder data)
    {
        using var connection = await listener.AcceptTcpClientAsync();
        using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII);
        using var writer = new StreamWriter(stream, System.Text.Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 test.example ESMTP");
        while (await reader.ReadLineAsync() is { } line)
        {
            commands.Add(line);
            if (line.StartsWith("EHLO", StringComparison.Ordinal)) await writer.WriteLineAsync("250 test.example");
            else if (line == "DATA")
            {
                await writer.WriteLineAsync("354 Send data");
                while (await reader.ReadLineAsync() is { } body && body != ".") data.AppendLine(body);
                await writer.WriteLineAsync("250 Queued");
            }
            else if (line == "QUIT") { await writer.WriteLineAsync("221 Bye"); return; }
            else await writer.WriteLineAsync("250 OK");
        }
    }

    // Compiles a synchronous Main to a WASI 0.3 command and runs it in Wasmtime with the given
    // arguments (preopened directories).
    private static Task<(int ExitCode, string Output, string Error)> RunFileProgramInWasmtime(
        TestAssets assets,
        string assembly,
        params string[] wasmtimeArguments) =>
        RunProgramInWasmtime(assets, assembly, ManagedExecutableCompletionShape.Synchronous, wasmtimeArguments);

    // Compiles Main to a WASI 0.3 command and runs it in Wasmtime with the given arguments.
    private static async Task<(int ExitCode, string Output, string Error)> RunProgramInWasmtime(
        TestAssets assets,
        string assembly,
        ManagedExecutableCompletionShape completion,
        params string[] wasmtimeArguments)
    {
        var component = await PackageProgram(assets, assembly, completion);
        return await RunWasmtimeWithOutput(assets.Root, [.. wasmtimeArguments, component]);
    }

    // Compiles Main to a WASI 0.3 command component and returns its path.
    private static async Task<string> PackageProgram(
        TestAssets assets,
        string assembly,
        ManagedExecutableCompletionShape completion,
        ManagedExecutableReturnShape returnShape = ManagedExecutableReturnShape.ExitCode,
        FinalWasmOptimization optimization = FinalWasmOptimization.None)
    {
        var entry = ReadEntryPoint(assembly);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], entry.TypeName, entry.MethodName, [], WasmTarget.Wasm32,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EntryMethodToken: entry.Token,
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable));
        var application = Path.Combine(assets.Directory, "application.wasm");
        var runtime = Path.Combine(assets.Directory, "runtime.wasm");
        var layout = Path.Combine(assets.Directory, "runtime-layout.json");
        var component = Path.Combine(assets.Directory, "files.component.wasm");
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
        var toolchainWit = Path.Combine(assets.Root, "src", "NetWasm.Toolchain", "wit");
        using var services = new ServiceCollection().AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var world = services.GetRequiredService<IWitDocumentReader>().Read(toolchainWit)
            .SelectWorld("netwasm:command/command@1.0.0");
        services.GetRequiredService<IComponentPackager>().Package(new(
            application, toolchainWit,
            services.GetRequiredService<IWitWorldSpecifierFormatter>().Format(world), component,
            ComponentTarget.Wasm32Wasi02, runtime,
            new(ManagedExecutableParameterShape.None, returnShape, completion),
            optimization));
        return component;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunWasmtimeWithInput(
        string workingDirectory,
        string input,
        params string[] arguments)
    {
        var start = new ProcessStartInfo("wasmtime")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("run");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Wasmtime did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.BaseStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(input));
        process.StandardInput.Close();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output, await error);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunWasmtimeWithOutput(
        string workingDirectory,
        params string[] arguments)
    {
        var start = new ProcessStartInfo("wasmtime")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("run");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Wasmtime did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output, await error);
    }

    private static async Task<(int ExitCode, byte[] Output, string Error)> RunWasmtimeWithOutputBytes(
        string workingDirectory,
        string component)
    {
        var start = new ProcessStartInfo("wasmtime")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add(component);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Wasmtime did not start.");
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await copy;
        return (process.ExitCode, output.ToArray(), await error);
    }

    private static async Task<(int ExitCode, string Error)> RunWasmtime(string workingDirectory, string component)
    {
        var start = new ProcessStartInfo("wasmtime")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add(component);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Wasmtime did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output;
        return (process.ExitCode, await error);
    }

    // A managed executable packaged as the SDK packages a command with structured diagnostics: its
    // application and NetWasm's runtime under the diagnostic command world.
    private static async Task<string> PackageCommand(
        TestAssets assets,
        bool optimize,
        string source,
        ManagedExecutableEntryPointAbi entryPoint)
    {
        var assembly = CompileExecutable(assets, optimize, source);
        var entry = ReadEntryPoint(assembly);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], entry.TypeName, entry.MethodName, [], WasmTarget.Wasm32,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0",
            EntryMethodToken: entry.Token,
            EntryPointKind: CompilerEntryPointKind.ManagedExecutable,
            StructuredDiagnostics: true));
        var application = Path.Combine(assets.Directory, "application.wasm");
        var runtime = Path.Combine(assets.Directory, "runtime.wasm");
        var layout = Path.Combine(assets.Directory, "runtime-layout.json");
        var component = Path.Combine(assets.Directory, "managed.component.wasm");
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
        var wit = Path.Combine(assets.Directory, "wit");
        var toolchain = Path.Combine(assets.Root, "src", "NetWasm.Toolchain");
        // The diagnostic command world over the toolchain command package's dependencies, wasi:cli
        // among them, as the toolchain stages it.
        CopyWitDirectory(Path.Combine(toolchain, "wit", "deps"), Path.Combine(wit, "deps"));
        File.Copy(Path.Combine(toolchain, "diagnostic-wit", "world.wit"), Path.Combine(wit, "world.wit"));
        using var services = new ServiceCollection().AddNetWasmCompiler()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var world = services.GetRequiredService<IWitDocumentReader>().Read(wit)
            .SelectWorld("netwasm:component/diagnostic-command@1.0.0");
        var worldSpecifier = services.GetRequiredService<IWitWorldSpecifierFormatter>().Format(world);
        services.GetRequiredService<IComponentPackager>().Package(new(
            application, wit, worldSpecifier, component,
            ComponentTarget.Wasm32Wasi02, runtime, entryPoint,
            FinalWasmOptimization.None,
            StructuredDiagnostics: true));
        return component;
    }

    private static void CopyWitDirectory(string source, string destination, bool recursive = true)
    {
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFiles(source, "*.wit",
                     recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target);
        }
    }

    private static async Task<string> RunComponentTool(string executable, string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Component tool did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var result = await output;
        Assert.True(process.ExitCode == 0, result + await error);
        return result;
    }

    private static string CompileExecutable(
        TestAssets assets,
        bool optimize,
        string source = Source,
        string? bindings = null)
    {
        var syntaxTrees = bindings is null
            ? [CSharpSyntaxTree.ParseText(source)]
            : new[] { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(bindings) };
        var compilation = CSharpCompilation.Create(
            "ManagedExecutableAsyncMainFixture",
            syntaxTrees,
            assets.Framework.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(
                OutputKind.ConsoleApplication,
                optimizationLevel: optimize
                    ? OptimizationLevel.Release
                    : OptimizationLevel.Debug,
                allowUnsafe: true));
        var path = Path.Combine(
            assets.Directory,
            "ManagedExecutableAsyncMainFixture.dll");
        var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Where(
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static EntryPoint ReadEntryPoint(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var portableExecutable = new PEReader(stream);
        var header = portableExecutable.PEHeaders.CorHeader;
        Assert.NotNull(header);
        var handle = MetadataTokens.EntityHandle(
            header.EntryPointTokenOrRelativeVirtualAddress);
        Assert.Equal(HandleKind.MethodDefinition, handle.Kind);
        var metadata = portableExecutable.GetMetadataReader();
        var selectedMethod = metadata.GetMethodDefinition(
            (MethodDefinitionHandle)handle);
        var type = metadata.GetTypeDefinition(selectedMethod.GetDeclaringType());
        var methodHandle = type.GetMethods().Single(candidate =>
            metadata.GetString(metadata.GetMethodDefinition(candidate).Name) ==
            "Main");
        var method = metadata.GetMethodDefinition(methodHandle);
        var @namespace = metadata.GetString(type.Namespace);
        var name = metadata.GetString(type.Name);
        return new EntryPoint(
            string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}",
            metadata.GetString(method.Name),
            MetadataTokens.GetToken(methodHandle));
    }

    private sealed record EntryPoint(string TypeName, string MethodName, int Token);

    private const string Source = """
        using System.Threading.Tasks;

        namespace ManagedExecutableAsyncMainFixture;

        internal static class Program
        {
            public static async Task<int> Main()
            {
                await Task.Yield();
                return 0;
            }
        }
        """;
}
