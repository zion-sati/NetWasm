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
    // with .NET 11 RC3's output on Linux except that sockets cannot be made non-blocking, Unix domain
    // sockets are unavailable, and a dual-mode socket cannot bind the IPv6 wildcard (WASI has no
    // dual-stack socket; dual-mode connects and sends to IPv4 work).
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

                private static string Describe(Exception e) => e is SocketException s ? s.SocketErrorCode.ToString() : e.GetType().Name;
            }
            """);

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
            "non-blocking: unsupported\n" +
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
            [MetadataReference.CreateFromFile(assets.CoreLib)],
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
