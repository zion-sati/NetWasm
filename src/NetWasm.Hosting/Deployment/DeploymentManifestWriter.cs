using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Hosting.Deployment;

/// <summary>Produces a canonical publish-safe deployment manifest as UTF-8 without BOM and with LF formatting.</summary>
public sealed class DeploymentManifestWriter : IDeploymentManifestWriter
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly IDeploymentManifestValidator _validator;

    public DeploymentManifestWriter(IDeploymentManifestValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public byte[] Write(DeploymentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        _validator.Validate(manifest);
        var canonical = manifest with
        {
            RuntimeFeatures = manifest.RuntimeFeatures.Order(StringComparer.Ordinal).ToImmutableArray(),
            Artifacts = manifest.Artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToImmutableArray(),
            RequiredImportModules = manifest.RequiredImportModules.Order(StringComparer.Ordinal).ToImmutableArray(),
            RequiredImports = OrderFunctions(manifest.RequiredImports),
            Exports = OrderFunctions(manifest.Exports),
        };
        return JsonSerializer.SerializeToUtf8Bytes(canonical, JsonOptions);
    }

    private static ImmutableArray<DeploymentFunction> OrderFunctions(ImmutableArray<DeploymentFunction> functions) =>
        functions.OrderBy(function => function.Interface, StringComparer.Ordinal)
            .ThenBy(function => function.Name, StringComparer.Ordinal)
            .ToImmutableArray();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            NewLine = "\n",
        };
        options.Converters.Add(new JsonStringEnumConverter<DeploymentKind>(JsonNamingPolicy.CamelCase, false));
        return options;
    }
}
