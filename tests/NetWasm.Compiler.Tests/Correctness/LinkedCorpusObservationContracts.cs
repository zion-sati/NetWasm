using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record LinkedCorpusObservationRequest(
    int SchemaVersion,
    string Target,
    string ModulePath,
    string ManifestPath,
    string ModuleSha256,
    string ManifestSha256,
    ImmutableArray<int> Inputs,
    bool ExposesLegacyTrace,
    bool UsesTypedTrace)
{
    // The method-symbols sidecar a fixture that emits stack traces opens, which the runner mounts
    // under /netwasm-sidecars as the program's hosts do; null for every other fixture.
    public LinkedCorpusSidecar? MethodSymbols { get; init; }
}

// A sidecar file and the name the program opens it by (<program>.wasm.method-symbols).
internal sealed record LinkedCorpusSidecar(string Path, string FileName);

internal sealed record LinkedCorpusObservationInvocation(
    LinkedCorpusObservationRequest Request,
    QualifiedProcessRequest Process,
    string RequestPath,
    string ResponsePath);

internal sealed record LinkedCorpusObservationResult(
    ImmutableDictionary<int, OracleObservation> Observations,
    string ModuleSha256,
    string ManifestSha256);

internal sealed class LinkedCorpusObservationException(
    LinkedCorpusObservationInvocation invocation,
    QualifiedProcessResult result) :
    Exception(Describe(result), result.LaunchException)
{
    // What CI can show of a failed run: how it ended and the tail of what it wrote.
    private static string Describe(QualifiedProcessResult result) =>
        $"linked corpus observation process failed: completion={result.Completion}, exit={result.ExitCode}" +
        $"{Environment.NewLine}stderr: {Tail(result.StandardError)}{Environment.NewLine}stdout: {Tail(result.StandardOutput)}";

    private static string Tail(string text) => text.Length <= 4000 ? text : "..." + text[^4000..];

    public LinkedCorpusObservationInvocation Invocation { get; } = invocation;
    public QualifiedProcessResult Result { get; } = result;
}

internal sealed record LinkedCorpusObservationEnvironment(
    string NodePath,
    string RunnerPath,
    TimeSpan Timeout);
