using Microsoft.Extensions.Logging;
using System;

namespace NetWasm.Compiler.Diagnostics;

internal sealed partial class CompilerDiagnosticProgressReporter(
    ILogger<CompilerDiagnosticProgressReporter> logger) : ICompilerProgressReporter
{
    private readonly ILogger<CompilerDiagnosticProgressReporter> _logger = logger ??
        throw new ArgumentNullException(nameof(logger));

    public void Report(CompilerProgressStage stage)
    {
        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        LogStageCompleted(_logger, stage);
    }

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Information,
        Message = "Compiler stage completed: {Stage}.")]
    private static partial void LogStageCompleted(
        ILogger logger,
        CompilerProgressStage stage);
}
