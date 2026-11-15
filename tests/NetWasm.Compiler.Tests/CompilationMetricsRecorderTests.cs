// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Pipeline;

namespace NetWasm.Compiler.Tests;

public sealed class CompilationMetricsRecorderTests
{
    [Fact]
    public void CompleteReportsContiguousStagesCountersAndTotal()
    {
        var observer = new RecordingObserver();
        var metrics = new CompilationMetricsRequestFactory(
            new SequenceClock(10, 15, 25, 40),
            new TickElapsedTimeCalculator()).Create(observer);

        metrics.Stages.Start(CompilerMetricStage.Initialization);
        metrics.Stages.Start(CompilerMetricStage.Metadata);
        metrics.Counters.Count("references", 3);
        metrics.Stages.Start(CompilerMetricStage.Analysis);
        metrics.Reports.Complete(CompilerMetricsOutcome.Succeeded, null);

        var report = Assert.Single(observer.Reports);
        Assert.Equal(CompilerMetricsOutcome.Succeeded, report.Outcome);
        Assert.Null(report.FailedStage);
        Assert.Equal(TimeSpan.FromTicks(30), report.TotalDuration);
        Assert.Equal(TimeSpan.FromTicks(30), report.StageDuration);
        Assert.Equal(TimeSpan.Zero, report.UnattributedDuration);
        Assert.Collection(
            report.Stages,
            stage => AssertStage(stage, CompilerMetricStage.Initialization, 5),
            stage =>
            {
                AssertStage(stage, CompilerMetricStage.Metadata, 10);
                Assert.Equal(3, stage.Counters["references"]);
            },
            stage => AssertStage(stage, CompilerMetricStage.Analysis, 15));
    }

    [Fact]
    public void CompleteReportsFailedStageAndObserverFailureIsObservational()
    {
        var observer = new RecordingObserver { Exception = new InvalidOperationException() };
        var metrics = new CompilationMetricsRequestFactory(
            new SequenceClock(1, 2, 4),
            new TickElapsedTimeCalculator()).Create(observer);

        metrics.Stages.Start(CompilerMetricStage.Analysis);
        metrics.Stages.Start(CompilerMetricStage.FailureReporting);
        metrics.Reports.Complete(CompilerMetricsOutcome.Failed, "analysis");

        var report = Assert.Single(observer.Reports);
        Assert.Equal(CompilerMetricsOutcome.Failed, report.Outcome);
        Assert.Equal("analysis", report.FailedStage);
        Assert.Equal(TimeSpan.FromTicks(3), report.TotalDuration);
    }

    [Fact]
    public void GuardsRejectInvalidRecorderUse()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CompilationMetricsRequestFactory(null!, new TickElapsedTimeCalculator()));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilationMetricsRequestFactory(new SequenceClock(1), null!));
        var data = new CompilationMetricsData();
        var timestamps = new SequenceClock(1);
        var elapsed = new TickElapsedTimeCalculator();
        var stages = new CompilationMetricStageStarter(data, timestamps, elapsed);
        var counters = new CompilationMetricCounterRecorder(data);
        var reports = new CompilationMetricReportCompleter(
            data, new RecordingObserver(), timestamps, elapsed);
        Assert.Throws<ArgumentNullException>(() =>
            new CompilationMetricsRequest(null!, counters, reports));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilationMetricsRequest(stages, null!, reports));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilationMetricsRequest(stages, counters, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilationMetricStageStarter(null!, timestamps, elapsed));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilationMetricStageStarter(data, null!, elapsed));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilationMetricStageStarter(data, timestamps, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilationMetricCounterRecorder(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilationMetricReportCompleter(
                null!, new RecordingObserver(), timestamps, elapsed));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilationMetricReportCompleter(data, null!, timestamps, elapsed));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilationMetricReportCompleter(
                data, new RecordingObserver(), null!, elapsed));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilationMetricReportCompleter(
                data, new RecordingObserver(), timestamps, null!));

        var factory = new CompilationMetricsRequestFactory(
            new SequenceClock(1, 2),
            new TickElapsedTimeCalculator());
        Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
        var metrics = factory.Create(new RecordingObserver());
        Assert.Throws<InvalidOperationException>(() =>
            metrics.Reports.Complete(CompilerMetricsOutcome.Canceled, "metadata"));
        Assert.Throws<InvalidOperationException>(() => metrics.Counters.Count("items", 0));

        metrics.Stages.Start(CompilerMetricStage.Metadata);
        Assert.Throws<ArgumentException>(() => metrics.Counters.Count("", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => metrics.Counters.Count("items", -1));
        metrics.Reports.Complete(CompilerMetricsOutcome.Canceled, "metadata");

        Assert.Throws<InvalidOperationException>(() =>
            metrics.Stages.Start(CompilerMetricStage.Analysis));
        Assert.Throws<InvalidOperationException>(() =>
            metrics.Counters.Count("items", 0));
        Assert.Throws<InvalidOperationException>(() =>
            metrics.Reports.Complete(CompilerMetricsOutcome.Succeeded, null));
    }

    private static void AssertStage(
        CompilerStageMetrics actual,
        CompilerMetricStage expectedStage,
        long expectedTicks)
    {
        Assert.Equal(expectedStage, actual.Stage);
        Assert.Equal(TimeSpan.FromTicks(expectedTicks), actual.Duration);
    }

    private sealed class RecordingObserver : ICompilerMetricsObserver
    {
        public List<CompilerMetricsReport> Reports { get; } = [];

        public Exception? Exception { get; init; }

        public void Report(CompilerMetricsReport report)
        {
            Reports.Add(report);
            if (Exception is not null)
            {
                throw Exception;
            }
        }
    }

    private sealed class SequenceClock(params long[] timestamps) : ICompilationTimestampReader
    {
        private readonly Queue<long> _timestamps = new(timestamps);

        public long Read() => _timestamps.Dequeue();
    }

    private sealed class TickElapsedTimeCalculator : ICompilationElapsedTimeCalculator
    {
        public TimeSpan Calculate(long startingTimestamp, long endingTimestamp) =>
            TimeSpan.FromTicks(endingTimestamp - startingTimestamp);
    }

}
