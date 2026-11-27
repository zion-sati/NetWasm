// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Diagnostics;
using System.Collections.Generic;
using System.Threading;

namespace NetWasm.Compiler.Tests.Diagnostics;

public sealed class MemoryReportingProgressReporterTests
{
    private const long Gigabyte = 1L << 30;

    [Fact]
    public void FollowsEveryStageWithTheMemoryAndReportsItAgainEachPeriodUntilTheCompilationCompletes()
    {
        var (reporter, inner, logger, time) = Create();

        reporter.Report(CompilerProgressStage.Start);
        time.Advance(TimeSpan.FromSeconds(2.5));
        reporter.Report(CompilerProgressStage.Metadata);
        var timer = Assert.Single(time.Timers);
        Assert.Equal(MemoryReportingProgressReporter.Period, timer.DueTime);
        Assert.Equal(MemoryReportingProgressReporter.Period, timer.Period);
        time.Advance(TimeSpan.FromSeconds(30));
        timer.Fire();
        reporter.Report(CompilerProgressStage.Complete);

        Assert.Equal([CompilerProgressStage.Start, CompilerProgressStage.Metadata, CompilerProgressStage.Complete],
            inner.Stages);
        const string memory = "Memory: 1.50 GB working set, 2.00 GB peak, 0.25 GB managed heap, 3.00 GB committed, " +
            "0.50 GB fragmented";
        Assert.Equal(
        [
            $"{memory} (0.0s elapsed)",
            $"{memory} (2.5s elapsed)",
            $"{memory} (32.5s elapsed)",
            $"{memory} (32.5s elapsed)",
        ], logger.Messages);
        Assert.True(timer.Disposed);
    }

    // A failed compilation reports Failed instead of Complete: its timer stops there, so nothing logs into the
    // failed compilation's diagnostic log once that log is closed.
    [Fact]
    public void AFailedCompilationStopsTheTimer()
    {
        var (reporter, _, logger, time) = Create();
        reporter.Report(CompilerProgressStage.Start);
        time.Advance(TimeSpan.FromSeconds(3));

        reporter.Report(CompilerProgressStage.Failed);

        var timer = Assert.Single(time.Timers);
        Assert.True(timer.Disposed);
        Assert.Equal(2, logger.Messages.Count);
    }

    [Fact]
    public void ANewCompilationRestartsTheClockAndTheTimer()
    {
        var (reporter, _, logger, time) = Create();
        reporter.Report(CompilerProgressStage.Start);
        time.Advance(TimeSpan.FromSeconds(4));

        reporter.Report(CompilerProgressStage.Start);

        Assert.Equal(2, time.Timers.Count);
        Assert.True(time.Timers[0].Disposed);
        Assert.False(time.Timers[1].Disposed);
        Assert.EndsWith("(0.0s elapsed)", logger.Messages[^1], StringComparison.Ordinal);
        reporter.Dispose();
        Assert.True(time.Timers[1].Disposed);
    }

    [Fact]
    public void ReadsNothingAndStartsNoTimerWhenNoLoggerTakesItsEntries()
    {
        var (reporter, inner, logger, time) = Create();
        logger.Enabled = false;

        reporter.Report(CompilerProgressStage.Start);
        reporter.Report(CompilerProgressStage.Complete);
        reporter.Dispose();

        Assert.Equal(2, inner.Stages.Count);
        Assert.Empty(time.Timers);
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public void RequiresItsCollaborators()
    {
        var inner = new RecordingReporter();
        var memory = new FixedMemory();
        var logger = new RecordingLogger();
        Assert.Throws<ArgumentNullException>(() =>
            new MemoryReportingProgressReporter(null!, memory, logger, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() =>
            new MemoryReportingProgressReporter(inner, null!, logger, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() =>
            new MemoryReportingProgressReporter(inner, memory, null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() =>
            new MemoryReportingProgressReporter(inner, memory, logger, null!));
    }

    // macOS reports no peak working set of its own; the reader's peak still never falls below what it has read.
    [Fact]
    public void TheProcessReaderReportsThisProcess()
    {
        var reader = new ProcessCompilerMemoryReader(hasProcess: true);
        var memory = reader.Read();
        var later = reader.Read();

        Assert.True(memory.WorkingSet > 0);
        Assert.True(memory.PeakWorkingSet >= memory.WorkingSet);
        Assert.True(memory.ManagedHeap > 0);
        Assert.True(memory.Committed >= 0);
        Assert.True(memory.Fragmented >= 0);
        Assert.True(later.PeakWorkingSet >= memory.PeakWorkingSet);
        Assert.True(later.PeakWorkingSet >= later.WorkingSet);
    }

    // In a browser or under WASI there is no process: only the managed heap is read.
    [Fact]
    public void WithoutAProcessOnlyTheManagedHeapIsRead()
    {
        var memory = new ProcessCompilerMemoryReader(hasProcess: false).Read();

        Assert.Equal((0L, 0L), (memory.WorkingSet, memory.PeakWorkingSet));
        Assert.True(memory.ManagedHeap > 0);
    }

    private static (MemoryReportingProgressReporter Reporter, RecordingReporter Inner, RecordingLogger Logger,
        ManualTimeProvider Time) Create()
    {
        var inner = new RecordingReporter();
        var logger = new RecordingLogger();
        var time = new ManualTimeProvider();
        return (new MemoryReportingProgressReporter(inner, new FixedMemory(), logger, time), inner, logger, time);
    }

    private sealed class FixedMemory : ICompilerMemoryReader
    {
        public CompilerMemorySnapshot Read() =>
            new(3 * Gigabyte / 2, 2 * Gigabyte, Gigabyte / 4, 3 * Gigabyte, Gigabyte / 2);
    }

    private sealed class RecordingReporter : ICompilerProgressReporter
    {
        public List<CompilerProgressStage> Stages { get; } = [];

        public void Report(CompilerProgressStage stage) => Stages.Add(stage);
    }

    private sealed class RecordingLogger : ILogger<MemoryReportingProgressReporter>
    {
        public bool Enabled { get; set; } = true;

        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => Enabled && logLevel == LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp = 1;

        public List<ManualTimer> Timers { get; } = [];

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime, period);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) : ITimer
    {
        public TimeSpan DueTime { get; } = dueTime;

        public TimeSpan Period { get; } = period;

        public bool Disposed { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => Disposed = true;

        public System.Threading.Tasks.ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}
