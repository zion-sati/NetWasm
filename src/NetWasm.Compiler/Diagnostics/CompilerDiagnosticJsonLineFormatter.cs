using System;
using System.Text.Json;
using System.Text.Json.Serialization;

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
                entry.ExceptionStackTrace),
            CompilerDiagnosticLineJsonContext.Default.CompilerDiagnosticFileEntry);
    }

    internal sealed record CompilerDiagnosticFileEntry(
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

[JsonSerializable(typeof(CompilerDiagnosticJsonLineFormatter.CompilerDiagnosticFileEntry))]
internal sealed partial class CompilerDiagnosticLineJsonContext : JsonSerializerContext;
