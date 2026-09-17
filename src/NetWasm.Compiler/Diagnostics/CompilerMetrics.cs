using System;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace NetWasm.Compiler.Diagnostics;

[JsonConverter(typeof(JsonStringEnumConverter<CompilerMetricStage>))]
public enum CompilerMetricStage
{
    Initialization,
    Metadata,
    Preparation,
    Analysis,
    Layouts,
    RootMaps,
    LoweringAndEmission,
    Complexity,
    ValidationAndDiagnostics,
    InteropManifest,
    ResultProjectionAndBinding,
    FailureReporting,
}

[JsonConverter(typeof(JsonStringEnumConverter<CompilerMetricsOutcome>))]
public enum CompilerMetricsOutcome
{
    Succeeded,
    Failed,
    Canceled,
}

public sealed record CompilerStageMetrics(
    CompilerMetricStage Stage,
    TimeSpan Duration,
    ImmutableDictionary<string, long> Counters);

public sealed record CompilerMetricsReport(
    CompilerMetricsOutcome Outcome,
    string? FailedStage,
    TimeSpan TotalDuration,
    TimeSpan StageDuration,
    TimeSpan UnattributedDuration,
    ImmutableArray<CompilerStageMetrics> Stages,
    FrontendArtifactCacheMetrics? FrontendCache = null);

public sealed record FrontendArtifactCacheMetrics(
    long Lookups,
    long Hits,
    long Misses,
    long MemoryHits,
    long DiskHits,
    long StagedArtifacts,
    long StagedBytes);

public sealed record CompilerAdapterTiming(
    TimeSpan TotalDuration,
    TimeSpan CompilerDuration,
    TimeSpan ResidualDuration);

public interface ICompilerMetricsObserver
{
    void Report(CompilerMetricsReport report);
}
