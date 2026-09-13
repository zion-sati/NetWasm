using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.StackTraces;

public sealed class StackTraceSymbolWriter(
    IArtifactDigestCalculator digests) : IStackTraceSymbolWriter
{
    public const string MediaType =
        "application/vnd.netwasm.stack-trace-symbols+json;version=1";
    public const string FileNameHint = "application.netwasm.stacktrace.json";

    public StackTraceSymbolArtifact Write(
        ImmutableArray<WasmStackTraceSymbol> symbols)
    {
        if (symbols.IsDefault)
        {
            throw new ArgumentException("Stack-trace symbols must be initialized.",
                nameof(symbols));
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteStartArray("methods");
            foreach (var symbol in symbols.OrderBy(symbol => symbol.Id))
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", symbol.Id);
                writer.WriteString("name", symbol.Name);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        var bytes = stream.ToArray();
        return new(bytes, MediaType, FileNameHint, digests.Calculate(bytes));
    }
}
