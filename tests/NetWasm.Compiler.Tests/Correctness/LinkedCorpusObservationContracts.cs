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
    bool UsesTypedTrace);

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
    Exception("linked corpus observation process failed", result.LaunchException)
{
    public LinkedCorpusObservationInvocation Invocation { get; } = invocation;
    public QualifiedProcessResult Result { get; } = result;
}

internal sealed record LinkedCorpusObservationEnvironment(
    string NodePath,
    string RunnerPath,
    TimeSpan Timeout);
