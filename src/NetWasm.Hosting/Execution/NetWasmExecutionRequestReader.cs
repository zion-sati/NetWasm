using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Hosting.Execution;

/// <summary>Decodes a strict transport-safe execution request without reading ambient state.</summary>
public sealed class NetWasmExecutionRequestReader : INetWasmExecutionRequestReader
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly INetWasmExecutionRequestValidator _validator;

    public NetWasmExecutionRequestReader(INetWasmExecutionRequestValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public NetWasmExecutionRequest Read(ReadOnlyMemory<byte> utf8Json)
    {
        var request = JsonSerializer.Deserialize<NetWasmExecutionRequest>(utf8Json.Span, JsonOptions)
            ?? throw new JsonException("A NetWasm execution request must be a JSON object.");
        _validator.Validate(request);
        return request;
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
        options.Converters.Add(new JsonStringEnumConverter<NetWasmPreopenAccess>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<NetWasmNetworkPolicy>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<NetWasmClock>(JsonNamingPolicy.CamelCase, false));
        return options;
    }
}
