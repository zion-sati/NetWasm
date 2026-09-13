using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Hosting.Execution;

/// <summary>Produces deterministic ephemeral request JSON while preserving argument order.</summary>
public sealed class NetWasmExecutionRequestWriter : INetWasmExecutionRequestWriter
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly INetWasmExecutionRequestValidator _validator;

    public NetWasmExecutionRequestWriter(INetWasmExecutionRequestValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public byte[] Write(NetWasmExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _validator.Validate(request);
        var canonical = request with
        {
            Environment = request.Environment.OrderBy(variable => variable.Name, StringComparer.Ordinal).ToImmutableArray(),
            Grants = request.Grants with
            {
                Environment = request.Grants.Environment.Order(StringComparer.Ordinal).ToImmutableArray(),
                Preopens = request.Grants.Preopens.OrderBy(preopen => preopen.GuestPath, StringComparer.Ordinal)
                    .ThenBy(preopen => preopen.HostPath, StringComparer.Ordinal).ToImmutableArray(),
                Clocks = request.Grants.Clocks.Order().ToImmutableArray(),
            },
            ApplicationImports = request.ApplicationImports.OrderBy(import => import.Module, StringComparer.Ordinal).ToImmutableArray(),
        };
        return JsonSerializer.SerializeToUtf8Bytes(canonical, JsonOptions);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            NewLine = "\n",
        };
        options.Converters.Add(new JsonStringEnumConverter<NetWasmPreopenAccess>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<NetWasmNetworkPolicy>(JsonNamingPolicy.CamelCase, false));
        options.Converters.Add(new JsonStringEnumConverter<NetWasmClock>(JsonNamingPolicy.CamelCase, false));
        return options;
    }
}
