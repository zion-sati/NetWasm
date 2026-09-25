using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record GeneratedCilFailureFingerprint(
    string Kind,
    string Stage,
    string Detail);

internal sealed record GeneratedCilReductionCase(
    GeneratedCilProgram Program,
    ImmutableArray<int> Inputs)
{
    public ImmutableArray<GeneratedCilProgram> SupportingMethods { get; init; } = [];
}

internal delegate GeneratedCilFailureFingerprint? GeneratedCilFailureOracle(
    GeneratedCilReductionCase candidate,
    CancellationToken cancellationToken);

internal sealed record GeneratedCilReductionRequest(
    GeneratedCilReductionCase Original,
    GeneratedCilFailureFingerprint Fingerprint,
    GeneratedCilFailureOracle Oracle,
    TimeSpan Timeout);

internal sealed record GeneratedCilReductionResult(
    GeneratedCilReductionCase Original,
    GeneratedCilReductionCase Reduced,
    GeneratedCilFailureFingerprint Fingerprint,
    bool TimedOut,
    int Attempts,
    ImmutableArray<string> AppliedPasses);

internal interface IGeneratedCilReductionPass
{
    string Name { get; }

    IEnumerable<GeneratedCilReductionCase> Generate(
        GeneratedCilReductionCase candidate);
}

internal interface IGeneratedCilReducer
{
    GeneratedCilReductionResult Reduce(GeneratedCilReductionRequest request);
}

internal interface IGeneratedCilManifestWriter
{
    void Write(
        string path,
        GeneratedCilProgram program,
        PatchedMethodBody? patched = null,
        CorpusArtifact? artifact = null);
}

internal sealed record GeneratedCilFailureBundleRequest(
    string OutputDirectory,
    GeneratedCilReductionResult Reduction,
    CorpusArtifact CompilerArtifact,
    string OriginalAssemblyPath,
    string ReducedAssemblyPath,
    string CoreClrTracePath,
    string NetWasmTracePath,
    string CompilerTracePath,
    string ComplexityMetricsPath,
    string CfgPath,
    string WasmPath,
    string ReproduceCommand);

internal interface IGeneratedCilFailureBundleWriter
{
    string Write(GeneratedCilFailureBundleRequest request);
}

internal interface IGeneratedCilRegressionRunner
{
    void RunRaw(int seed, ReadOnlySpan<byte> cil, ImmutableArray<int> inputs);
}

internal interface IGeneratedCilRegressionPromoter
{
    string Promote(
        string regressionId,
        string outputDirectory,
        GeneratedCilReductionResult reduction);
}

internal sealed record InvalidCilReductionRequest(
    byte[] Original,
    string Invariant,
    Func<ReadOnlyMemory<byte>, CancellationToken, string?> Oracle,
    TimeSpan Timeout);

internal sealed record InvalidCilReductionResult(
    byte[] Original,
    byte[] Reduced,
    string Invariant,
    bool TimedOut,
    int Attempts);

internal interface IInvalidCilReducer
{
    InvalidCilReductionResult Reduce(InvalidCilReductionRequest request);
}

internal interface IRoslynSyntaxReducer
{
    string Reduce(
        string source,
        Func<string, CancellationToken, bool> preservesFailure,
        TimeSpan timeout);
}
