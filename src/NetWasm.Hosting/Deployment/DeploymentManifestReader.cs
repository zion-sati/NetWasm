using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Hosting.Deployment;

/// <summary>Decodes a strict publish-safe deployment manifest without accessing files or tools.</summary>
public sealed class DeploymentManifestReader : IDeploymentManifestReader
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly IDeploymentManifestValidator _validator;

    public DeploymentManifestReader(IDeploymentManifestValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public DeploymentManifest Read(ReadOnlyMemory<byte> utf8Json)
    {
        var manifest = JsonSerializer.Deserialize<DeploymentManifest>(utf8Json.Span, JsonOptions)
            ?? throw new JsonException("A NetWasm deployment manifest must be a JSON object.");
        _validator.Validate(manifest);
        return manifest;
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
        options.Converters.Add(new JsonStringEnumConverter<DeploymentKind>(JsonNamingPolicy.CamelCase, false));
        return options;
    }
}
