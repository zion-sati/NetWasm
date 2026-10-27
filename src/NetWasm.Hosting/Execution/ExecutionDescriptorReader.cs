using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetWasm.Hosting;

namespace NetWasm.Hosting.Execution;

/// <summary>Decodes the strict versioned descriptor without accessing local files or tools.</summary>
public sealed class ExecutionDescriptorReader : IExecutionDescriptorReader
{
    private static readonly HostingJsonContext Json = new(new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        AllowDuplicateProperties = false,
    });

    private readonly IExecutionDescriptorValidator _validator;

    public ExecutionDescriptorReader(IExecutionDescriptorValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public ExecutionDescriptor Read(ReadOnlyMemory<byte> utf8Json)
    {
        var descriptor = JsonSerializer.Deserialize(utf8Json.Span, Json.ExecutionDescriptor)
            ?? throw new JsonException("A NetWasm execution descriptor must be a JSON object.");
        _validator.Validate(descriptor);
        return descriptor;
    }
}
