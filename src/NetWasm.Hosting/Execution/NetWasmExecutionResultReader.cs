using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Hosting.Execution;

/// <summary>Decodes a strict transport-safe execution result.</summary>
public sealed class NetWasmExecutionResultReader : INetWasmExecutionResultReader
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly INetWasmExecutionResultValidator _validator;

    public NetWasmExecutionResultReader(INetWasmExecutionResultValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public NetWasmExecutionResult Read(ReadOnlyMemory<byte> utf8Json)
    {
        var result = JsonSerializer.Deserialize<NetWasmExecutionResult>(utf8Json.Span, JsonOptions)
            ?? throw new JsonException("A NetWasm execution result must be a JSON object.");
        _validator.Validate(result);
        return result;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
            AllowDuplicateProperties = false,
        };
        options.Converters.Add(new JsonStringEnumConverter<NetWasmCompletionKind>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<NetWasmFailurePhase>(JsonNamingPolicy.CamelCase, false));
        return options;
    }
}
