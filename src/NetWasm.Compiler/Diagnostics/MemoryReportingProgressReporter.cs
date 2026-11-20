// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.Logging;
using System;
using System.Threading;

namespace NetWasm.Compiler.Diagnostics;

// Follows each compiler stage with the process's memory, and reports it again every Period while the compilation
// runs, from a timer, so that the diagnostic log of a build that runs out of memory shows how its memory grew and
// in which stage. It reads nothing and starts no timer unless a logger takes its entries: the diagnostic log does,
// inside a compilation that writes one, and nwc's console does not.
internal sealed class MemoryReportingProgressReporter : ICompilerProgressReporter, IDisposable
{
    internal static readonly TimeSpan Period = TimeSpan.FromSeconds(30);

    private const double Gigabyte = 1024d * 1024 * 1024;

    private static readonly Action<ILogger, double, double, double, double, Exception?> LogMemory =
        LoggerMessage.Define<double, double, double, double>(
            LogLevel.Information,
            new EventId(4003, "CompilerMemory"),
            "Memory: {WorkingSet:F2} GB working set, {PeakWorkingSet:F2} GB peak, " +
            "{ManagedHeap:F2} GB managed heap ({Elapsed:F1}s elapsed)");

    private readonly ICompilerProgressReporter _inner;
    private readonly ICompilerMemoryReader _memory;
    private readonly ILogger<MemoryReportingProgressReporter> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private long _startedAt;
    private ITimer? _timer;

    public MemoryReportingProgressReporter(
        ICompilerProgressReporter inner,
        ICompilerMemoryReader memory,
        ILogger<MemoryReportingProgressReporter> logger,
        TimeProvider timeProvider)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _startedAt = _timeProvider.GetTimestamp();
    }

    public void Report(CompilerProgressStage stage)
    {
        _inner.Report(stage);
        lock (_gate)
        {
            if (stage == CompilerProgressStage.Start)
            {
                _startedAt = _timeProvider.GetTimestamp();
                StopTimer();
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    // The timer runs in this compilation's execution context, so its entries reach its log.
                    _timer = _timeProvider.CreateTimer(
                        static state => ((MemoryReportingProgressReporter)state!).Log(), this, Period, Period);
                }
            }
            Log();
            if (stage == CompilerProgressStage.Complete)
            {
                StopTimer();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopTimer();
        }
    }

    private void Log()
    {
        lock (_gate)
        {
            if (!_logger.IsEnabled(LogLevel.Information)) return;
            var memory = _memory.Read();
            LogMemory(_logger, memory.WorkingSet / Gigabyte, memory.PeakWorkingSet / Gigabyte,
                memory.ManagedHeap / Gigabyte, _timeProvider.GetElapsedTime(_startedAt).TotalSeconds, null);
        }
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
