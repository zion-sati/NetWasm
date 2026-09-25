using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.StackTraces;
using NetWasm.Testing.CompilerHost;

namespace NetWasm.Testing.CompilerHost.Tests;

public sealed class CompilerHostApplicationTests
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    public void WritesSuccessfulResponseWithOptionalMetricsAndStackTrace(
        bool aliases,
        bool collectMetrics,
        bool emitStackTrace)
    {
        using var files = new HostTestFiles();
        var loader = new RecordingMetadataLoader();
        var request = Request(files.ModulePath) with
        {
            ReferenceAssemblyAliases = aliases
                ? ImmutableDictionary<string, string>.Empty.Add("alias", "reference.dll")
                : ImmutableDictionary<string, string>.Empty,
            CollectCompilerMetrics = collectMetrics,
            EmitStackTrace = emitStackTrace,
            StackTraceSymbolsPath = files.StackTracePath,
        };
        var application = CreateApplication(new SuccessfulCompiler(), loader);

        var exitCode = application.Run(request, files.ResponsePath);

        Assert.Equal(0, exitCode);
        var response = JsonSerializer.Deserialize<CompilationResponse>(
            File.ReadAllText(files.ResponsePath));
        Assert.NotNull(response);
        Assert.Equal(7, response.StaticDataEnd);
        Assert.Equal(64, response.ModuleSha256.Length);
        Assert.Equal("Fixture.Example", response.TypeNames[17]);
        Assert.Equal(collectMetrics, response.CompilerMetrics is not null);
        Assert.Equal(collectMetrics, response.CompilerTiming is not null);
        Assert.Equal(aliases, loader.ReceivedAliases is not null);
        Assert.Equal(emitStackTrace, File.Exists(files.StackTracePath));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void CapturedDiagnosticsRetainMetricsAndExitPolicy(bool capture, int expectedExitCode)
    {
        using var files = new HostTestFiles();
        var application = CreateApplication(
            new DiagnosticCompiler(),
            new RecordingMetadataLoader());
        var request = Request(files.ModulePath) with { CaptureDiagnostic = capture };

        var exitCode = application.Run(request, files.ResponsePath);

        Assert.Equal(expectedExitCode, exitCode);
        if (capture)
        {
            var response = JsonSerializer.Deserialize<CapturedDiagnosticResponse>(
                File.ReadAllText(files.ResponsePath));
            Assert.NotNull(response);
            Assert.Equal(CompilerMetricsOutcome.Failed, response.CompilerMetrics?.Outcome);
            Assert.NotNull(response.CompilerTiming);
        }
        else
        {
            var response = JsonSerializer.Deserialize<FailedCompilationResponse>(
                File.ReadAllText(files.ResponsePath));
            Assert.NotNull(response);
            Assert.Equal(CompilerMetricsOutcome.Failed, response.Outcome);
        }
    }

    [Theory]
    [InlineData(true, CompilerMetricsOutcome.Canceled)]
    [InlineData(false, CompilerMetricsOutcome.Failed)]
    public void WritesStructuredNonzeroFailureResponse(
        bool cancel,
        CompilerMetricsOutcome expectedOutcome)
    {
        using var files = new HostTestFiles();
        var application = CreateApplication(
            new FailingCompiler(cancel),
            new RecordingMetadataLoader());

        var exitCode = application.Run(Request(files.ModulePath), files.ResponsePath);

        Assert.Equal(1, exitCode);
        var response = JsonSerializer.Deserialize<FailedCompilationResponse>(
            File.ReadAllText(files.ResponsePath));
        Assert.NotNull(response);
        Assert.Equal(expectedOutcome, response.Outcome);
        Assert.True(response.AdapterDuration >= response.CompilerMetrics?.TotalDuration);
        Assert.Equal(response.AdapterDuration, response.CompilerTiming?.TotalDuration);
        Assert.Equal(
            response.AdapterDuration - response.CompilerMetrics?.TotalDuration,
            response.CompilerTiming?.ResidualDuration);
        Assert.Equal(expectedOutcome, response.CompilerMetrics?.Outcome);
    }

    [Fact]
    public void GuardsDependenciesAndRequestArguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CreateApplication(null!, new RecordingMetadataLoader()));
        Assert.Throws<ArgumentNullException>(() =>
            CreateApplication(new SuccessfulCompiler(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerHostApplication(new SuccessfulCompiler(), new RecordingMetadataLoader(), null!, new CompilerHostArtifactWriter()));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerHostApplication(new SuccessfulCompiler(), new RecordingMetadataLoader(), new CompilerHostArtifactFormatter(), null!));
        var application = CreateApplication(
            new SuccessfulCompiler(),
            new RecordingMetadataLoader());
        Assert.Throws<ArgumentNullException>(() => application.Run(null!, "response.json"));
        Assert.Throws<ArgumentException>(() => application.Run(Request("module.wasm"), ""));
    }

    [Fact]
    public void DelegatesTheExactRequestAndFormattedArtifacts()
    {
        using var files = new HostTestFiles();
        var formatter = new RecordingFormatter();
        var writer = new RecordingWriter();
        var application = new CompilerHostApplication(new SuccessfulCompiler(), new RecordingMetadataLoader(), formatter, writer);
        var request = Request(files.ModulePath) with { RuntimeLayoutPath = "layout.json", InteropManifestPath = "interop.json" };

        Assert.Equal(0, application.Run(request, files.ResponsePath));

        Assert.Same(request, formatter.Request);
        Assert.Equal(7, formatter.Result!.StaticDataEnd);
        Assert.Equal(formatter.Artifacts, writer.Artifacts);
        Assert.Equal(1, writer.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArtifactFailureProducesFailedResponseBeforeMetadataLoading(bool formattingFails)
    {
        using var files = new HostTestFiles();
        var formatter = new RecordingFormatter { Fail = formattingFails };
        var writer = new RecordingWriter { Fail = !formattingFails };
        var loader = new RecordingMetadataLoader();
        var application = new CompilerHostApplication(new SuccessfulCompiler(), loader, formatter, writer);

        Assert.Equal(1, application.Run(Request(files.ModulePath), files.ResponsePath));

        Assert.Equal(formattingFails ? 0 : 1, writer.Calls);
        Assert.Equal(0, loader.Calls);
        Assert.Equal(CompilerMetricsOutcome.Failed,
            JsonSerializer.Deserialize<FailedCompilationResponse>(File.ReadAllText(files.ResponsePath))!.Outcome);
    }

    private sealed class RecordingFormatter : ICompilerHostArtifactFormatter
    {
        public CompilationRequest? Request { get; private set; }
        public CompilationResult? Result { get; private set; }
        public bool Fail { get; init; }
        public ImmutableArray<CompilerHostArtifact> Artifacts { get; } = [new("artifact.bin", [1, 2])];

        public ImmutableArray<CompilerHostArtifact> Format(CompilationRequest request, CompilationResult result)
        {
            Request = request;
            Result = result;
            if (Fail)
                throw new InvalidOperationException("format failure");
            return Artifacts;
        }
    }

    private sealed class RecordingWriter : ICompilerHostArtifactWriter
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public ImmutableArray<CompilerHostArtifact> Artifacts { get; private set; }

        public void Write(ImmutableArray<CompilerHostArtifact> artifacts)
        {
            Calls++;
            if (Fail)
                throw new IOException("write failure");
            Artifacts = artifacts;
        }
    }

    private static CompilerHostApplication CreateApplication(
        INetWasmCompiler compiler,
        IMetadataCompilationLoader loader) =>
        new(compiler, loader, new CompilerHostArtifactFormatter(), new CompilerHostArtifactWriter());

    private static CompilationRequest Request(string modulePath) => new(
        "entry.dll", [], "Application", "Run", [], WasmTarget.Wasm32, null, [],
        ImmutableDictionary<string, string>.Empty, modulePath);

    private static CompilerMetricsReport Metrics(CompilerMetricsOutcome outcome) => new(
        outcome,
        outcome == CompilerMetricsOutcome.Succeeded ? null : "metadata",
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        []);

    private sealed class SuccessfulCompiler : INetWasmCompiler
    {
        public CompilationResult Compile(CompilerOptions options)
        {
            options.MetricsObserver?.Report(Metrics(CompilerMetricsOutcome.Succeeded));
            var layout = (ManagedLayoutSnapshot)RuntimeHelpers.GetUninitializedObject(
                typeof(ManagedLayoutSnapshot));
            var staticDataType = typeof(ManagedLayoutSnapshot).Assembly.GetType(
                "NetWasm.Compiler.Layout.ManagedStaticData", throwOnError: true)!;
            var staticData = RuntimeHelpers.GetUninitializedObject(staticDataType);
            staticDataType.GetField(
                "<TypeDescriptors>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(staticData, ImmutableArray.Create(
                    new TypeDescriptorLayout(new(new("Fixture"), 1), 17, 0, 16, 0, 0, null)));
            typeof(ManagedLayoutSnapshot).GetField(
                "<StaticData>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(layout, staticData);
            return new([0, 97, 115, 109], null!, layout, null!, 7)
            {
                StackTraceSymbols = new([1, 2, 3], "application/json", "symbols.json", "hash"),
            };
        }
    }

    private sealed class DiagnosticCompiler : INetWasmCompiler
    {
        public CompilationResult Compile(CompilerOptions options)
        {
            options.MetricsObserver?.Report(Metrics(CompilerMetricsOutcome.Failed));
            throw new CompilerException(new(
                DiagnosticCode.InvalidEntryPoint, "diagnostic", "Application.Run", 0));
        }
    }

    private sealed class FailingCompiler(bool cancel) : INetWasmCompiler
    {
        public CompilationResult Compile(CompilerOptions options)
        {
            var outcome = cancel ? CompilerMetricsOutcome.Canceled : CompilerMetricsOutcome.Failed;
            options.MetricsObserver?.Report(Metrics(outcome));
            if (cancel)
            {
                throw new OperationCanceledException();
            }
            throw new InvalidOperationException();
        }
    }

    private sealed class RecordingMetadataLoader : IMetadataCompilationLoader
    {
        public int Calls { get; private set; }
        public ImmutableDictionary<string, string>? ReceivedAliases { get; private set; }

        public IMetadataCompilationLease Load(
            string entryAssemblyPath,
            IEnumerable<string> referencePaths,
            ImmutableDictionary<string, string>? aliases = null)
        {
            Calls++;
            ReceivedAliases = aliases;
            return new FixtureMetadataLease();
        }
    }

    private sealed class FixtureMetadataLease : IMetadataCompilationLease
    {
        public MetadataCompilationSnapshot Snapshot { get; } = new(
            [], new("Fixture"),
            [new(new(new("Fixture"), 2), "Fixture", "Unrelated", false, [], []),
             new(new(new("Fixture"), 1), "Fixture", "Example", false, [], [])],
            [], []);
        public void Dispose()
        {
        }
    }

    private sealed class HostTestFiles : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), $"compiler-host-{Guid.NewGuid():N}");

        public HostTestFiles() => Directory.CreateDirectory(_root);
        public string ModulePath => Path.Combine(_root, "module.wasm");
        public string ResponsePath => Path.Combine(_root, "response.json");
        public string StackTracePath => Path.Combine(_root, "symbols.json");
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
