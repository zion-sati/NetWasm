using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.EntryPoints;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class DiagnosticsDirectActorTests
{
    [Fact]
    public void EntryAndTraceDiagnosticsRejectMissingCapabilities()
    {
        var paths = new RecordingPaths(null);
        var json = new RecordingJson();
        Assert.Throws<ArgumentNullException>(() => new CompilerEntryDiagnosticArtifactWriter(null!, json));
        Assert.Throws<ArgumentNullException>(() => new CompilerEntryDiagnosticArtifactWriter(paths, null!));
        Assert.Throws<ArgumentNullException>(() => new CompilerProgramSnapshotBuilder(null!));
        Assert.Throws<ArgumentNullException>(() => new CompilerReproductionCommandBuilder(null!));

        var layouts = new CompilerDiagnosticLayoutTraceFormatter();
        var dispatch = new CompilerDiagnosticDispatchTraceFormatter();
        var methods = new CompilerDiagnosticMethodTraceFormatter(new CompilerCilOperandFormatter(),
            new CompilerStructuredMethodFormatter());
        Assert.Throws<ArgumentNullException>(() => new CompilerDiagnosticTraceFormatter(null!, dispatch, methods));
        Assert.Throws<ArgumentNullException>(() => new CompilerDiagnosticTraceFormatter(layouts, null!, methods));
        Assert.Throws<ArgumentNullException>(() => new CompilerDiagnosticTraceFormatter(layouts, dispatch, null!));
    }

    [Fact]
    public void EntrylessDiagnosticWritersRecordNullInsteadOfFormattingAFictitiousEntry()
    {
        var options = new CompilerOptions("worker.dll", [], "", "", [],
            EntryPointKind: CompilerEntryPointKind.Library);
        var json = new RecordingJson();
        var symbols = new CountingSymbols();
        var method = ValidationTestData.Method();
        var writer = new[] { new CompilerEntryDiagnosticArtifactWriter(new RecordingPaths("entry.json"), json) }
            .Cast<ICompilerEntryDiagnosticArtifactWriter>().Single();

        writer.WriteEntry(options, symbols, new(null, [new ProgramExport("value", method.Key)]));

        Assert.Equal(0, symbols.MethodsFormatted);
        using var entry = JsonDocument.Parse(json.LastJson!);
        Assert.Equal(JsonValueKind.Null, entry.RootElement.GetProperty("entryPoint").ValueKind);
        Assert.Equal("value", entry.RootElement.GetProperty("exports")[0].GetProperty("name").GetString());

        var snapshot = new[] { new CompilerProgramSnapshotBuilder(new CompilerCilOperandFormatter()) }
            .Cast<ICompilerProgramSnapshotBuilder>().Single();
        var program = ValidationTestData.Program() with { EntryPoint = null };
        using var result = JsonDocument.Parse(JsonSerializer.Serialize(snapshot.BuildProgram(symbols, program)));
        Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("EntryPoint").ValueKind);
    }

    [Theory]
    [InlineData(CompilerEntryPointKind.RawFunction, true, "function")]
    [InlineData(CompilerEntryPointKind.ManagedExecutable, false, "exe")]
    [InlineData(CompilerEntryPointKind.Library, false, "library")]
    public void ReproductionCommandPreservesTheCompilationMode(
        CompilerEntryPointKind kind, bool hasEntry, string target)
    {
        var builder = new[] { new CompilerReproductionCommandBuilder(new RecordingSourcePaths()) }
            .Cast<ICompilerReproductionCommandBuilder>().Single();
        var command = builder.Build(new CompilerOptions("worker.dll", [], "Worker", "Run", [],
            EntryPointKind: kind));

        Assert.Equal(hasEntry, command.Contains("'-entry:Worker::Run'", StringComparison.Ordinal));
        Assert.Contains($"'-target:{target}'", command);
    }

    [Fact]
    public void ReproductionCommandPreservesTheJavaScriptExportBoundary()
    {
        var builder = new CompilerReproductionCommandBuilder(new RecordingSourcePaths());
        var options = new CompilerOptions(
            "worker.dll",
            [],
            "",
            "",
            [],
            WitPath: "worker.wit",
            EntryPointKind: CompilerEntryPointKind.Library,
            UseJavaScriptExportBoundary: true);

        var command = builder.Build(options);

        Assert.Contains("'-wit:worker.wit'", command);
        Assert.Contains("'-jsexportboundary+'", command);
    }

    [Fact]
    public void ArtifactWritersSkipAllProductsWhenTraceIsNotRequested()
    {
        using var context = CompilerInvariantTestContext.Create();
        var options = CompilerTestSupport.Options(context.Assets, []);
        var paths = new RecordingPaths(null);
        var json = new RecordingJson();
        var source = new RecordingSourceCopier();
        var sourcePaths = new CompilerSourcePathProvider(new RecordingFiles(false));
        var reproduction = new CompilerReproductionCommandBuilder(sourcePaths);
        var provenance = new RecordingProvenance();
        var artifacts = new CompilerDiagnosticArtifactEnumerator();
        var symbols = MetadataCompilationTestActors.Symbols(context.Metadata.Snapshot);
        var bodies = MetadataCompilationTestActors.MethodBodies(context.Metadata.Snapshot);
        var entry = new CompilationEntry(
            context.Result.Program.EntryPoint!,
            context.Result.Program.Exports);

        new CompilerInitialDiagnosticArtifactWriter(
            paths, json, source, sourcePaths, reproduction, provenance,
            new RecordingFiles(false)).WriteInitialArtifacts(options);
        new CompilerMetadataDiagnosticArtifactWriter(
            paths, json, new RecordingMetadataSnapshot()).WriteMetadata(
                options, context.Metadata.Snapshot, bodies, symbols);
        new CompilerEntryDiagnosticArtifactWriter(paths, json).WriteEntry(
            options, symbols, entry);
        new CompilerProgramDiagnosticArtifactWriter(
            paths, json, new RecordingProgramSnapshot()).WriteProgram(
                options, symbols, context.Result.Program);
        new CompilerLayoutDiagnosticArtifactWriter(
            paths, json, new RecordingLayoutSnapshot()).WriteLayouts(
                options, context.Result.Layouts);
        new CompilerRootMapDiagnosticArtifactWriter(
            paths, json, new RecordingRootMapSnapshot()).WriteRootMaps(
                options, context.Result.Program);
        new CompilerComplexityDiagnosticArtifactWriter(
            paths, json).WriteComplexity(options, new CompilerComplexityReport(
                [], 0, 0, 0, 0, 0));
        new CompilerEmissionDiagnosticArtifactWriter(
            paths,
            json,
            new RecordingBinary(),
            new RecordingText(),
            new RecordingWasmText(),
            new RecordingFiles(false)).WriteEmission(options, [1]);
        new CompilerSuccessDiagnosticArtifactWriter(paths, json).WriteSuccess(options);
        new CompilerFailureDiagnosticArtifactWriter(
            paths, json, reproduction, artifacts).WriteFailure(
                options, "metadata", new InvalidOperationException("failure"));

        Assert.Equal(0, json.Writes);
        Assert.Equal(10, paths.Resolutions);
        Assert.Equal(0, source.Copies);
    }

    [Fact]
    public void BoundaryReadersAndSourceSelectionCoverMissingInputs()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var options = new CompilerOptions(
            Path.Combine(directory, "missing.dll"),
            [],
            "Entry",
            "Run",
            []);

        Assert.Empty(new CompilerDiagnosticArtifactEnumerator().Enumerate(directory));
        Assert.Throws<ArgumentException>(() =>
            new CompilerDiagnosticArtifactEnumerator().Enumerate(""));
        Assert.False(new CompilerDiagnosticFileExistenceReader().Exists(
            Path.Combine(directory, "missing.dll")));
        Assert.Throws<ArgumentException>(() =>
            new CompilerDiagnosticFileExistenceReader().Exists(""));
        Assert.Throws<ArgumentException>(() =>
            new CompilerDiagnosticFileByteReader().ReadBytes(""));
        Assert.Empty(new CompilerSourcePathProvider(
            new RecordingFiles(false)).Provide(options));
        var sourceProvider = new CompilerSourcePathProvider(new RecordingFiles(false));
        Assert.DoesNotContain("--wit", new CompilerReproductionCommandBuilder(
            sourceProvider).Build(options));
        Assert.Contains("wasm64", new CompilerReproductionCommandBuilder(
            sourceProvider).Build(options with { Target = WasmTarget.Wasm64 }));
        Assert.Throws<ArgumentNullException>(() => sourceProvider.Provide(null!));
        Assert.Throws<ArgumentNullException>(() => new CompilerReproductionCommandBuilder(
            sourceProvider).Build(null!));
    }

    [Fact]
    public void ArtifactWritersUseInjectedCapabilitiesWhenTraceIsRequested()
    {
        using var context = CompilerInvariantTestContext.Create();
        var options = CompilerTestSupport.Options(context.Assets, []) with
        {
            DiagnosticTracePath = Path.Combine(context.Assets.Directory, "diagnostic.trace"),
        };
        var passDirectory = options.DiagnosticTracePath + ".passes";
        Directory.CreateDirectory(passDirectory);
        File.WriteAllText(Path.Combine(passDirectory, "existing.json"), "{}");
        var paths = new RecordingPaths("artifact.json");
        var json = new RecordingJson();
        var source = new RecordingSourceCopier();
        var sourcePaths = new RecordingSourcePaths("source.cs", "missing.cs");
        var reproduction = new RecordingReproduction();
        var provenance = new RecordingProvenance();
        var files = new RecordingFiles(true);
        var symbols = MetadataCompilationTestActors.Symbols(context.Metadata.Snapshot);
        var bodies = MetadataCompilationTestActors.MethodBodies(context.Metadata.Snapshot);
        var entry = new CompilationEntry(
            context.Result.Program.EntryPoint!,
            [new ProgramExport("export", context.Result.Program.EntryPoint!.Key)]);
        var binary = new RecordingBinary();
        var text = new RecordingText();
        var wasmText = new RecordingWasmText();

        new CompilerInitialDiagnosticArtifactWriter(
            paths, json, source, sourcePaths, reproduction, provenance, new SelectiveFiles())
            .WriteInitialArtifacts(options);
        new CompilerMetadataDiagnosticArtifactWriter(
            paths, json, new RecordingMetadataSnapshot()).WriteMetadata(
                options, context.Metadata.Snapshot, bodies, symbols);
        new CompilerEntryDiagnosticArtifactWriter(paths, json).WriteEntry(
            options, symbols, entry);
        new CompilerProgramDiagnosticArtifactWriter(
            paths, json, new RecordingProgramSnapshot()).WriteProgram(
                options, symbols, context.Result.Program);
        new CompilerLayoutDiagnosticArtifactWriter(
            paths, json, new RecordingLayoutSnapshot()).WriteLayouts(
                options, context.Result.Layouts);
        new CompilerRootMapDiagnosticArtifactWriter(
            paths, json, new RecordingRootMapSnapshot()).WriteRootMaps(
                options, context.Result.Program);
        new CompilerComplexityDiagnosticArtifactWriter(
            paths, json).WriteComplexity(options, new CompilerComplexityReport(
                [], 0, 0, 0, 0, 0));
        new CompilerEmissionDiagnosticArtifactWriter(
            paths, json, binary, text, wasmText, files).WriteEmission(options, [1]);
        new CompilerEmissionDiagnosticArtifactWriter(
            paths,
            json,
            binary,
            text,
            wasmText,
            new RecordingFiles(false)).WriteEmission(options, [1]);
        new CompilerSuccessDiagnosticArtifactWriter(paths, json).WriteSuccess(options);
        new CompilerFailureDiagnosticArtifactWriter(
            paths, json, reproduction, new CompilerDiagnosticArtifactEnumerator())
            .WriteFailure(options, "metadata", new InvalidOperationException("failure"));
        new CompilerFailureDiagnosticArtifactWriter(
            paths, json, reproduction, new CompilerDiagnosticArtifactEnumerator())
            .WriteFailure(
                options,
                "analysis",
                new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.InvalidCil,
                    "bad")));

        Assert.True(json.Writes >= 12);
        Assert.Equal(1, source.Copies);
        Assert.Equal(2, binary.Writes);
        Assert.Equal(0, text.Writes);
        Assert.Equal(2, wasmText.Writes);
        Assert.True(reproduction.Calls >= 2);
        Assert.Equal(1, provenance.Calls);
        Assert.Contains("existing.json", new CompilerDiagnosticArtifactEnumerator()
            .Enumerate(passDirectory));

        var failedText = new RecordingText();
        new CompilerEmissionDiagnosticArtifactWriter(
            paths,
            json,
            binary,
            failedText,
            new ThrowingWasmText(),
            files).WriteEmission(options, [1]);
        Assert.Equal(1, failedText.Writes);
    }

    [Fact]
    public void ArtifactWriterMethodsRejectMissingInputs()
    {
        using var context = CompilerInvariantTestContext.Create();
        var options = CompilerTestSupport.Options(context.Assets, []);
        var snapshot = context.Metadata.Snapshot;
        var symbols = MetadataCompilationTestActors.Symbols(snapshot);
        var bodies = MetadataCompilationTestActors.MethodBodies(snapshot);
        var entry = new CompilationEntry(
            context.Result.Program.EntryPoint!,
            context.Result.Program.Exports);
        var paths = new RecordingPaths(null);
        var json = new RecordingJson();
        var reproduction = new RecordingReproduction();
        var report = new CompilerComplexityReport([], 0, 0, 0, 0, 0);

        Assert.Throws<ArgumentNullException>(() => new CompilerInitialDiagnosticArtifactWriter(
            paths, json, new RecordingSourceCopier(),
            new RecordingSourcePaths(), reproduction, new RecordingProvenance(),
            new RecordingFiles(false)).WriteInitialArtifacts(null!));

        var metadataWriter = new CompilerMetadataDiagnosticArtifactWriter(
            paths, json, new RecordingMetadataSnapshot());
        Assert.Throws<ArgumentNullException>(() => metadataWriter.WriteMetadata(
            null!, snapshot, bodies, symbols));
        Assert.Throws<ArgumentNullException>(() => metadataWriter.WriteMetadata(
            options, null!, bodies, symbols));
        Assert.Throws<ArgumentNullException>(() => metadataWriter.WriteMetadata(
            options, snapshot, null!, symbols));
        Assert.Throws<ArgumentNullException>(() => metadataWriter.WriteMetadata(
            options, snapshot, bodies, null!));

        var entryWriter = new CompilerEntryDiagnosticArtifactWriter(paths, json);
        Assert.Throws<ArgumentNullException>(() => entryWriter.WriteEntry(
            null!, symbols, entry));
        Assert.Throws<ArgumentNullException>(() => entryWriter.WriteEntry(
            options, null!, entry));
        Assert.Throws<ArgumentNullException>(() => entryWriter.WriteEntry(
            options, symbols, null!));

        var programWriter = new CompilerProgramDiagnosticArtifactWriter(
            paths, json, new RecordingProgramSnapshot());
        Assert.Throws<ArgumentNullException>(() => programWriter.WriteProgram(
            null!, symbols, context.Result.Program));
        Assert.Throws<ArgumentNullException>(() => programWriter.WriteProgram(
            options, null!, context.Result.Program));
        Assert.Throws<ArgumentNullException>(() => programWriter.WriteProgram(
            options, symbols, null!));

        var layoutWriter = new CompilerLayoutDiagnosticArtifactWriter(
            paths, json, new RecordingLayoutSnapshot());
        Assert.Throws<ArgumentNullException>(() => layoutWriter.WriteLayouts(
            null!, context.Result.Layouts));
        Assert.Throws<ArgumentNullException>(() => layoutWriter.WriteLayouts(
            options, null!));

        var rootWriter = new CompilerRootMapDiagnosticArtifactWriter(
            paths, json, new RecordingRootMapSnapshot());
        Assert.Throws<ArgumentNullException>(() => rootWriter.WriteRootMaps(
            null!, context.Result.Program));
        Assert.Throws<ArgumentNullException>(() => rootWriter.WriteRootMaps(
            options, null!));

        var complexityWriter = new CompilerComplexityDiagnosticArtifactWriter(paths, json);
        Assert.Throws<ArgumentNullException>(() => complexityWriter.WriteComplexity(
            null!, report));
        Assert.Throws<ArgumentNullException>(() => complexityWriter.WriteComplexity(
            options, null!));

        var emissionWriter = new CompilerEmissionDiagnosticArtifactWriter(
            paths, json, new RecordingBinary(), new RecordingText(),
            new RecordingWasmText(), new RecordingFiles(false));
        Assert.Throws<ArgumentNullException>(() => emissionWriter.WriteEmission(null!, [1]));

        Assert.Throws<ArgumentNullException>(() =>
            new CompilerSuccessDiagnosticArtifactWriter(paths, json).WriteSuccess(null!));

        var failureWriter = new CompilerFailureDiagnosticArtifactWriter(
            paths, json, reproduction, new CompilerDiagnosticArtifactEnumerator());
        Assert.Throws<ArgumentNullException>(() => failureWriter.WriteFailure(
            null!, "stage", new InvalidOperationException()));
        Assert.Throws<ArgumentException>(() => failureWriter.WriteFailure(
            options, "", new InvalidOperationException()));
        Assert.Throws<ArgumentNullException>(() => failureWriter.WriteFailure(
            options, "stage", null!));
    }

    private sealed class RecordingPaths(string? path) : ICompilerDiagnosticArtifactPathResolver
    {
        public int Resolutions { get; private set; }

        public string? ResolvePath(CompilerOptions options, string artifactName)
        {
            Resolutions++;
            return path;
        }
    }

    private sealed class RecordingJson : ICompilerDiagnosticJsonArtifactWriter
    {
        public int Writes { get; private set; }
        public string? LastJson { get; private set; }

        public void WriteJson<T>(string path, T value, JsonTypeInfo<T> typeInfo)
        {
            LastJson = JsonSerializer.Serialize(value, typeInfo);
            Writes++;
        }
    }

    private sealed class CountingSymbols : ISymbolFormatter
    {
        public int MethodsFormatted { get; private set; }
        public string Format(EntityKey key) => key.ToString();
        public string Format(MethodDefinitionModel method)
        {
            MethodsFormatted++;
            return method.Name;
        }
    }

    private sealed class RecordingFiles(bool exists) : ICompilerDiagnosticFileExistenceReader
    {
        public bool Exists(string path) => exists;
    }

    private sealed class SelectiveFiles : ICompilerDiagnosticFileExistenceReader
    {
        public bool Exists(string path) => !path.EndsWith("missing.cs", StringComparison.Ordinal);
    }

    private sealed class RecordingSourceCopier : ICompilerDiagnosticSourceArtifactCopier
    {
        public int Copies { get; private set; }

        public void CopySource(FileInfo source, string destination) => Copies++;
    }

    private sealed class RecordingSourcePaths(params string[] paths) : ICompilerSourcePathProvider
    {
        public ImmutableArray<string> Provide(CompilerOptions options) => [.. paths];
    }

    private sealed class RecordingReproduction : ICompilerReproductionCommandBuilder
    {
        public int Calls { get; private set; }

        public string Build(CompilerOptions options)
        {
            Calls++;
            return "reproduce";
        }
    }

    private sealed class RecordingProvenance : ICompilerDiagnosticProvenanceProvider
    {
        public int Calls { get; private set; }

        public CompilerProvenanceArtifact ProvideProvenance(CompilerOptions options)
        {
            Calls++;
            return new("compiler", "dotnet", "runtime", "os", "arch", null, []);
        }
    }

    private sealed class RecordingMetadataSnapshot : ICompilerMetadataSnapshotBuilder
    {
        public CompilerMetadataArtifact BuildMetadata(
            MetadataCompilationSnapshot metadata,
            IMethodBodyReader bodies,
            ISymbolFormatter symbols) => new("entry", [], []);
    }

    private sealed class RecordingProgramSnapshot : ICompilerProgramSnapshotBuilder
    {
        public CompilerProgramArtifact BuildProgram(ISymbolFormatter symbols, ReachableProgram program) =>
            new(null, [], [], [], [], [], [], [], [], [], [], [], []);
    }

    private sealed class RecordingLayoutSnapshot : ICompilerLayoutSnapshotBuilder
    {
        public CompilerLayoutsArtifact BuildLayouts(NetWasm.Compiler.Layout.ManagedLayoutSnapshot layouts) =>
            new(WasmTarget.Wasm32, 4, 4, 0, [], [], [], [], []);
    }

    private sealed class RecordingRootMapSnapshot : ICompilerRootMapSnapshotBuilder
    {
        public CompilerRootMapsArtifact BuildRootMaps(ReachableProgram program) => new([], []);
    }

    private sealed class RecordingBinary : ICompilerDiagnosticBinaryArtifactWriter
    {
        public int Writes { get; private set; }

        public string WriteBinary(string path, ReadOnlySpan<byte> value)
        {
            Writes++;
            return path;
        }
    }

    private sealed class RecordingText : ICompilerDiagnosticTextArtifactWriter
    {
        public int Writes { get; private set; }

        public void WriteText(string path, string value) => Writes++;
    }

    private sealed class RecordingWasmText : ICompilerWasmTextWriter
    {
        public int Writes { get; private set; }

        public void WriteText(string wasmPath, string watPath) => Writes++;
    }

    private sealed class ThrowingWasmText : ICompilerWasmTextWriter
    {
        public void WriteText(string wasmPath, string watPath) =>
            throw new InvalidOperationException("tool failure");
    }
}
