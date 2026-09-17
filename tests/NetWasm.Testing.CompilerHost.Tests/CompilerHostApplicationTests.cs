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
        var application = new CompilerHostApplication(new SuccessfulCompiler(), loader);

        var exitCode = application.Run(request, files.ResponsePath);

        Assert.Equal(0, exitCode);
        var response = JsonSerializer.Deserialize<CompilationResponse>(
            File.ReadAllText(files.ResponsePath));
        Assert.NotNull(response);
        Assert.Equal(7, response.StaticDataEnd);
        Assert.Equal(64, response.ModuleSha256.Length);
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
        var application = new CompilerHostApplication(
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
        var application = new CompilerHostApplication(
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
            new CompilerHostApplication(null!, new RecordingMetadataLoader()));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerHostApplication(new SuccessfulCompiler(), null!));
        var application = new CompilerHostApplication(
            new SuccessfulCompiler(),
            new RecordingMetadataLoader());
        Assert.Throws<ArgumentNullException>(() => application.Run(null!, "response.json"));
        Assert.Throws<ArgumentException>(() => application.Run(Request("module.wasm"), ""));
    }

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
                .SetValue(staticData, ImmutableArray<TypeDescriptorLayout>.Empty);
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
        public ImmutableDictionary<string, string>? ReceivedAliases { get; private set; }

        public IMetadataCompilationLease Load(
            string entryAssemblyPath,
            IEnumerable<string> referencePaths,
            ImmutableDictionary<string, string>? aliases = null)
        {
            ReceivedAliases = aliases;
            return new EmptyMetadataLease();
        }
    }

    private sealed class EmptyMetadataLease : IMetadataCompilationLease
    {
        public MetadataCompilationSnapshot Snapshot => throw new InvalidOperationException();
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
