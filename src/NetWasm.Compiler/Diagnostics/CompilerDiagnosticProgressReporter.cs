using Microsoft.Extensions.Logging;
using System;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticProgressReporter(
    ILogger<CompilerDiagnosticProgressReporter> logger) : ICompilerProgressReporter
{
    private static readonly Action<ILogger, CompilerProgressStage, Exception?> LogStageCompleted =
        LoggerMessage.Define<CompilerProgressStage>(
            LogLevel.Information,
            new EventId(4002, nameof(LogStageCompleted)),
            "Compiler stage completed: {Stage}.");

    private readonly ILogger<CompilerDiagnosticProgressReporter> _logger = logger ??
        throw new ArgumentNullException(nameof(logger));

    public void Report(CompilerProgressStage stage)
    {
        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        LogStageCompleted(_logger, stage, null);
    }
}
