using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LinkedCorpusOracleRunnerTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, 0)]
    [InlineData(WasmTarget.Wasm64, 0)]
    [InlineData(WasmTarget.Wasm32, 1)]
    [InlineData(WasmTarget.Wasm64, 1)]
    public void CompileBuildAndRunPreservesOneExplicitLinkedCell(WasmTarget target, int formValue)
    {
        var dependencies = new Dependencies();
        var form = (CorpusWasmForm)formValue;
        var compilation = CreateCompilation() with
        {
            Fixture = CreateCompilation().Fixture with
            {
                CaptureCompilerDiagnostics = target == WasmTarget.Wasm32,
                EmitStackTrace = form == CorpusWasmForm.Optimized,
                ProcessTimeout = form == CorpusWasmForm.Direct
                    ? TimeSpan.FromSeconds(9)
                    : null,
                RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            },
        };
        var cell = new CorpusMatrixCell(compilation.Profile, target, form);
        using var cancellation = new CancellationTokenSource();

        var result = CreateRunner(dependencies).CompileBuildAndRun(
            compilation, cell, cancellation.Token);

        Assert.Equal(target, result.Target);
        Assert.Equal(form, result.Form);
        Assert.Same(dependencies.Observed.Observations, result.Observations);
        var targetName = target == WasmTarget.Wasm64 ? "wasm64" : "wasm32";
        var formName = form == CorpusWasmForm.Optimized ? "optimized" : "direct";
        Assert.Contains($".{targetName}.{formName}.linked.application.wasm",
            result.ApplicationModulePath, StringComparison.Ordinal);
        Assert.Equal(dependencies.ApplicationHash, result.ApplicationModuleSha256);
        Assert.Equal(dependencies.LayoutHash, result.RuntimeLayoutSha256);
        Assert.Equal(dependencies.ManifestHash, result.InteropManifestSha256);
        Assert.Equal(dependencies.ModuleHash, result.ModuleSha256);
        Assert.Equal(dependencies.BuildResults, result.BuildSteps);
        Assert.Equal(cancellation.Token, dependencies.CompilerToken);
        Assert.Equal(cancellation.Token, dependencies.BuildToken);
        Assert.Equal(cancellation.Token, dependencies.ObservationToken);
        Assert.Equal(OracleRuntimeCapabilities.GarbageCollection, dependencies.RequiredCapabilities);
        Assert.Equal(OracleRuntimeCapabilities.GarbageCollection |
            OracleRuntimeCapabilities.Finalization |
            OracleRuntimeCapabilities.WeakReferenceClearing,
            dependencies.ProvidedCapabilities);
        Assert.Same(dependencies.Tools, dependencies.BuildRequest!.Tools);
        Assert.Equal(target, dependencies.BuildRequest.Target);
        Assert.Equal(form, dependencies.BuildRequest.Form);
        Assert.Equal(compilation.Fixture.ProcessTimeout ?? Environment.ProcessTimeout,
            dependencies.BuildRequest.Timeout);
        Assert.Equal(dependencies.Tools.Node, dependencies.ObservationEnvironment!.NodePath);
        Assert.Equal(dependencies.BuildRequest.Timeout, dependencies.ObservationEnvironment.Timeout);
        Assert.EndsWith("linked-corpus-runner.mjs", dependencies.ObservationEnvironment.RunnerPath,
            StringComparison.Ordinal);
        Assert.Equal(targetName, dependencies.ObservationRequest!.Target);
        Assert.Equal(result.ModulePath, dependencies.ObservationRequest.ModulePath);
        Assert.Equal(result.InteropManifestPath, dependencies.ObservationRequest.ManifestPath);
        Assert.Equal(dependencies.ModuleHash, dependencies.ObservationRequest.ModuleSha256);
        Assert.Equal(dependencies.ManifestHash, dependencies.ObservationRequest.ManifestSha256);
        Assert.Equal(target == WasmTarget.Wasm32, dependencies.CompilerRequest!.DiagnosticTracePath is not null);
        Assert.Equal(form == CorpusWasmForm.Optimized, dependencies.CompilerRequest.StackTraceSymbolsPath is not null);
        Assert.Equal(result.RuntimeLayoutPath, dependencies.CompilerRequest.RuntimeLayoutPath);
        Assert.Equal(result.InteropManifestPath, dependencies.CompilerRequest.InteropManifestPath);
        Assert.Equal([
            "capabilities", "tools", "exports", "compiler-request", "compile",
            "fingerprint-application", "build-plan", "build",
            "fingerprint-layout", "fingerprint-manifest", "fingerprint-module",
            "observation-request", "observe",
        ], dependencies.Calls);
    }

    [Theory]
    [InlineData("capabilities", 1)]
    [InlineData("tools", 2)]
    [InlineData("exports", 3)]
    [InlineData("compiler-request", 4)]
    [InlineData("compile", 5)]
    [InlineData("fingerprint-application", 6)]
    [InlineData("build-plan", 7)]
    [InlineData("build", 8)]
    [InlineData("fingerprint-layout", 9)]
    [InlineData("fingerprint-manifest", 10)]
    [InlineData("fingerprint-module", 11)]
    [InlineData("observation-request", 12)]
    [InlineData("observe", 13)]
    public void CompileBuildAndRunPreservesFirstCollaboratorFailure(
        string stage, int callCount)
    {
        var dependencies = new Dependencies { FailureStage = stage };
        var compilation = CreateCompilation();
        var failure = Assert.Throws<InvalidOperationException>(() =>
            CreateRunner(dependencies).CompileBuildAndRun(compilation,
                new(compilation.Profile, WasmTarget.Wasm32, CorpusWasmForm.Direct)));
        Assert.Same(dependencies.Failure, failure);
        Assert.Equal(callCount, dependencies.Calls.Count);
        Assert.Equal(stage, dependencies.Calls[^1]);
        Assert.True(dependencies.Calls.Count(call => call == stage) == 1);
    }

    [Fact]
    public void CompileBuildAndRunRejectsInvalidInputsBeforeWork()
    {
        var dependencies = new Dependencies();
        var runner = CreateRunner(dependencies);
        var compilation = CreateCompilation();
        Assert.Throws<ArgumentNullException>(() => runner.CompileBuildAndRun(null!,
            new(compilation.Profile, WasmTarget.Wasm32, CorpusWasmForm.Direct)));
        Assert.Throws<ArgumentNullException>(() => runner.CompileBuildAndRun(compilation, null!));
        Assert.Throws<ArgumentException>(() => runner.CompileBuildAndRun(compilation,
            new(CilProfile.Debug, WasmTarget.Wasm32, CorpusWasmForm.Direct)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = Assert.Throws<OperationCanceledException>(() =>
            runner.CompileBuildAndRun(compilation,
                new(compilation.Profile, WasmTarget.Wasm32, CorpusWasmForm.Direct),
                cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Empty(dependencies.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompileBuildAndRunRejectsUnsupportedTargetOrForm(bool target)
    {
        var dependencies = new Dependencies();
        var compilation = CreateCompilation();
        var cell = new CorpusMatrixCell(compilation.Profile,
            target ? (WasmTarget)99 : WasmTarget.Wasm32,
            target ? CorpusWasmForm.Direct : (CorpusWasmForm)99);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateRunner(dependencies).CompileBuildAndRun(compilation, cell));
        Assert.Equal(["capabilities", "tools"], dependencies.Calls);
    }

    [Fact]
    public void CompileBuildAndRunStopsWhenCompilerArtifactIdentityDiffers()
    {
        var dependencies = new Dependencies { CompilerHash = new string('e', 64) };
        var compilation = CreateCompilation();
        Assert.Throws<InvalidOperationException>(() =>
            CreateRunner(dependencies).CompileBuildAndRun(compilation,
                new(compilation.Profile, WasmTarget.Wasm32, CorpusWasmForm.Direct)));
        Assert.Equal("fingerprint-application", dependencies.Calls[^1]);
        Assert.DoesNotContain("build-plan", dependencies.Calls);
    }

    private static ILinkedCorpusOracleRunner CreateRunner(Dependencies dependencies) =>
        Assert.IsAssignableFrom<ILinkedCorpusOracleRunner>(new LinkedCorpusOracleRunner(
            Environment, dependencies, dependencies, dependencies, dependencies,
            dependencies, dependencies, dependencies, dependencies, dependencies,
            dependencies));

    private static CompilerCorrectnessEnvironment Environment { get; } = new(
        Path.GetFullPath("repository"), "dotnet", "sdk", "roslyn", "corelib",
        "references", "oracle.mjs", "oracle.dll", "compiler.dll",
        TimeSpan.FromSeconds(3));

    private static CorpusCompilation CreateCompilation()
    {
        var artifact = new CorpusArtifact("same.dll", "same.pdb", "hash", "pdb-hash", "compiler", []);
        return new(new("Fixture", "Tests", "", [-1, 0, 1]), CilProfile.Release,
            artifact, artifact, Path.GetFullPath("run"));
    }

    private sealed class Dependencies : ILinkedCorpusToolPathsProvider,
        IOracleRuntimeCapabilityVerifier, ICorpusExportFactory,
        ICorpusCompilerRequestFactory, ICorpusApplicationCompiler,
        ILinkedCorpusBuildPlanFactory, ILinkedCorpusBuildExecutor,
        ICorpusArtifactFingerprint, ILinkedCorpusObservationRequestFactory,
        ILinkedCorpusObservationProcess
    {
        public List<string> Calls { get; } = [];
        public string? FailureStage { get; init; }
        public InvalidOperationException Failure { get; } = new("sentinel");
        public string ApplicationHash { get; } = new('a', 64);
        public string LayoutHash { get; } = new('c', 64);
        public string ManifestHash { get; } = new('b', 64);
        public string ModuleHash { get; } = new('d', 64);
        public string CompilerHash { get; init; } = new('a', 64);
        public LinkedCorpusToolPaths Tools { get; } = new(
            Path.GetFullPath("emsdk"), Path.GetFullPath("wasm-merge"),
            Path.GetFullPath("wasm-opt"), Path.GetFullPath("wasm-tools"),
            Path.GetFullPath("node"));
        public ImmutableArray<LinkedCorpusBuildStepResult> BuildResults { get; } = [];
        public LinkedCorpusObservationResult Observed { get; } = new(
            ImmutableDictionary<int, OracleObservation>.Empty.Add(
                -1, new(OracleObservationKind.Value, 42, null, 0)),
            new string('d', 64), new string('b', 64));
        public OracleRuntimeCapabilities RequiredCapabilities { get; private set; }
        public OracleRuntimeCapabilities ProvidedCapabilities { get; private set; }
        public CorpusCompilerRequest? CompilerRequest { get; private set; }
        public CancellationToken CompilerToken { get; private set; }
        public LinkedCorpusBuildRequest? BuildRequest { get; private set; }
        public CancellationToken BuildToken { get; private set; }
        public LinkedCorpusObservationRequest? ObservationRequest { get; private set; }
        public LinkedCorpusObservationEnvironment? ObservationEnvironment { get; private set; }
        public CancellationToken ObservationToken { get; private set; }

        public LinkedCorpusToolPaths Get()
        {
            Record("tools");
            return Tools;
        }

        public void Verify(OracleRuntimeCapabilities required, OracleRuntimeCapabilities provided)
        {
            Record("capabilities");
            RequiredCapabilities = required;
            ProvidedCapabilities = provided;
        }

        public ImmutableArray<RequestedExport> Create(CorpusFixture fixture)
        {
            Record("exports");
            return [new("trace", fixture.EntryType, "Trace")];
        }

        public CorpusCompilerRequest Create(
            CorpusCompilation compilation, WasmTarget target, string? tracePath,
            string modulePath, string? stackTraceSymbolsPath,
            ImmutableArray<RequestedExport> exports,
            string? runtimeLayoutPath = null, string? interopManifestPath = null)
        {
            Record("compiler-request");
            CompilerRequest = new(compilation.NetWasm.AssemblyPath, [],
                compilation.Fixture.EntryType, compilation.Fixture.WasmEntryMethod,
                exports, target, tracePath, [], ImmutableDictionary<string, string>.Empty,
                null, null, modulePath, compilation.Fixture.EmitStackTrace,
                stackTraceSymbolsPath, runtimeLayoutPath, interopManifestPath);
            return CompilerRequest;
        }

        public CorpusCompilerResponse Compile(
            CorpusCompilation compilation,
            CorpusCompilerRequest compilerRequest,
            CancellationToken cancellationToken = default)
        {
            Record("compile");
            CompilerToken = cancellationToken;
            return new(ImmutableDictionary<int, string>.Empty.Add(17, "System.Exception"),
                CompilerHash, 0);
        }

        public LinkedCorpusBuildPlan Create(LinkedCorpusBuildRequest request)
        {
            Record("build-plan");
            BuildRequest = request;
            return new(Path.Combine(request.RunDirectory, "runtime.wasm"),
                Path.Combine(request.RunDirectory, "merged.wasm"),
                Path.Combine(request.RunDirectory, "final.wasm"),
                [new(LinkedCorpusBuildStage.NativeRuntime,
                    new("tool", [], request.Timeout))]);
        }

        public ImmutableArray<LinkedCorpusBuildStepResult> Execute(
            LinkedCorpusBuildPlan plan, CancellationToken cancellationToken = default)
        {
            Record("build");
            BuildToken = cancellationToken;
            return BuildResults;
        }

        public string Compute(string path)
        {
            string stage;
            string result;
            if (path.EndsWith(".application.wasm", StringComparison.Ordinal))
            {
                stage = "fingerprint-application";
                result = ApplicationHash;
            }
            else if (path.EndsWith(".runtime-layout.json", StringComparison.Ordinal))
            {
                stage = "fingerprint-layout";
                result = LayoutHash;
            }
            else if (path.EndsWith(".interop-manifest.json", StringComparison.Ordinal))
            {
                stage = "fingerprint-manifest";
                result = ManifestHash;
            }
            else
            {
                stage = "fingerprint-module";
                result = ModuleHash;
            }
            Record(stage);
            return result;
        }

        public LinkedCorpusObservationRequest Create(
            CorpusFixture fixture, WasmTarget target, string modulePath,
            string manifestPath, string moduleSha256, string manifestSha256)
        {
            Record("observation-request");
            ObservationRequest = new(1,
                target == WasmTarget.Wasm64 ? "wasm64" : "wasm32",
                modulePath, manifestPath, moduleSha256, manifestSha256,
                fixture.Inputs, fixture.ExposesLegacyTrace, fixture.UsesTypedTrace);
            return ObservationRequest;
        }

        public LinkedCorpusObservationResult Observe(
            LinkedCorpusObservationRequest request,
            ImmutableDictionary<int, string> typeNames,
            LinkedCorpusObservationEnvironment environment,
            CancellationToken cancellationToken = default)
        {
            Record("observe");
            ObservationRequest = request;
            ObservationEnvironment = environment;
            ObservationToken = cancellationToken;
            Assert.Equal("System.Exception", typeNames[17]);
            return Observed;
        }

        private void Record(string stage)
        {
            Calls.Add(stage);
            if (stage == FailureStage) throw Failure;
        }
    }
}
