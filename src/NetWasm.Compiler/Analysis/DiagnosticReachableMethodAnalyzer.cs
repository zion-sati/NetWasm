using Microsoft.Extensions.Logging;
using System;

namespace NetWasm.Compiler.Analysis;

internal sealed class DiagnosticReachableMethodAnalyzer(
    IReachableMethodAnalyzer inner,
    ILogger<DiagnosticReachableMethodAnalyzer> logger) : IReachableMethodAnalyzer
{
    private readonly IReachableMethodAnalyzer _inner = inner ??
        throw new ArgumentNullException(nameof(inner));
    private readonly ILogger<DiagnosticReachableMethodAnalyzer> _logger = logger ??
        throw new ArgumentNullException(nameof(logger));

    private static readonly Action<ILogger, string, Exception?> LogFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, nameof(LogFailure)),
            "Managed method analysis failed for {Method}.");

    public ReachableMethodAnalysis Analyze(ReachableMethodRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return _inner.Analyze(request);
        }
        catch (Exception exception)
        {
            LogFailure(_logger, request.Method.ToString(), exception);
            throw;
        }
    }
}
