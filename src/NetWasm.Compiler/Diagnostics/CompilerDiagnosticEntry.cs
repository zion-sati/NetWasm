using Microsoft.Extensions.Logging;

namespace NetWasm.Compiler.Diagnostics;

internal sealed record CompilerDiagnosticEntry(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    string? ExceptionType,
    string? ExceptionMessage,
    string? ExceptionStackTrace);
