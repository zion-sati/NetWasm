using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace NetWasm.Hosting.Execution;

/// <summary>Produces deterministic UTF-8 without BOM, with LF formatting and ordered package identities.</summary>
public sealed class ExecutionDescriptorWriter : IExecutionDescriptorWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
    };

    private readonly IExecutionDescriptorValidator _validator;

    public ExecutionDescriptorWriter(IExecutionDescriptorValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public byte[] Write(ExecutionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _validator.Validate(descriptor);
        var ordered = descriptor with
        {
            ToolPackages = descriptor.ToolPackages.OrderBy(package => package.Id, StringComparer.Ordinal).ToImmutableArray(),
        };
        return JsonSerializer.SerializeToUtf8Bytes(ordered, JsonOptions);
    }
}
