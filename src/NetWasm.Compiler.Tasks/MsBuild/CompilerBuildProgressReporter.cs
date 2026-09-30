using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tasks.MsBuild;

internal sealed class CompilerBuildProgressReporter : ICompilerProgressReporter
{
    private readonly ICompilerBuildMessageWriter _messages;
    private readonly TimeProvider _timeProvider;
    private long _startedAt;

    public CompilerBuildProgressReporter(ICompilerBuildMessageWriter messages)
        : this(messages, TimeProvider.System)
    {
    }

    internal CompilerBuildProgressReporter(
        ICompilerBuildMessageWriter messages,
        TimeProvider timeProvider)
    {
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _startedAt = _timeProvider.GetTimestamp();
    }

    public void Report(CompilerProgressStage stage)
    {
        if (stage == CompilerProgressStage.Start)
        {
            _startedAt = _timeProvider.GetTimestamp();
        }

        var elapsed = _timeProvider.GetElapsedTime(_startedAt).TotalSeconds;
        var message = stage switch
        {
            CompilerProgressStage.Start => "Loading managed metadata...",
            CompilerProgressStage.Metadata => "Preparing the entry point...",
            CompilerProgressStage.Entry => "Analyzing the reachable program...",
            CompilerProgressStage.Analysis => "Computing managed layouts...",
            CompilerProgressStage.Layouts => "Computing GC root maps...",
            CompilerProgressStage.RootMaps => "Emitting WebAssembly...",
            CompilerProgressStage.Emission => "Recording compiler metrics...",
            CompilerProgressStage.Complexity => "Validating WebAssembly...",
            CompilerProgressStage.Validation => "Generating the interop manifest...",
            CompilerProgressStage.InteropManifest => "Finalizing compilation...",
            CompilerProgressStage.Complete => "Managed compilation completed.",
            _ => throw new ArgumentOutOfRangeException(nameof(stage)),
        };
        _messages.Write($"NetWasm: {message} ({elapsed:F1}s elapsed)");
    }
}
