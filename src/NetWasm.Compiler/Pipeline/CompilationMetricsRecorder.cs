using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompilationTimestampReader
{
    long Read();
}

internal interface ICompilationElapsedTimeCalculator
{
    TimeSpan Calculate(long startingTimestamp, long endingTimestamp);
}

internal interface ICompilationMetricStageStarter
{
    void Start(CompilerMetricStage stage);
}

internal interface ICompilationMetricCounterRecorder
{
    void Count(string name, long value);
}

internal interface ICompilationMetricReportCompleter
{
    void Complete(CompilerMetricsOutcome outcome, string? failedStage);
}

internal interface ICompilationMetricsRequestFactory
{
    CompilationMetricsRequest Create(ICompilerMetricsObserver observer);
}

internal sealed class CompilationTimestampReader : ICompilationTimestampReader
{
    public long Read() => TimeProvider.System.GetTimestamp();
}

internal sealed class CompilationElapsedTimeCalculator : ICompilationElapsedTimeCalculator
{
    public TimeSpan Calculate(long startingTimestamp, long endingTimestamp) =>
        TimeProvider.System.GetElapsedTime(startingTimestamp, endingTimestamp);
}

internal sealed class CompilationMetricsRequest(
    ICompilationMetricStageStarter stages,
    ICompilationMetricCounterRecorder counters,
    ICompilationMetricReportCompleter reports)
{
    internal ICompilationMetricStageStarter Stages { get; } = stages ??
        throw new ArgumentNullException(nameof(stages));
    internal ICompilationMetricCounterRecorder Counters { get; } = counters ??
        throw new ArgumentNullException(nameof(counters));
    internal ICompilationMetricReportCompleter Reports { get; } = reports ??
        throw new ArgumentNullException(nameof(reports));
}

internal sealed class CompilationMetricsRequestFactory(
    ICompilationTimestampReader timestamps,
    ICompilationElapsedTimeCalculator elapsedTimes) : ICompilationMetricsRequestFactory
{
    private readonly ICompilationTimestampReader _timestamps = timestamps ??
        throw new ArgumentNullException(nameof(timestamps));
    private readonly ICompilationElapsedTimeCalculator _elapsedTimes = elapsedTimes ??
        throw new ArgumentNullException(nameof(elapsedTimes));

    public CompilationMetricsRequest Create(ICompilerMetricsObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var data = new CompilationMetricsData();
        return new(
            new CompilationMetricStageStarter(data, _timestamps, _elapsedTimes),
            new CompilationMetricCounterRecorder(data),
            new CompilationMetricReportCompleter(
                data, observer, _timestamps, _elapsedTimes));
    }
}

internal sealed class CompilationMetricsData
{
    internal ImmutableArray<CompilerStageMetrics>.Builder Stages { get; } =
        ImmutableArray.CreateBuilder<CompilerStageMetrics>();
    internal Dictionary<string, long> Counters { get; } = new(StringComparer.Ordinal);
    internal CompilerMetricStage Stage { get; set; }
    internal long TotalStarted { get; set; }
    internal long StageStarted { get; set; }
    internal bool Started { get; set; }
    internal bool Completed { get; set; }
}

internal sealed class CompilationMetricStageStarter(
    CompilationMetricsData data,
    ICompilationTimestampReader timestamps,
    ICompilationElapsedTimeCalculator elapsedTimes) : ICompilationMetricStageStarter
{
    private readonly CompilationMetricsData _data = data ??
        throw new ArgumentNullException(nameof(data));
    private readonly ICompilationTimestampReader _timestamps = timestamps ??
        throw new ArgumentNullException(nameof(timestamps));
    private readonly ICompilationElapsedTimeCalculator _elapsedTimes = elapsedTimes ??
        throw new ArgumentNullException(nameof(elapsedTimes));

    public void Start(CompilerMetricStage stage)
    {
        CompilationMetricsGuard.ThrowIfCompleted(_data);
        var now = _timestamps.Read();
        if (_data.Started)
        {
            CompilationMetricsStageAppender.Append(_data, now, _elapsedTimes);
        }
        else
        {
            _data.Started = true;
            _data.TotalStarted = now;
        }

        _data.Stage = stage;
        _data.StageStarted = now;
    }
}

internal sealed class CompilationMetricCounterRecorder(
    CompilationMetricsData data) : ICompilationMetricCounterRecorder
{
    private readonly CompilationMetricsData _data = data ??
        throw new ArgumentNullException(nameof(data));

    public void Count(string name, long value)
    {
        CompilationMetricsGuard.ThrowIfCompleted(_data);
        if (!_data.Started)
        {
            throw new InvalidOperationException("No compiler metrics stage has started.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        _data.Counters[name] = value;
    }
}

internal sealed class CompilationMetricReportCompleter(
    CompilationMetricsData data,
    ICompilerMetricsObserver observer,
    ICompilationTimestampReader timestamps,
    ICompilationElapsedTimeCalculator elapsedTimes) : ICompilationMetricReportCompleter
{
    private readonly CompilationMetricsData _data = data ??
        throw new ArgumentNullException(nameof(data));
    private readonly ICompilerMetricsObserver _observer = observer ??
        throw new ArgumentNullException(nameof(observer));
    private readonly ICompilationTimestampReader _timestamps = timestamps ??
        throw new ArgumentNullException(nameof(timestamps));
    private readonly ICompilationElapsedTimeCalculator _elapsedTimes = elapsedTimes ??
        throw new ArgumentNullException(nameof(elapsedTimes));

    public void Complete(CompilerMetricsOutcome outcome, string? failedStage)
    {
        CompilationMetricsGuard.ThrowIfCompleted(_data);
        if (!_data.Started)
        {
            throw new InvalidOperationException("No compiler metrics stage has started.");
        }

        var now = _timestamps.Read();
        CompilationMetricsStageAppender.Append(_data, now, _elapsedTimes);
        _data.Completed = true;
        var stages = _data.Stages.ToImmutable();
        var stageDuration = TimeSpan.Zero;
        foreach (var stage in stages)
        {
            stageDuration += stage.Duration;
        }
        var totalDuration = _elapsedTimes.Calculate(_data.TotalStarted, now);
        var report = new CompilerMetricsReport(
            outcome,
            failedStage,
            totalDuration,
            stageDuration,
            totalDuration - stageDuration,
            stages);
        try
        {
            _observer.Report(report);
        }
        catch
        {
            // Metrics are observational and must not change compilation behavior.
        }
    }
}

internal static class CompilationMetricsStageAppender
{
    internal static void Append(
        CompilationMetricsData data,
        long ended,
        ICompilationElapsedTimeCalculator elapsedTimes)
    {
        var counters = data.Counters.ToImmutableDictionary(StringComparer.Ordinal);
        data.Stages.Add(new(
            data.Stage,
            elapsedTimes.Calculate(data.StageStarted, ended),
            counters));
        data.Counters.Clear();
    }
}

internal static class CompilationMetricsGuard
{
    internal static void ThrowIfCompleted(CompilationMetricsData data)
    {
        if (data.Completed)
        {
            throw new InvalidOperationException("Compiler metrics have already completed.");
        }
    }
}
