using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler;
using NetWasm.Compiler.Cli;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.NativeInterop;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class CliCommandTests
{
    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void CompileCommandPublishesExactReachedNativeSignatures(string target)
    {
        var files = new RecordingFiles();
        var result = Result() with
        {
            RuntimeFeatures = [
                NetWasmRuntimeFeatureIds.LocalTime,
                NetWasmRuntimeFeatureIds.EphemeronHandles,
            ],
            NativeImports =
            [
                new("mule", "calculate", [WasmValueType.I32, WasmValueType.I64,
                    WasmValueType.F32, WasmValueType.F64], WasmValueType.F64),
                new("mule", "release", [], null),
            ],
        };
        var command = Assert.IsAssignableFrom<ICompilerCliCommand>(
            CreateCompileCommand(new RecordingCompiler(result), files));

        Assert.Equal(0, command.Run([
            "--input", "application.dll", "--output", "application.wasm",
            "--entry", "Example.Entry::Run", "--target", target,
            "--runtime-layout", "layout.json",
        ]));

        Assert.Equal(result.ApplicationModule, Assert.Single(files.BinaryWrites).Content);
        using var layout = JsonDocument.Parse(Assert.Single(files.TextWrites).Content);
        Assert.Equal(3, layout.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(target, layout.RootElement.GetProperty("target").GetString());
        Assert.Equal(result.StaticDataEnd, layout.RootElement.GetProperty("applicationStaticDataEnd").GetInt32());
        Assert.Equal(
            ["ephemeron-handles", "local-time"],
            layout.RootElement.GetProperty("runtimeFeatures")
                .EnumerateArray()
                .Select(feature => feature.GetString()!)
                .ToArray());
        var imports = layout.RootElement.GetProperty("nativeImports").EnumerateArray().ToArray();
        Assert.Equal(2, imports.Length);
        Assert.Equal("mule", imports[0].GetProperty("libraryName").GetString());
        Assert.Equal("calculate", imports[0].GetProperty("entryPoint").GetString());
        var parameters = imports[0].GetProperty("parameters").EnumerateArray().ToArray();
        Assert.Equal(4, parameters.Length);
        Assert.Equal("i32", parameters[0].GetString());
        Assert.Equal("i64", parameters[1].GetString());
        Assert.Equal("f32", parameters[2].GetString());
        Assert.Equal("f64", parameters[3].GetString());
        Assert.Equal("f64", imports[0].GetProperty("returnType").GetString());
        Assert.Equal("release", imports[1].GetProperty("entryPoint").GetString());
        Assert.Empty(imports[1].GetProperty("parameters").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, imports[1].GetProperty("returnType").ValueKind);
    }

    [Fact]
    public void CompileCommandRejectsUninitializedNativeFactsBeforeAnyWrite()
    {
        var files = new RecordingFiles();
        var command = Assert.IsAssignableFrom<ICompilerCliCommand>(
            CreateCompileCommand(
                new RecordingCompiler(Result() with { NativeImports = default }),
                files));

        var error = Assert.Throws<InvalidOperationException>(() => command.Run([
            "--input", "application.dll", "--output", "application.wasm",
            "--entry", "Example.Entry::Run", "--runtime-layout", "layout.json",
        ]));

        Assert.Contains("native import facts", error.Message);
        Assert.Empty(files.BinaryWrites);
        Assert.Empty(files.TextWrites);
    }

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void CompileCommandPublishesVersionedCallbackSupportBeforeTheApplication(
        string target)
    {
        var files = new RecordingFiles();
        var support = CallbackSupport(target == "wasm64"
            ? WasmValueType.I64
            : WasmValueType.I32);
        var command = CreateCompileCommand(
            new RecordingCompiler(Result() with { NativeCallbackSupport = support }),
            files);

        Assert.Equal(0, command.Run([
            "--input", "application.dll",
            "--output", "out/application.wasm",
            "--entry", "Example.Entry::Run",
            "--target", target,
            "--runtime-layout", "layout.json",
        ]));

        Assert.Equal("out/application.callbacks.o", files.BinaryWrites[0].Path);
        Assert.Equal(support.ObjectBytes, files.BinaryWrites[0].Content);
        Assert.Equal("out/application.wasm", files.BinaryWrites[^1].Path);
        Assert.Equal("binary:out/application.wasm", files.Operations[^1]);
        Assert.Empty(files.Deletes);
        using var layout = JsonDocument.Parse(Assert.Single(files.TextWrites).Content);
        Assert.Equal(4, layout.RootElement.GetProperty("schemaVersion").GetInt32());
        var callback = Assert.Single(layout.RootElement
            .GetProperty("nativeCallbackSupport")
            .GetProperty("callbacks")
            .EnumerateArray());
        Assert.Equal("application.callbacks.o", layout.RootElement
            .GetProperty("nativeCallbackSupport")
            .GetProperty("fileName")
            .GetString());
        Assert.Equal("runtime_callback",
            callback.GetProperty("runtimeImportSymbol").GetString());
        Assert.Equal("i32", callback.GetProperty("parameters")[0].GetString());
        Assert.Equal(target == "wasm64" ? "i64" : "i32",
            callback.GetProperty("parameters")[1].GetString());
        Assert.Equal("i32", callback.GetProperty("returnType").GetString());
    }

    [Fact]
    public void CompileCommandPublishesVoidCallbackAndRejectsCaseOnlySidecarAliases()
    {
        var files = new RecordingFiles();
        var support = CallbackSupport(WasmValueType.I32);
        var command = CreateCompileCommand(
            new RecordingCompiler(Result() with
            {
                NativeCallbackSupport = support with
                {
                    Callbacks = [support.Callbacks[0] with { ReturnType = null }],
                },
            }),
            files);

        Assert.Equal(0, command.Run([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--runtime-layout", "layout.json",
        ]));

        using var layout = JsonDocument.Parse(Assert.Single(files.TextWrites).Content);
        Assert.Equal(JsonValueKind.Null, Assert.Single(layout.RootElement
            .GetProperty("nativeCallbackSupport")
            .GetProperty("callbacks")
            .EnumerateArray())
            .GetProperty("returnType").ValueKind);

        var collision = CreateCompileCommand(
            new RecordingCompiler(Result()),
            new RecordingFiles());
        var error = Assert.Throws<CompilerException>(() => collision.Run([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--runtime-layout", "APPLICATION.WASM",
        ]));
        Assert.Equal(DiagnosticCode.InvalidCommandLine, error.Diagnostic.Code);
    }

    [Fact]
    public void CompileCommandRejectsIncompleteCallbackArtifactsBeforeAnyWrite()
    {
        var files = new RecordingFiles();
        var support = CallbackSupport(WasmValueType.I32);
        var command = CreateCompileCommand(
            new RecordingCompiler(Result() with { NativeCallbackSupport = support }),
            files);

        var missingLayout = Assert.Throws<CompilerException>(() => command.Run([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
        ]));

        Assert.Equal(DiagnosticCode.InvalidCommandLine, missingLayout.Diagnostic.Code);
        Assert.Empty(files.BinaryWrites);
        Assert.Empty(files.TextWrites);
        Assert.Empty(files.Deletes);

        var malformedCommand = CreateCompileCommand(
            new RecordingCompiler(Result() with
            {
                NativeCallbackSupport = support with
                {
                    Callbacks = [support.Callbacks[0] with
                    {
                        Parameters = [(WasmValueType)0],
                    }],
                },
            }),
            files);
        Assert.Throws<InvalidOperationException>(() => malformedCommand.Run([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--runtime-layout", "layout.json",
        ]));
        Assert.Empty(files.BinaryWrites);
        Assert.Empty(files.TextWrites);
        Assert.Empty(files.Deletes);
    }

    [Fact]
    public void LibraryCompileCommandPassesEntrylessModeToTheCompiler()
    {
        var files = new RecordingFiles();
        var compiler = new RecordingCompiler(Result());
        var command = Assert.IsAssignableFrom<ICompilerCliCommand>(
            CreateCompileCommand(compiler, files));

        var exitCode = command.Run([
            "--input", "worker.dll", "--output", "worker.wasm", "--entry-kind", "library"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(CompilerEntryPointKind.Library, compiler.Options!.EntryPointKind);
        Assert.Empty(compiler.Options.EntryTypeName);
        Assert.Empty(compiler.Options.EntryMethodName);
        Assert.Equal("worker.wasm", Assert.Single(files.BinaryWrites).Path);
    }

    [Fact]
    public void CompileCommandWritesRequestedArtifactsThroughCapabilities()
    {
        var files = new RecordingFiles();
        var compiler = new RecordingCompiler(Result());
        var command = CreateCompileCommand(compiler, files);

        var result = command.Run([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--target", "wasm64",
            "--runtime-layout", "layout.json",
            "--interop-manifest", "interop.json",
            "--diagnostic-trace", "trace-directory",
            "--diagnostic-log", "compiler.jsonl",
            "--javascript-export-boundary", "true",
        ]);

        Assert.Equal(0, result);
        Assert.Equal(string.Empty, command.Name);
        Assert.Equal("application.dll", compiler.Options?.EntryAssemblyPath);
        Assert.Equal("trace-directory", compiler.Options!.DiagnosticTracePath);
        Assert.Equal("compiler.jsonl", compiler.Options!.DiagnosticLogPath);
        Assert.True(compiler.Options.UseJavaScriptExportBoundary);
        Assert.Equal("application.wasm", Assert.Single(files.BinaryWrites).Path);
        Assert.Contains(files.TextWrites, write => write.Path == "layout.json");
        Assert.Contains(files.TextWrites, write => write.Path == "interop.json");
        var layout = files.TextWrites.Single(write => write.Path == "layout.json").Content;
        Assert.Contains("wasm64", layout);
        Assert.Contains('\n', layout);
        Assert.DoesNotContain('\r', layout);
        Assert.DoesNotContain('\r', files.TextWrites.Single(write =>
            write.Path == "interop.json").Content);
    }

    [Fact]
    public void CompileCommandEnablesAndWritesStackTraceSidecar()
    {
        var files = new RecordingFiles();
        var compiler = new RecordingCompiler(Result() with
        {
            StackTraceSymbols = new NetWasm.Compiler.StackTraces.StackTraceSymbolArtifact(
                [1, 2, 3],
                "application/vnd.netwasm.stack-trace-symbols+json;version=1",
                "application.netwasm.stacktrace.json",
                new string('0', 64)),
        });
        var command = CreateCompileCommand(compiler, files);

        var exitCode = command.Run([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--stack-trace-symbols", "symbols.json",
        ]);

        Assert.Equal(0, exitCode);
        Assert.True(compiler.Options?.EmitStackTrace);
        Assert.Equal([1, 2, 3], files.BinaryWrites.Single(write =>
            write.Path == "symbols.json").Content);

        var missingSymbolsCommand = CreateCompileCommand(
            new RecordingCompiler(Result()),
            files);
        var binaryWriteCount = files.BinaryWrites.Count;
        var textWriteCount = files.TextWrites.Count;
        var deleteCount = files.Deletes.Count;
        Assert.Throws<InvalidOperationException>(() =>
            missingSymbolsCommand.Run([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--stack-trace-symbols", "symbols.json",
        ]));
        Assert.Equal(binaryWriteCount, files.BinaryWrites.Count);
        Assert.Equal(textWriteCount, files.TextWrites.Count);
        Assert.Equal(deleteCount, files.Deletes.Count);
    }

    [Fact]
    public void CompileCommandSkipsOptionalArtifactsWhenNotRequested()
    {
        var files = new RecordingFiles();
        var command = CreateCompileCommand(new RecordingCompiler(Result()), files);

        command.Run([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
        ]);

        Assert.Equal(string.Empty, command.Name);
        Assert.Empty(files.TextWrites);
        Assert.Equal(["application.callbacks.o"], files.Deletes);
    }

    [Fact]
    public void CompileCommandWritesWasm32RuntimeLayout()
    {
        var files = new RecordingFiles();
        var command = CreateCompileCommand(new RecordingCompiler(Result()), files);

        command.Run([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--runtime-layout", "layout.json",
        ]);

        using var layout = JsonDocument.Parse(Assert.Single(files.TextWrites).Content);
        Assert.Equal(3, layout.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Empty(layout.RootElement.GetProperty("runtimeFeatures").EnumerateArray());
        Assert.Empty(layout.RootElement.GetProperty("nativeImports").EnumerateArray());
        Assert.Equal("wasm32", layout.RootElement.GetProperty("target").GetString());
        Assert.False(layout.RootElement.TryGetProperty("runtimeGlobalBase", out _));
    }

    [Fact]
    public void ComponentizeCommandUsesComponentizeOptions()
    {
        var document = Document();
        var files = new RecordingFiles();
        var manifest = new ComponentManifest(
            1,
            "example:test@1.0.0",
            "main",
            "wasm32",
            "0.2",
            "utf8",
            "hash",
            "wasm-tools 1",
            [],
            [],
            [],
            []);
        var validator = new RecordingValidator();
        var packager = new RecordingPackager();
        var command = new ComponentizeCliCommand(
            new RecordingDocuments(document),
            validator,
            new RecordingManifestBuilder(manifest),
            packager,
            new RecordingManifestInputs(),
            files);

        var result = command.Run([
            "--core-module", "application.wasm",
            "--wit", "contract.wit",
            "--output", "component.wasm",
            "--manifest", "component.json",
            "--optimization", "none",
        ]);

        Assert.Equal(0, result);
        Assert.Equal("componentize", command.Name);
        Assert.True(validator.Called);
        Assert.NotNull(packager.Request);
        Assert.Equal("wasm32", packager.Request!.Target.Width);
        Assert.Equal(FinalWasmOptimization.None, packager.Request.Optimization);
        Assert.Equal("component.json", Assert.Single(files.TextWrites).Path);
        Assert.DoesNotContain('\r', Assert.Single(files.TextWrites).Content);
    }

    [Fact]
    public void ComponentizeCommandUsesWasm64Target()
    {
        var document = Document();
        var files = new RecordingFiles();
        var command = new ComponentizeCliCommand(
            new RecordingDocuments(document),
            new RecordingValidator(),
            new RecordingManifestBuilder(new ComponentManifest(
                1, "example:test@1.0.0", "main", "wasm64", "0.2", "utf8",
                "hash", "wasm-tools 1", [], [], [], [])),
            new RecordingPackager(),
            new RecordingManifestInputs(),
            files);

        command.Run([
            "--core-module", "application.wasm",
            "--wit", "contract.wit",
            "--output", "component.wasm",
            "--manifest", "component.json",
            "--target", "wasm64",
        ]);

        Assert.Contains("wasm64", Assert.Single(files.TextWrites).Content);
    }

    [Fact]
    public void CommandConstructorsRejectMissingCapabilities()
    {
        var files = new RecordingFiles();
        Assert.Throws<ArgumentNullException>(() => new CompileCliCommand(
            null!, Planner(), files, files, files));
        Assert.Throws<ArgumentNullException>(() => new CompileCliCommand(
            new RecordingCompiler(Result()), null!, files, files, files));
        Assert.Throws<ArgumentNullException>(() => new CompileCliCommand(
            new RecordingCompiler(Result()), Planner(), null!, files, files));
        Assert.Throws<ArgumentNullException>(() => new CompileCliCommand(
            new RecordingCompiler(Result()), Planner(), files, null!, files));
        Assert.Throws<ArgumentNullException>(() => new CompileCliCommand(
            new RecordingCompiler(Result()), Planner(), files, files, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new CompileCliArtifactPlanner(null!));

        Assert.Throws<ArgumentNullException>(() => new ComponentizeCliCommand(
            null!, new RecordingValidator(), new RecordingManifestBuilder(null!),
            new RecordingPackager(), new RecordingManifestInputs(), files));
        Assert.Throws<ArgumentNullException>(() => new ComponentizeCliCommand(
            new RecordingDocuments(Document()), null!, new RecordingManifestBuilder(null!),
            new RecordingPackager(), new RecordingManifestInputs(), files));
        Assert.Throws<ArgumentNullException>(() => new ComponentizeCliCommand(
            new RecordingDocuments(Document()), new RecordingValidator(), null!,
            new RecordingPackager(), new RecordingManifestInputs(), files));
        Assert.Throws<ArgumentNullException>(() => new ComponentizeCliCommand(
            new RecordingDocuments(Document()), new RecordingValidator(),
            new RecordingManifestBuilder(null!), null!, new RecordingManifestInputs(), files));
        Assert.Throws<ArgumentNullException>(() => new ComponentizeCliCommand(
            new RecordingDocuments(Document()), new RecordingValidator(),
            new RecordingManifestBuilder(null!), new RecordingPackager(), null!, files));
        Assert.Throws<ArgumentNullException>(() => new ComponentizeCliCommand(
            new RecordingDocuments(Document()), new RecordingValidator(),
            new RecordingManifestBuilder(null!), new RecordingPackager(),
            new RecordingManifestInputs(), null!));
    }

    [Fact]
    public void FileCapabilitiesPreserveContentAndOverwriteDestination()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netwasm-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var binary = Path.Combine(directory, "module.wasm");
            var text = Path.Combine(directory, "manifest.json");
            new SystemTextFileWriter().Write(text, "source");
            new SystemBinaryFileWriter().Write(binary, [1, 2, 3]);

            Assert.Equal("source", new SystemTextFileReader().Read(text));
            Assert.Equal([1, 2, 3], File.ReadAllBytes(binary));
            new SystemFileDeleter().Delete(binary);
            Assert.False(File.Exists(binary));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CliRegistrationAddsAllCommandCapabilities()
    {
        var services = new ServiceCollection();
        services.AddNetWasmCompilerCli();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ICompilerCli));
        Assert.Equal(2, services.Count(descriptor =>
            descriptor.ServiceType == typeof(ICompilerCliCommand)));
    }

    private static CompilationResult Result() => new(
        [1, 2, 3],
        null!,
        null!,
        new HostInteropManifest(
            1,
            "wasm64",
            new HostInteropStatusAbi(0, -1, 0),
            new HostInteropTargetLayout(4, 4, 8, 4, 8),
            [],
            []),
        100);

    private static WasmNativeCallbackSupportArtifact CallbackSupport(
        WasmValueType pointerType)
    {
        var bytes = new byte[] { 0, 97, 115, 109, 1, 0, 0, 0 };
        return new(
            bytes,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            [new(
                "native_callback",
                "runtime_callback",
                "application_callback",
                "callback_address",
                [WasmValueType.I32, pointerType],
                WasmValueType.I32)],
            ["application_callback"],
            ["callback_address"]);
    }

    private static CompileCliCommand CreateCompileCommand(
        INetWasmCompiler compiler,
        RecordingFiles files) => new(
        compiler,
        Planner(),
        files,
        files,
        files);

    private static CompileCliArtifactPlanner Planner() => new(
        new NativeCallbackSupportArtifactValidator());

    private static WitDocument Document()
    {
        var world = new WitWorld(0, "main", "example:test@1.0.0", [], []);
        return new WitDocument([], [], [world], [], "{}");
    }

    private sealed class RecordingCompiler(CompilationResult result) : INetWasmCompiler
    {
        public CompilerOptions? Options { get; private set; }

        public CompilationResult Compile(CompilerOptions options)
        {
            Options = options;
            return result;
        }
    }

    private sealed class RecordingFiles :
        IBinaryFileWriter,
        ITextFileWriter,
        IFileDeleter
    {
        public List<(string Path, byte[] Content)> BinaryWrites { get; } = [];
        public List<(string Path, string Content)> TextWrites { get; } = [];
        public List<string> Deletes { get; } = [];
        public List<string> Operations { get; } = [];

        public void Write(string path, byte[] content)
        {
            BinaryWrites.Add((path, content));
            Operations.Add($"binary:{path}");
        }

        public void Write(string path, string content)
        {
            TextWrites.Add((path, content));
            Operations.Add($"text:{path}");
        }

        public void Delete(string path)
        {
            Deletes.Add(path);
            Operations.Add($"delete:{path}");
        }
    }

    private sealed class RecordingDocuments(WitDocument document) : IWitDocumentReader
    {
        public WitDocument Read(string path) => document;
    }

    private sealed class RecordingValidator : IWitWorldValidator
    {
        public bool Called { get; private set; }

        public void Validate(WitDocument document, WitWorld world) => Called = true;
    }

    private sealed class RecordingManifestBuilder(ComponentManifest manifest) :
        IComponentManifestBuilder
    {
        public ComponentManifest Build(
            WitDocument document,
            WitWorld world,
            ComponentTarget target,
            ComponentManifestInputs inputs) => manifest;
    }

    private sealed class RecordingPackager : IComponentPackager
    {
        public ComponentPackageRequest? Request { get; private set; }

        public void Package(ComponentPackageRequest request) => Request = request;
    }

    private sealed class RecordingManifestInputs : IComponentManifestInputReader
    {
        public ComponentManifestInputs Read(ComponentizeCliOptions options) =>
            new(ComponentJavaScriptBoundary.Empty, ComponentAdapterVersions.None);
    }
}
