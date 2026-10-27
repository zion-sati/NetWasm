using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticProgressReporterTests
{
    public static TheoryData<CompilerProgressStage, string> StageMessages => new()
    {
        { CompilerProgressStage.Start, "NetWasm: Loading managed metadata..." },
        { CompilerProgressStage.Metadata, "NetWasm: Preparing the entry point..." },
        { CompilerProgressStage.Entry, "NetWasm: Analyzing the reachable program..." },
        { CompilerProgressStage.Analysis, "NetWasm: Computing managed layouts..." },
        { CompilerProgressStage.Layouts, "NetWasm: Computing GC root maps..." },
        { CompilerProgressStage.RootMaps, "NetWasm: Emitting WebAssembly..." },
        { CompilerProgressStage.Emission, "NetWasm: Recording compiler metrics..." },
        { CompilerProgressStage.Complexity, "NetWasm: Validating WebAssembly..." },
        { CompilerProgressStage.Validation, "NetWasm: Generating the interop manifest..." },
        { CompilerProgressStage.InteropManifest, "NetWasm: Finalizing compilation..." },
        { CompilerProgressStage.Complete, "NetWasm: Managed compilation completed." },
    };

    [Theory]
    [MemberData(nameof(StageMessages))]
    public void ReportLogsTheActiveCompilerStage(CompilerProgressStage stage, string expected)
    {
        var logger = new RecordingLogger();
        ((ICompilerProgressReporter)new CompilerDiagnosticProgressReporter(logger, new ManualTimeProvider()))
            .Report(stage);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(4002, entry.EventId.Id);
        Assert.Equal($"{expected} (0.0s elapsed)", entry.Message);
    }

    [Fact]
    public void ReportShowsCumulativeElapsedTimeSinceCompilationStarted()
    {
        var logger = new RecordingLogger();
        var time = new ManualTimeProvider();
        var reporter = new CompilerDiagnosticProgressReporter(logger, time);

        reporter.Report(CompilerProgressStage.Start);
        time.Advance(TimeSpan.FromMilliseconds(420));
        reporter.Report(CompilerProgressStage.Entry);
        time.Advance(TimeSpan.FromMilliseconds(880));
        reporter.Report(CompilerProgressStage.Complete);

        Assert.Equal(
            [
                "NetWasm: Loading managed metadata... (0.0s elapsed)",
                "NetWasm: Analyzing the reachable program... (0.4s elapsed)",
                "NetWasm: Managed compilation completed. (1.3s elapsed)",
            ],
            logger.Entries.Select(entry => entry.Message));
    }

    [Fact]
    public void ReporterRejectsMissingDependenciesAndAnUnknownStage()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerDiagnosticProgressReporter(null!, new ManualTimeProvider()));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerDiagnosticProgressReporter(new RecordingLogger(), null!));
        var logger = new RecordingLogger();
        var reporter = new CompilerDiagnosticProgressReporter(logger, new ManualTimeProvider());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            reporter.Report((CompilerProgressStage)int.MaxValue));
        Assert.Empty(logger.Entries);
    }

    private sealed record Entry(LogLevel Level, EventId EventId, string Message);

    private sealed class RecordingLogger : ILogger<CompilerDiagnosticProgressReporter>
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new(logLevel, eventId, formatter(state, exception)));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
