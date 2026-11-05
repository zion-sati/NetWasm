using Microsoft.Extensions.Logging;
using System;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticCompilationDecorator(
    INetWasmCompiler compiler,
    ILogger<CompilerDiagnosticCompilationDecorator> logger,
    ICompilerDiagnosticLogPathResolver logPaths) : INetWasmCompiler
{
    private static readonly Action<ILogger, Core.WasmTarget, Exception?> LogStarted =
        LoggerMessage.Define<Core.WasmTarget>(
            LogLevel.Information,
            new EventId(4000, nameof(LogStarted)),
            "Compiler invocation started for {Target}.");
    private static readonly Action<ILogger, Exception?> LogCompilationFailure =
        LoggerMessage.Define(
            LogLevel.Critical,
            new EventId(4001, nameof(LogCompilationFailure)),
            "Compilation failed.");

    private readonly INetWasmCompiler _compiler = compiler ??
        throw new ArgumentNullException(nameof(compiler));
    private readonly ILogger<CompilerDiagnosticCompilationDecorator> _logger = logger ??
        throw new ArgumentNullException(nameof(logger));
    private readonly ICompilerDiagnosticLogPathResolver _logPaths = logPaths ??
        throw new ArgumentNullException(nameof(logPaths));

    public CompilationResult Compile(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Logged(_logger, _logPaths, options, () => _compiler.Compile(options));
    }

    // A compile within the diagnostic log's scope, its start and any failure logged.
    internal static T Logged<T>(ILogger<CompilerDiagnosticCompilationDecorator> logger,
        ICompilerDiagnosticLogPathResolver logPaths, CompilerOptions options, Func<T> compile)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(logPaths);
        ArgumentNullException.ThrowIfNull(compile);
        using var scope = logger.BeginScope(new CompilerDiagnosticScope(logPaths.Resolve(options)));
        LogStarted(logger, options.Target, null);
        try
        {
            return compile();
        }
        catch (Exception exception)
        {
            LogCompilationFailure(logger, exception);
            throw;
        }
    }
}
