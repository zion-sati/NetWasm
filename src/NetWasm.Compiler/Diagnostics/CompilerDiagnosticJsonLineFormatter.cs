using System;
using System.Text.Json;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticJsonLineFormatter : ICompilerDiagnosticLineFormatter
{
    public string Format(long sequence, DateTimeOffset timestampUtc, CompilerDiagnosticEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return JsonSerializer.Serialize(
            new CompilerDiagnosticFileEntry(
                sequence,
                timestampUtc,
                entry.Category,
                (int)entry.Level,
                entry.EventId.Id,
                entry.EventId.Name,
                entry.Message,
                entry.ExceptionType,
                entry.ExceptionMessage,
                entry.ExceptionStackTrace));
    }

    private sealed record CompilerDiagnosticFileEntry(
        long Sequence,
        DateTimeOffset TimestampUtc,
        string Category,
        int Level,
        int EventId,
        string? EventName,
        string Message,
        string? ExceptionType,
        string? ExceptionMessage,
        string? ExceptionStackTrace);
}
