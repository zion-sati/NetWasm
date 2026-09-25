using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record LinkedCorpusExecution(
    ImmutableDictionary<int, OracleObservation> Observations,
    WasmTarget Target,
    CorpusWasmForm Form,
    string ApplicationModulePath,
    string ApplicationModuleSha256,
    string RuntimeLayoutPath,
    string RuntimeLayoutSha256,
    string InteropManifestPath,
    string InteropManifestSha256,
    string ModulePath,
    string ModuleSha256,
    ImmutableArray<LinkedCorpusBuildStepResult> BuildSteps);

internal interface ILinkedCorpusOracleRunner
{
    LinkedCorpusExecution CompileBuildAndRun(
        CorpusCompilation compilation,
        CorpusMatrixCell cell,
        CancellationToken cancellationToken = default);
}

internal sealed class LinkedCorpusOracleRunner(
    CompilerCorrectnessEnvironment environment,
    ILinkedCorpusToolPathsProvider tools,
    IOracleRuntimeCapabilityVerifier capabilities,
    ICorpusExportFactory exports,
    ICorpusCompilerRequestFactory compilerRequests,
    ICorpusApplicationCompiler compiler,
    ILinkedCorpusBuildPlanFactory buildPlans,
    ILinkedCorpusBuildExecutor builds,
    ICorpusArtifactFingerprint fingerprints,
    ILinkedCorpusObservationRequestFactory observationRequests,
    ILinkedCorpusObservationProcess observations) : ILinkedCorpusOracleRunner
{
    private const OracleRuntimeCapabilities LinkedCapabilities =
        OracleRuntimeCapabilities.GarbageCollection |
        OracleRuntimeCapabilities.Finalization |
        OracleRuntimeCapabilities.WeakReferenceClearing;

    public LinkedCorpusExecution CompileBuildAndRun(
        CorpusCompilation compilation,
        CorpusMatrixCell cell,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(cell);
        if (cell.Profile != compilation.Profile)
        {
            throw new ArgumentException(
                "linked cell profile differs from its compiled corpus", nameof(cell));
        }
        cancellationToken.ThrowIfCancellationRequested();
        capabilities.Verify(
            compilation.Fixture.RequiredRuntimeCapabilities, LinkedCapabilities);
        var selectedTools = tools.Get();
        var targetName = cell.Target switch
        {
            WasmTarget.Wasm32 => "wasm32",
            WasmTarget.Wasm64 => "wasm64",
            _ => throw new ArgumentOutOfRangeException(nameof(cell)),
        };
        var formName = cell.Form switch
        {
            CorpusWasmForm.Direct => "direct",
            CorpusWasmForm.Optimized => "optimized",
            _ => throw new ArgumentOutOfRangeException(nameof(cell)),
        };
        var prefix = Path.Combine(compilation.Directory,
            $"{compilation.Fixture.Name}.{compilation.Profile}.{targetName}.{formName}.linked");
        var applicationPath = prefix + ".application.wasm";
        var runtimeLayoutPath = prefix + ".runtime-layout.json";
        var manifestPath = prefix + ".interop-manifest.json";
        var tracePath = compilation.Fixture.CaptureCompilerDiagnostics
            ? prefix + ".trace.txt"
            : null;
        var stackTracePath = compilation.Fixture.EmitStackTrace
            ? prefix + ".stacktrace.json"
            : null;
        var compilerRequest = compilerRequests.Create(
            compilation, cell.Target, tracePath, applicationPath, stackTracePath,
            exports.Create(compilation.Fixture), runtimeLayoutPath, manifestPath);
        var compilerResponse = compiler.Compile(
            compilation, compilerRequest, cancellationToken);
        var applicationHash = fingerprints.Compute(applicationPath);
        if (!StringComparer.Ordinal.Equals(
                applicationHash, compilerResponse.ModuleSha256))
        {
            throw new InvalidOperationException(
                "linked application identity differs from the compiler response");
        }
        var timeout = compilation.Fixture.ProcessTimeout ?? environment.ProcessTimeout;
        var buildPlan = buildPlans.Create(new(
            environment.RepositoryRoot, compilation.Directory, applicationPath,
            runtimeLayoutPath, cell.Target, cell.Form, selectedTools, timeout));
        var buildSteps = builds.Execute(buildPlan, cancellationToken);
        var runtimeLayoutHash = fingerprints.Compute(runtimeLayoutPath);
        var manifestHash = fingerprints.Compute(manifestPath);
        var moduleHash = fingerprints.Compute(buildPlan.OutputModulePath);
        var observationRequest = observationRequests.Create(
            compilation.Fixture, cell.Target, buildPlan.OutputModulePath,
            manifestPath, moduleHash, manifestHash);
        var observed = observations.Observe(
            observationRequest,
            compilerResponse.TypeNames,
            new(selectedTools.Node,
                Path.Combine(environment.RepositoryRoot, "tests",
                    "NetWasm.Compiler.Tests", "Correctness",
                    "linked-corpus-runner.mjs"),
                timeout),
            cancellationToken);
        return new(
            observed.Observations, cell.Target, cell.Form,
            applicationPath, applicationHash,
            runtimeLayoutPath, runtimeLayoutHash,
            manifestPath, manifestHash,
            buildPlan.OutputModulePath, moduleHash,
            buildSteps);
    }
}
