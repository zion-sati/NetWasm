// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
    ImmutableArray<CompilerStageMetrics> Stages);

public sealed record CompilerAdapterTiming(
    TimeSpan TotalDuration,
    TimeSpan CompilerDuration,
    TimeSpan ResidualDuration);

public interface ICompilerMetricsObserver
{
    void Report(CompilerMetricsReport report);
}
