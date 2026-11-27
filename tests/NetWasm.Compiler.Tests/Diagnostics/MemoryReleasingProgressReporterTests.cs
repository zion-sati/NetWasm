// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests.Diagnostics;

public sealed class MemoryReleasingProgressReporterTests
{
    // The collector's committed memory goes back as the analysis, the layouts and the root maps finish, before the
    // stage is reported; no other stage releases it.
    [Fact]
    public void ReleasesMemoryAfterTheLongStagesBeforeReportingThem()
    {
        var events = new List<string>();
        var reporter = new MemoryReleasingProgressReporter(new Recording(events), new Releasing(events));

        foreach (var stage in new[]
        {
            CompilerProgressStage.Start, CompilerProgressStage.Metadata, CompilerProgressStage.Entry,
            CompilerProgressStage.Analysis, CompilerProgressStage.Layouts, CompilerProgressStage.RootMaps,
            CompilerProgressStage.Emission, CompilerProgressStage.Complete,
        })
        {
            reporter.Report(stage);
        }

        Assert.Equal(
            ["Start", "Metadata", "Entry", "release", "Analysis", "release", "Layouts", "release", "RootMaps",
                "Emission", "Complete"],
            events);
    }

    // A full collection runs only in a process, once the committed memory is at least the floor and has grown by the
    // growth since the last one; without a process (a browser, WASI) there is nothing to return.
    [Fact]
    public void TheCollectingReleaserCollectsOnceTheCommittedMemoryHasGrownEnough()
    {
        var memory = new SequencedMemory(3L << 29, 5L << 29, 1L << 29, 4L << 29);
        var logger = new ReleaseLog();
        var releaser = new CollectingCompilerMemoryReleaser(hasProcess: true, memory, floor: 0,
            growth: CollectingCompilerMemoryReleaser.ClosureGrowth, logger);

        // 1.5 GiB: not yet. 2.5 GiB: collects, leaving 0.5 GiB committed. 2 GiB: 1.5 GiB more, not yet.
        Assert.Equal([false, true, false], Collections(releaser, 3));
        Assert.Equal(4, memory.Reads);
        // The one collection logs what it returned: 2.5 GiB committed before, 0.5 GiB after.
        var message = Assert.Single(logger.Messages);
        Assert.StartsWith("Memory released: 2.50 GB committed, 0.50 GB after a full collection", message, StringComparison.Ordinal);
        var unprocessed = new SequencedMemory(8L << 30);
        new CollectingCompilerMemoryReleaser(hasProcess: false, unprocessed, floor: 0, growth: 0,
            NullLogger<CollectingCompilerMemoryReleaser>.Instance).Release();
        Assert.Equal(0, unprocessed.Reads);
    }

    // Between stages any compile holding the floor collects, every time; a smaller one never does.
    [Fact]
    public void TheStageReleaserCollectsWheneverTheFloorIsCommitted()
    {
        var stage = new CollectingCompilerMemoryReleaser(hasProcess: true,
            new SequencedMemory(1L << 30, 3L << 30, 3L << 30, 3L << 30, 3L << 30),
            floor: CollectingCompilerMemoryReleaser.StageFloor, growth: 0,
            NullLogger<CollectingCompilerMemoryReleaser>.Instance);

        Assert.Equal([false, true, true], Collections(stage, 3));
    }

    private static List<bool> Collections(CollectingCompilerMemoryReleaser releaser, int calls)
    {
        var collected = new List<bool>();
        for (var call = 0; call < calls; call++)
        {
            var before = GC.CollectionCount(GC.MaxGeneration);
            releaser.Release();
            collected.Add(GC.CollectionCount(GC.MaxGeneration) > before);
        }
        return collected;
    }

    private sealed class ReleaseLog : ILogger<CollectingCompilerMemoryReleaser>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private sealed class SequencedMemory(params long[] committed) : ICompilerMemoryReader
    {
        public int Reads { get; private set; }

        public CompilerMemorySnapshot Read()
        {
            var value = committed[Math.Min(Reads, committed.Length - 1)];
            Reads++;
            return new(value, value, 0, value, 0);
        }
    }

    private sealed class Recording(List<string> events) : ICompilerProgressReporter
    {
        public void Report(CompilerProgressStage stage) => events.Add(stage.ToString());
    }

    private sealed class Releasing(List<string> events) : ICompilerMemoryReleaser
    {
        public void Release() => events.Add("release");
    }
}

// nwc watches each compile's memory: a timer applies the releaser until the compile ends. Other hosts watch nothing.
public sealed class CompilerMemoryWatchTests
{
    [Fact]
    public void ThePollingWatchReleasesOnEachTickUntilItsHandleIsDisposed()
    {
        var releases = new CountingReleaser();
        var time = new ManualTime();

        var handle = new PollingCompilerMemoryWatch(releases, time).Watch();
        var timer = Assert.Single(time.Timers);
        timer.Tick();
        timer.Tick();
        handle.Dispose();

        Assert.Equal(2, releases.Count);
        Assert.Equal(PollingCompilerMemoryWatch.Interval, timer.DueTime);
        Assert.Equal(PollingCompilerMemoryWatch.Interval, timer.Period);
        Assert.True(timer.Disposed);
    }

    [Fact]
    public void OnlyACompilerProcessWatchesMemory()
    {
        using var hosted = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        using var process = new ServiceCollection().AddNetWasmCompiler().WatchCompilerMemory().BuildServiceProvider();

        Assert.IsType<UnwatchedCompilerMemory>(hosted.GetRequiredService<ICompilerMemoryWatch>());
        Assert.IsType<PollingCompilerMemoryWatch>(process.GetRequiredService<ICompilerMemoryWatch>());
        new UnwatchedCompilerMemory().Watch().Dispose();
    }

    private sealed class CountingReleaser : ICompilerMemoryReleaser
    {
        public int Count { get; private set; }

        public void Release() => Count++;
    }

    private sealed class ManualTime : TimeProvider
    {
        public List<ManualTimer> Timers { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(() => callback(state), dueTime, period);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class ManualTimer(Action tick, TimeSpan dueTime, TimeSpan period) : ITimer
    {
        public TimeSpan DueTime { get; } = dueTime;
        public TimeSpan Period { get; } = period;
        public bool Disposed { get; private set; }

        public void Tick() => tick();

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
