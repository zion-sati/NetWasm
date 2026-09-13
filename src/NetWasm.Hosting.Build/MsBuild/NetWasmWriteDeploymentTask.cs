using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.Composition;
using NetWasm.Hosting.Build.Deployment;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Build.MsBuild;

public sealed class NetWasmWriteDeploymentTask : Microsoft.Build.Utilities.Task
{
    private readonly IDeploymentBuildWriter _writer;

    public NetWasmWriteDeploymentTask()
        : this(HostingBuildComposition.CreateDeploymentBuildWriter())
    {
    }

    internal NetWasmWriteDeploymentTask(IDeploymentBuildWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    [Required] public string ManifestPath { get; set; } = string.Empty;
    [Required] public string DeploymentRoot { get; set; } = string.Empty;
    [Required] public string SemanticBuildId { get; set; } = string.Empty;
    [Required] public string DeploymentKind { get; set; } = string.Empty;
    [Required] public string Profile { get; set; } = string.Empty;
    [Required] public string Target { get; set; } = string.Empty;
    [Required] public string FeatureSet { get; set; } = string.Empty;
    [Required] public string ExecutionContract { get; set; } = string.Empty;
    [Required] public string SdkVersion { get; set; } = string.Empty;
    [Required] public string CompilerVersion { get; set; } = string.Empty;
    [Required] public string RuntimeVersion { get; set; } = string.Empty;
    [Required] public string RuntimeAbiVersion { get; set; } = string.Empty;
    [Required] public string HostingVersion { get; set; } = string.Empty;
    [Required] public string ToolchainVersion { get; set; } = string.Empty;
    [Required] public ITaskItem[] Artifacts { get; set; } = [];
    public ITaskItem[] RuntimeFeatures { get; set; } = [];
    public ITaskItem[] RequiredImportModules { get; set; } = [];
    public ITaskItem[] RequiredImports { get; set; } = [];
    public ITaskItem[] Exports { get; set; } = [];

    [Output] public string BuildFingerprint { get; private set; } = string.Empty;
    [Output] public string ManifestSha256 { get; private set; } = string.Empty;

    public override bool Execute()
    {
        BuildFingerprint = string.Empty;
        ManifestSha256 = string.Empty;
        try
        {
            var result = _writer.Write(new(
                Path.GetFullPath(ManifestPath),
                Path.GetFullPath(DeploymentRoot),
                SemanticBuildId,
                ParseKind(DeploymentKind),
                Profile,
                Target,
                FeatureSet,
                ExecutionContract,
                new(SdkVersion, CompilerVersion, RuntimeVersion, RuntimeAbiVersion,
                    HostingVersion, ToolchainVersion),
                [.. RuntimeFeatures.Select(item => item.ItemSpec)],
                [.. Artifacts.Select(CreateArtifact)],
                [.. RequiredImportModules.Select(item => item.ItemSpec)],
                [.. RequiredImports.Select(CreateFunction)],
                [.. Exports.Select(CreateFunction)]));
            BuildFingerprint = result.Manifest.BuildFingerprint;
            ManifestSha256 = result.ManifestSha256;
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK043: {exception.Message}");
            return false;
        }
    }

    private static DeploymentKind ParseKind(string value) => value switch
    {
        "component" => Hosting.Deployment.DeploymentKind.Component,
        "raw" => Hosting.Deployment.DeploymentKind.Raw,
        "browser" => Hosting.Deployment.DeploymentKind.Browser,
        _ => throw new ArgumentException("DeploymentKind must be component, raw, or browser."),
    };

    private static DeploymentArtifactSource CreateArtifact(ITaskItem item)
    {
        var schemaText = item.GetMetadata("SchemaVersion");
        return new(
            Path.GetFullPath(item.ItemSpec),
            item.GetMetadata("RelativePath"),
            item.GetMetadata("Role"),
            item.GetMetadata("MediaType"),
            string.IsNullOrWhiteSpace(schemaText)
                ? null
                : int.Parse(schemaText, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static DeploymentFunction CreateFunction(ITaskItem item) => new(
        item.GetMetadata("Interface"),
        item.GetMetadata("Name"),
        ReadSignature(item.GetMetadata("Parameters")),
        ReadSignature(item.GetMetadata("Results")));

    private static ImmutableArray<string> ReadSignature(string json)
    {
        var values = JsonSerializer.Deserialize<string[]>(json) ??
            throw new ArgumentException("Deployment function signatures must be JSON arrays.");
        if (values.Any(value => value is null))
        {
            throw new ArgumentException("Deployment function signatures cannot contain null.");
        }
        return [.. values];
    }
}
