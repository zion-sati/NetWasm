using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler;
using NetWasm.Compiler.Cli;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class CliCommandTests
{
    [Fact]
    public void CompileCommandWritesRequestedArtifactsThroughCapabilities()
    {
        var files = new RecordingFiles();
        var compiler = new RecordingCompiler(Result());
        var command = new CompileCliCommand(
            compiler,
            files,
            files);

        var result = command.Run([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--target", "wasm64",
            "--runtime-layout", "layout.json",
            "--interop-manifest", "interop.json",
            "--diagnostic-trace", "trace-directory",
            "--diagnostic-log", "compiler.jsonl",
        ]);

        Assert.Equal(0, result);
        Assert.Equal(string.Empty, command.Name);
        Assert.Equal("application.dll", compiler.Options?.EntryAssemblyPath);
        Assert.Equal("trace-directory", compiler.Options!.DiagnosticTracePath);
        Assert.Equal("compiler.jsonl", compiler.Options!.DiagnosticLogPath);
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
        var command = new CompileCliCommand(
            compiler,
            files,
            files);

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

        var missingSymbolsCommand = new CompileCliCommand(
            new RecordingCompiler(Result()),
            files,
            files);
        Assert.Throws<InvalidOperationException>(() =>
            missingSymbolsCommand.Run([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--stack-trace-symbols", "symbols.json",
        ]));
    }

    [Fact]
    public void CompileCommandSkipsOptionalArtifactsWhenNotRequested()
    {
        var files = new RecordingFiles();
        var command = new CompileCliCommand(
            new RecordingCompiler(Result()),
            files,
            files);

        command.Run([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
        ]);

        Assert.Equal(string.Empty, command.Name);
        Assert.Empty(files.TextWrites);
    }

    [Fact]
    public void CompileCommandWritesWasm32RuntimeLayout()
    {
        var files = new RecordingFiles();
        var command = new CompileCliCommand(
            new RecordingCompiler(Result()),
            files,
            files);

        command.Run([
            "--input", "application.dll",
            "--output", "application.wasm",
            "--entry", "Example.Entry::Run",
            "--runtime-layout", "layout.json",
        ]);

        using var layout = JsonDocument.Parse(Assert.Single(files.TextWrites).Content);
        Assert.Equal(2, layout.RootElement.GetProperty("schemaVersion").GetInt32());
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
            null!, files, files));
        Assert.Throws<ArgumentNullException>(() => new CompileCliCommand(
            new RecordingCompiler(Result()), null!, files));
        Assert.Throws<ArgumentNullException>(() => new CompileCliCommand(
            new RecordingCompiler(Result()), files, null!));

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
        ITextFileWriter
    {
        public List<(string Path, byte[] Content)> BinaryWrites { get; } = [];
        public List<(string Path, string Content)> TextWrites { get; } = [];
        public void Write(string path, byte[] content) => BinaryWrites.Add((path, content));

        public void Write(string path, string content) => TextWrites.Add((path, content));
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
