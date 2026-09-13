using Microsoft.Extensions.Logging;
using System;

namespace NetWasm.Compiler.Diagnostics;

internal sealed partial class CompilerDiagnosticCompilationDecorator(
    INetWasmCompiler compiler,
    ILogger<CompilerDiagnosticCompilationDecorator> logger,
    ICompilerDiagnosticLogPathResolver logPaths) : INetWasmCompiler
{
    private static readonly Action<ILogger, Core.WasmTarget, Exception?> LogStarted =
        LoggerMessage.Define<Core.WasmTarget>(
            LogLevel.Information,
            new EventId(4000, nameof(LogStarted)),
            "Compiler invocation started for {Target}.");

    private readonly INetWasmCompiler _compiler = compiler ??
        throw new ArgumentNullException(nameof(compiler));
    private readonly ILogger<CompilerDiagnosticCompilationDecorator> _logger = logger ??
        throw new ArgumentNullException(nameof(logger));
    private readonly ICompilerDiagnosticLogPathResolver _logPaths = logPaths ??
        throw new ArgumentNullException(nameof(logPaths));

    public CompilationResult Compile(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var scope = _logger.BeginScope(new CompilerDiagnosticScope(
            _logPaths.Resolve(options)));
        LogStarted(_logger, options.Target, null);
        try
        {
            return _compiler.Compile(options);
        }
        catch (Exception exception)
        {
            LogCompilationFailure(_logger, exception);
            throw;
        }
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Critical, Message = "Compilation failed.")]
    private static partial void LogCompilationFailure(
        ILogger logger,
        Exception exception);
}
