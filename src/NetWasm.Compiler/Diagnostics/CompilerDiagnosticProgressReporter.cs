using Microsoft.Extensions.Logging;
using System;

namespace NetWasm.Compiler.Diagnostics;

// Reports each compiler stage as it starts, with the time since the compilation
// began, to every logging provider: the diagnostic log and the host's output.
internal sealed class CompilerDiagnosticProgressReporter : ICompilerProgressReporter
{
    private static readonly Action<ILogger, string, double, Exception?> LogStage =
        LoggerMessage.Define<string, double>(
            LogLevel.Information,
            new EventId(4002, "CompilerStage"),
            "NetWasm: {Stage} ({Elapsed:F1}s elapsed)");

    private readonly ILogger<CompilerDiagnosticProgressReporter> _logger;
    private readonly TimeProvider _timeProvider;
    private long _startedAt;

    public CompilerDiagnosticProgressReporter(
        ILogger<CompilerDiagnosticProgressReporter> logger,
        TimeProvider timeProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _startedAt = _timeProvider.GetTimestamp();
    }

    public void Report(CompilerProgressStage stage)
    {
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
        if (stage == CompilerProgressStage.Start)
        {
            _startedAt = _timeProvider.GetTimestamp();
        }

        LogStage(_logger, message, _timeProvider.GetElapsedTime(_startedAt).TotalSeconds, null);
    }
}
