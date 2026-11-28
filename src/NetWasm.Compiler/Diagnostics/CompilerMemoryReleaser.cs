// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerMemoryReleaser
{
    // Returns to the operating system the memory the collector keeps committed beyond what the compiler still holds,
    // once the compile has grown by enough since it last did.
    void Release();
}

// The Native AOT nwc collects with Satori, which keeps the memory a stage freed committed: on Devolutions' Avalonia
// sample 52 GB stayed committed over a 3 GB managed heap after the layouts, and the compile peaked there. A full
// blocking, compacting collection in aggressive mode returns it (the working set fell to 9 GB). It runs once the
// committed memory is at least Floor and has grown by at least Growth since the last one. Between stages (Floor 2 GiB,
// Growth 0) that is every boundary of a compile of any size, which keeps one stage's committed memory out of the next
// (the layouts peaked at 28 GB without the boundary collection, 11.5 with it); within a stage (Floor 0, Growth 2 GiB)
// it is every 2 GiB a drain, an expansion or emission adds. A compile that never grows that much never pays for one.
// After a collection Satori keeps 3-5 GB committed above the live heap, so the growth allowed on top of that sets the
// peak: on the three-app repro (Debug) 4 GiB peaked at 20.8 GiB with 40 collections (38 s), 2 GiB at 17.0 GiB with 89
// (72 s), 1 GiB at 16.3 GiB with 187 (141 s).
// (Comparing against the managed heap does not work: between collections that counts the garbage too.) In a browser
// or under WASI there is no process memory to return, so it does nothing.
internal sealed class CollectingCompilerMemoryReleaser(
    bool hasProcess,
    ICompilerMemoryReader memory,
    long floor,
    long growth,
    ILogger<CollectingCompilerMemoryReleaser> logger) : ICompilerMemoryReleaser
{
    internal const long StageFloor = 2L << 30;
    internal const long ClosureGrowth = 2L << 30;

    private const double Gigabyte = 1024d * 1024 * 1024;

    private static readonly Action<ILogger, double, double, double, double, Exception?> LogRelease =
        LoggerMessage.Define<double, double, double, double>(
            LogLevel.Debug,
            new EventId(4322, "CompilerMemoryRelease"),
            "Memory released: {Before:F2} GB committed, {After:F2} GB after a full collection, {Live:F2} GB live, " +
            "{Milliseconds:F0} ms");

    private readonly ICompilerMemoryReader _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly ILogger<CollectingCompilerMemoryReleaser> _logger = logger ??
        throw new ArgumentNullException(nameof(logger));
    // A compile's thread and nwc's memory watch may both ask at once; one collection serves both.
    private readonly Lock _gate = new();
    private long _committedAfterRelease;

    public void Release()
    {
        if (!hasProcess || !_gate.TryEnter())
        {
            return;
        }
        try
        {
            var committed = _memory.Read().Committed;
            if (committed < floor || committed - Interlocked.Read(ref _committedAfterRelease) < growth)
            {
                return;
            }
            var started = Stopwatch.GetTimestamp();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            var elapsed = Stopwatch.GetElapsedTime(started);
            var after = _memory.Read();
            Interlocked.Exchange(ref _committedAfterRelease, after.Committed);
            LogRelease(_logger, committed / Gigabyte, after.Committed / Gigabyte, after.ManagedHeap / Gigabyte,
                elapsed.TotalMilliseconds, null);
        }
        finally
        {
            _gate.Exit();
        }
    }
}
