using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class CompilerBuildProgressReporterTests
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
    public void ReportWritesTheActiveCompilerStage(
        CompilerProgressStage stage,
        string expected)
    {
        var messages = new RecordingBuildMessageWriter();
        ((ICompilerProgressReporter)new CompilerBuildProgressReporter(
            messages,
            new ManualTimeProvider()))
            .Report(stage);

        var message = Assert.Single(messages.Messages);
        Assert.Equal($"{expected} (0.0s elapsed)", message);
    }

    [Fact]
    public void ReportShowsCumulativeElapsedTimeSinceCompilationStarted()
    {
        var messages = new RecordingBuildMessageWriter();
        var time = new ManualTimeProvider();
        var reporter = new CompilerBuildProgressReporter(messages, time);

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
            messages.Messages);
    }

    [Fact]
    public void ReporterRejectsMissingWriterAndUnknownStage()
    {
        Assert.Throws<ArgumentNullException>(() => new CompilerBuildProgressReporter(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerBuildProgressReporter(new RecordingBuildMessageWriter(), null!));
        var reporter = new CompilerBuildProgressReporter(new RecordingBuildMessageWriter());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            reporter.Report((CompilerProgressStage)int.MaxValue));
    }

    [Fact]
    public void MessageWriterUsesHighImportanceMsBuildOutput()
    {
        var engine = new RecordingBuildEngine();
        var task = new RecordingTask { BuildEngine = engine };
        ((ICompilerBuildMessageWriter)new CompilerBuildMessageWriter(task.Logger))
            .Write("compiler progress");

        var message = Assert.Single(engine.Messages);
        Assert.Equal("compiler progress", message.Message);
        Assert.Equal(MessageImportance.High, message.Importance);
    }

    [Fact]
    public void MessageWriterRejectsMissingLoggerAndEmptyMessages()
    {
        Assert.Throws<ArgumentNullException>(() => new CompilerBuildMessageWriter(null!));
        var engine = new RecordingBuildEngine();
        var task = new RecordingTask { BuildEngine = engine };
        var writer = new CompilerBuildMessageWriter(task.Logger);

        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
        Assert.Throws<ArgumentException>(() => writer.Write(" "));
        Assert.Empty(engine.Messages);
    }

    private sealed class RecordingBuildMessageWriter : ICompilerBuildMessageWriter
    {
        public List<string> Messages { get; } = [];

        public void Write(string message) => Messages.Add(message);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }

    private sealed class RecordingTask : Microsoft.Build.Utilities.Task
    {
        public TaskLoggingHelper Logger => Log;

        public override bool Execute() => true;
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildMessageEventArgs> Messages { get; } = [];
        public int ColumnNumberOfTaskNode => 0;
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs) => false;

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public void LogErrorEvent(BuildErrorEventArgs e)
        {
        }

        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e);

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
        }
    }
}
