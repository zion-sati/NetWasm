using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal enum CilProfile
{
    Debug,
    Release,
    Emitted,
}

internal static class CilProfiles
{
    public static ImmutableArray<CilProfile> Roslyn { get; } = [CilProfile.Debug, CilProfile.Release];
}

internal enum OracleMode
{
    SameIl,
    SameSource,
    FrozenDesktop,
}

internal enum OracleObservationKind
{
    Value,
    ManagedException,
    Trap,
    TimedOut,
    CompileRejected,
}

internal enum TraceRecordKind
{
    Mark,
    Boolean,
    Int32,
    Int64,
    Float32Bits,
    Float64Bits,
    ManagedException,
    StateChecksum,
}

internal readonly record struct TraceRecord(
    TraceRecordKind Kind,
    int EventId,
    long Payload);

internal sealed record CorpusFixture(
    string Name,
    string Namespace,
    string Source,
    ImmutableArray<int> Inputs)
{
    public ImmutableArray<CorpusSourceFile> AdditionalSources { get; init; } = [];

    public string? CaseId { get; init; }

    public ImmutableArray<string> FeatureIds { get; init; } = [];

    // Optional real test identity, never inferred from the fixture's assembly name.
    // This replays the owning method, not necessarily one theory row or matrix cell.
    public string? ReplayTestMethod { get; init; }

    // Used only by owning theories whose first argument is named "input".
    public int? ReplayInput { get; init; }

    // A named matrix owns profile/target/form selection. Null retains legacy flags.
    public CorpusMatrixSelection? Matrix { get; init; }

    public bool AllowUnsafe { get; init; }

    // Real runtime semantics required by the assertion, not merely API calls
    // appearing in its CIL. A simulated import cannot satisfy these requirements.
    public OracleRuntimeCapabilities RequiredRuntimeCapabilities { get; init; }

    // Null preserves differential-only fixtures; a value declares an independent
    // success contract for every input, checked before NetWasm execution.
    public int? ExpectedReturnValue { get; init; }

    // Mixed fixtures can pin only inputs with independent contracts, leaving
    // numeric observations differential. Do not combine with ExpectedReturnValue.
    public ImmutableDictionary<int, int> ExpectedReturnValues { get; init; } =
        ImmutableDictionary<int, int>.Empty;

    public ImmutableDictionary<int, string> ExpectedExceptionTypes { get; init; } =
        ImmutableDictionary<int, string>.Empty;

    public bool UsesTypedTrace { get; init; }

    public bool ExposesLegacyTrace { get; init; } = true;

    public bool SupportsBatchedOracle { get; init; }

    // Compare all available semantic observations before reporting mismatches.
    // Infrastructure failures still stop immediately; false preserves fail-fast.
    public bool ReportAllMismatches { get; init; }

    public bool ExecuteWasm64 { get; init; } = true;

    public bool ExecuteOptimizedWasm { get; init; }

    public bool CaptureCompilerDiagnostics { get; init; }

    public bool EmitStackTrace { get; init; }

    public bool RequiresReactor { get; init; }

    public string DesktopEntryMethod { get; init; } = "Run";

    public string WasmEntryMethod { get; init; } = "Run";

    public string? ReactorObserveMethod { get; init; }

    public ImmutableArray<string> NetWasmReferencePaths { get; init; } = [];

    public ImmutableDictionary<string, string> ReferenceAssemblyAliases { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    public string? FrozenOracleEvidencePath { get; init; }

    public TimeSpan? ProcessTimeout { get; init; }

    public OracleMode OracleMode { get; init; } = OracleMode.SameSource;

    public string? SameSourceReason { get; init; } =
        "The fixture exercises NetWasm.CoreLib or runtime-specific behavior.";


    public string EntryType => $"{Namespace}.EntryPoint";
}

internal sealed record CorpusSourceFile(string Name, string Content);

internal sealed record CorpusSourceArtifact(string Name, string Path, string Sha256);

internal sealed record CorpusArtifact(
    string AssemblyPath,
    string PdbPath,
    string AssemblySha256,
    string PdbSha256,
    string CompilerVersion,
    ImmutableArray<string> CompilerOptions);

internal sealed record CorpusCompilation(
    CorpusFixture Fixture,
    CilProfile Profile,
    CorpusArtifact Desktop,
    CorpusArtifact NetWasm,
    string Directory)
{
    public ImmutableArray<CorpusSourceArtifact> Sources { get; init; } = [];
}

internal sealed record OracleObservation(
    OracleObservationKind Kind,
    int? Value,
    string? ExceptionType,
    int Trace)
{
    public string? Detail { get; init; }

    public ImmutableArray<TraceRecord> TraceRecords { get; init; } =
        [new(TraceRecordKind.StateChecksum, 0, Trace)];
}

internal sealed record CorpusHostIdentity(
    string? TargetFramework,
    string RuntimeVersion,
    string FrameworkDescription,
    string ProcessArchitecture);

internal sealed record FrozenOracleRequest(
    string RelativePath,
    string FixtureName,
    CilProfile Profile,
    ImmutableArray<int> Inputs,
    string SourceSha256,
    string DesktopAssemblySha256,
    string SdkVersion,
    string TargetFramework,
    string RuntimeVersion,
    string FrameworkDescription,
    string ProcessArchitecture);

internal sealed record OracleComparison(bool Equivalent, string Message);

internal sealed record NetWasmExecution(
    ImmutableDictionary<int, OracleObservation> Observations,
    string? DiagnosticTracePath,
    string ModulePath,
    string ModuleSha256,
    WasmTarget Target,
    bool Executed)
{
    public CorpusExecutionBackend Backend { get; init; } =
        CorpusExecutionBackend.Simulated;

    public CorpusWasmForm Form { get; init; } = CorpusWasmForm.Direct;

    public ImmutableDictionary<int, OracleObservation> OptimizedObservations { get; init; } =
        ImmutableDictionary<int, OracleObservation>.Empty;

    public string? OptimizedModulePath { get; init; }

    public string? OptimizedModuleSha256 { get; init; }
}

internal interface IRoslynCorpusCompiler
{
    CorpusCompilation Compile(
        CorpusFixture fixture,
        CilProfile profile,
        string outputDirectory);
}

internal interface IDesktopOracleRunner
{
    ImmutableDictionary<int, OracleObservation> Run(
        CorpusCompilation compilation,
        CancellationToken cancellationToken = default);
}

internal interface INetWasmOracleRunner
{
    NetWasmExecution CompileAndRun(
        CorpusCompilation compilation,
        WasmTarget target,
        CancellationToken cancellationToken = default);
}

internal interface IOracleComparer
{
    OracleComparison Compare(
        OracleObservation desktop,
        OracleObservation netWasm);
}

internal interface ICompilerFailureArtifactWriter
{
    string Write(
        CorpusCompilation compilation,
        int input,
        OracleObservation desktop,
        OracleObservation netWasm,
        NetWasmExecution execution,
        string reason);
}

internal interface IDifferentialCorpusRunner
{
    void Run(CorpusFixture fixture);
}

internal interface ICompiledCorpusRunner
{
    void Run(
        CorpusCompilation compilation,
        CancellationToken cancellationToken = default);
}

internal interface ICompiledCorpusComparisonRunner
{
    void RunAgainstOracle(
        CorpusCompilation compilation,
        ImmutableDictionary<int, OracleObservation> expected,
        CancellationToken cancellationToken = default);
}

internal sealed record CorpusDimension(
    string Name,
    ImmutableArray<string> Levels);

internal sealed record GeneratedCorpusCase(
    string Name,
    string Family,
    int Seed,
    ImmutableDictionary<string, string> Dimensions,
    CorpusFixture Fixture)
{
}

internal interface IPairwiseCoveringArray
{
    ImmutableArray<ImmutableDictionary<string, string>> Generate(
        ImmutableArray<CorpusDimension> dimensions,
        int seed);
}

internal interface IGeneratedCorpusTemplate
{
    string Family { get; }

    int Seed { get; }

    ImmutableArray<CorpusDimension> Dimensions { get; }

    CorpusFixture Create(
        string caseName,
        ImmutableDictionary<string, string> values);

    ImmutableArray<ImmutableDictionary<string, string>> TargetedCases { get; }
}

internal interface ILanguageCorpusGenerator
{
    ImmutableArray<GeneratedCorpusCase> GeneratePullRequestCorpus();
}

internal interface IGeneratedCorpusRunner
{
    void Run(GeneratedCorpusCase generatedCase);
}

internal interface IGeneratedFailureArtifactWriter
{
    string Write(
        GeneratedCorpusCase generatedCase,
        Exception exception,
        SourceReductionResult? reduction = null);
}

internal interface IValidCSharpFuzzGenerator
{
    ImmutableArray<GeneratedCorpusCase> Generate(ImmutableArray<int> seeds);
}

internal sealed record CfgPropertyCase(
    string Name,
    int Seed,
    string Shape,
    CorpusFixture Fixture,
    ImmutableDictionary<int, OracleObservation> Expected);

internal interface ICfgPropertyCorpusGenerator
{
    ImmutableArray<CfgPropertyCase> Generate(ImmutableArray<int> seeds);
}

internal interface ICfgPropertyRunner
{
    void Run(CfgPropertyCase property);
}

internal sealed record MalformedInputMutation(
    string Name,
    string Invariant,
    string Path,
    string Sha256,
    int FileOffset,
    string OriginalHex,
    string ReplacementHex);

internal interface IMalformedInputGenerator
{
    ImmutableArray<MalformedInputMutation> Generate(
        string validAssembly,
        string outputDirectory);

    ImmutableArray<MalformedInputMutation> GenerateExceptionHandling(
        string validAssembly,
        string outputDirectory);
}

internal sealed record MalformedCompilationObservation(
    QualifiedProcessCompletion Completion,
    CompilerDiagnostic? Diagnostic,
    bool ModuleExists,
    bool FailureSnapshotExists,
    string StandardError)
{
    public int? ExitCode { get; init; }
    public string? ArtifactPrefix { get; init; }
    public QualifiedProcessResult? Process { get; init; }
    public Exception? ResponseFailure { get; init; }
}

internal interface IMalformedCompilationRunner
{
    MalformedCompilationObservation Compile(
        MalformedInputMutation mutation,
        string referencePath,
        string entryType,
        string outputDirectory,
        int attempt);
}
