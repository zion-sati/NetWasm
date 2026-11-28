// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;

namespace NetWasm.Compiler.Diagnostics;

internal interface IAnalysisStepRecorder
{
    // Runs one step of the whole-program analysis and returns what it returns. When the diagnostic log takes debug
    // entries, the step's entry gives its time, the bytes every thread allocated while it ran and the memory after it;
    // at trace level a second entry gives the heap still live after a full collection, one collection per step, which
    // a timing run leaves off.
    T Record<T>(string step, Func<T> run);
}

// The recorder for a builder made without one: each step only runs.
internal sealed class UnrecordedAnalysisStepRecorder : IAnalysisStepRecorder
{
    public static UnrecordedAnalysisStepRecorder Instance { get; } = new();

    private UnrecordedAnalysisStepRecorder()
    {
    }

    public T Record<T>(string step, Func<T> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run();
    }
}

internal static class AnalysisStepRecorderExtensions
{
    // A step that returns nothing.
    public static void Record(this IAnalysisStepRecorder steps, string step, Action run)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(run);
        steps.Record(step, () =>
        {
            run();
            return true;
        });
    }
}

// The diagnostic log's account of where a large compile spends its time and memory, step by step: what a build that
// runs out of memory, or never seems to finish, needs its log to show.
internal sealed class LoggingAnalysisStepRecorder(
    ILogger<LoggingAnalysisStepRecorder> logger,
    ICompilerMemoryReader memory) : IAnalysisStepRecorder
{
    private const double Megabyte = 1024d * 1024;
    private const double Gigabyte = 1024d * 1024 * 1024;

    private static readonly Action<ILogger, string, double, double, double, double, double, Exception?> LogStep =
        LoggerMessage.Define<string, double, double, double, double, double>(
            LogLevel.Debug,
            new EventId(4320, "AnalysisStep"),
            "Analysis step {Step}: {Milliseconds:F0} ms, {Allocated:F0} MB allocated; {WorkingSet:F2} GB working set, " +
            "{ManagedHeap:F2} GB managed heap, {Committed:F2} GB committed");
    private static readonly Action<ILogger, string, double, Exception?> LogLiveHeap =
        LoggerMessage.Define<string, double>(
            LogLevel.Trace,
            new EventId(4321, "AnalysisStepLiveHeap"),
            "Analysis step {Step}: {LiveHeap:F2} GB live after a full collection");

    private readonly ILogger<LoggingAnalysisStepRecorder> _logger = logger ??
        throw new ArgumentNullException(nameof(logger));
    private readonly ICompilerMemoryReader _memory = memory ?? throw new ArgumentNullException(nameof(memory));

    public T Record<T>(string step, Func<T> run)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(run);
        if (!_logger.IsEnabled(LogLevel.Debug)) return run();
        var allocated = GC.GetTotalAllocatedBytes(precise: false);
        var started = Stopwatch.GetTimestamp();
        var result = run();
        var elapsed = Stopwatch.GetElapsedTime(started);
        allocated = GC.GetTotalAllocatedBytes(precise: false) - allocated;
        var after = _memory.Read();
        LogStep(_logger, step, elapsed.TotalMilliseconds, allocated / Megabyte, after.WorkingSet / Gigabyte,
            after.ManagedHeap / Gigabyte, after.Committed / Gigabyte, null);
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            LogLiveHeap(_logger, step, GC.GetTotalMemory(forceFullCollection: true) / Gigabyte, null);
        }
        return result;
    }
}
