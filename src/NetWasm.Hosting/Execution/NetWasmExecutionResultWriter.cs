using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Hosting.Execution;

/// <summary>Produces a deterministic transport-safe execution result as UTF-8 without BOM and with LF formatting.</summary>
public sealed class NetWasmExecutionResultWriter : INetWasmExecutionResultWriter
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly INetWasmExecutionResultValidator _validator;

    public NetWasmExecutionResultWriter(INetWasmExecutionResultValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public byte[] Write(NetWasmExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _validator.Validate(result);
        return JsonSerializer.SerializeToUtf8Bytes(result, JsonOptions);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            NewLine = "\n",
        };
        options.Converters.Add(new JsonStringEnumConverter<NetWasmCompletionKind>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<NetWasmFailurePhase>(JsonNamingPolicy.CamelCase, false));
        return options;
    }
}
