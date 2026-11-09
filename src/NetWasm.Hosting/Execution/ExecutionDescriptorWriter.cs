using System;
using System.Text.Json;
using NetWasm.Hosting;

namespace NetWasm.Hosting.Execution;

/// <summary>Produces deterministic UTF-8 without BOM, with LF formatting.</summary>
public sealed class ExecutionDescriptorWriter : IExecutionDescriptorWriter
{
    private static readonly HostingJsonContext Json = new(new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
    });

    private readonly IExecutionDescriptorValidator _validator;

    public ExecutionDescriptorWriter(IExecutionDescriptorValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public byte[] Write(ExecutionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _validator.Validate(descriptor);
        return JsonSerializer.SerializeToUtf8Bytes(descriptor, Json.ExecutionDescriptor);
    }
}
