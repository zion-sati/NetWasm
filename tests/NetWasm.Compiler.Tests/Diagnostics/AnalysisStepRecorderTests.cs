// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests.Diagnostics;

public sealed partial class AnalysisStepRecorderTests
{
    private const long Gigabyte = 1L << 30;

    [Fact]
    public void ADebugLogGetsTheStepsTimeAllocationAndMemory()
    {
        var logger = new LevelLogger(LogLevel.Debug);
        var recorder = new LoggingAnalysisStepRecorder(logger, new FixedMemory());

        var result = recorder.Record("closure 1", () => new byte[4 << 20].Length);

        Assert.Equal(4 << 20, result);
        var entry = Assert.Single(logger.Messages);
        var match = StepEntry().Match(entry);
        Assert.True(match.Success, entry);
        Assert.Equal("closure 1", match.Groups["step"].Value);
        // Every thread's allocations count, so at least the step's own array.
        Assert.True(double.Parse(match.Groups["allocated"].Value, CultureInfo.InvariantCulture) >= 4, entry);
        Assert.EndsWith(
            "1.50 GB working set, 0.25 GB managed heap, 3.00 GB committed", entry, StringComparison.Ordinal);
    }

    // A full collection per step is what a retention run asks for, at trace level, and only there.
    [Fact]
    public void ATraceLogAlsoGetsTheHeapLiveAfterAFullCollection()
    {
        var logger = new LevelLogger(LogLevel.Trace);
        var recorder = new LoggingAnalysisStepRecorder(logger, new FixedMemory());

        recorder.Record("ledger snapshot", () => 0);

        Assert.Equal(2, logger.Messages.Count);
        Assert.Matches(StepEntry(), logger.Messages[0]);
        Assert.Matches(
            @"^Analysis step ledger snapshot: \d+\.\d\d GB live after a full collection$", logger.Messages[1]);
    }

    [Fact]
    public void WithoutADebugLogTheStepOnlyRuns()
    {
        var logger = new LevelLogger(LogLevel.Information);
        var memory = new FixedMemory();
        var recorder = new LoggingAnalysisStepRecorder(logger, memory);
        var ran = false;

        recorder.Record("drain 1", () => ran = true);

        Assert.True(ran);
        Assert.Empty(logger.Messages);
        Assert.Equal(0, memory.Reads);
    }

    [Fact]
    public void AStepThatThrowsGetsNoEntry()
    {
        var logger = new LevelLogger(LogLevel.Trace);
        var recorder = new LoggingAnalysisStepRecorder(logger, new FixedMemory());

        Assert.Throws<InvalidOperationException>(() =>
            recorder.Record<int>("program", () => throw new InvalidOperationException()));
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public void AStepAndItsWorkAreRequired()
    {
        var recorder = new LoggingAnalysisStepRecorder(new LevelLogger(LogLevel.Debug), new FixedMemory());

        Assert.Throws<ArgumentNullException>(() => recorder.Record("step", (Func<int>)null!));
        Assert.Throws<ArgumentNullException>(() => recorder.Record(null!, () => 0));
        Assert.Throws<ArgumentNullException>(() => recorder.Record("step", (Action)null!));
        Assert.Throws<ArgumentNullException>(() => ((IAnalysisStepRecorder)null!).Record("step", () => { }));
    }

    // A builder made without a recorder runs each step and logs nothing.
    [Fact]
    public void TheUnrecordedRecorderOnlyRunsTheStep()
    {
        Assert.Equal(7, UnrecordedAnalysisStepRecorder.Instance.Record("step", () => 7));
        Assert.Throws<ArgumentNullException>(() => UnrecordedAnalysisStepRecorder.Instance.Record<int>("step", null!));
    }

    [GeneratedRegex(@"^Analysis step (?<step>[a-z0-9 -]+): \d+ ms, (?<allocated>\d+) MB allocated; ")]
    private static partial Regex StepEntry();

    private sealed class FixedMemory : ICompilerMemoryReader
    {
        public int Reads { get; private set; }

        public CompilerMemorySnapshot Read()
        {
            Reads++;
            return new(3 * Gigabyte / 2, 2 * Gigabyte, Gigabyte / 4, 3 * Gigabyte, Gigabyte / 2);
        }
    }

    private sealed class LevelLogger(LogLevel minimum) : ILogger<LoggingAnalysisStepRecorder>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) Messages.Add(formatter(state, exception));
        }
    }
}
