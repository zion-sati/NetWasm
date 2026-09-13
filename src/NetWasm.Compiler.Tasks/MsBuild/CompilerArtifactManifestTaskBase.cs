using System.Collections.Immutable;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using MsBuildTask = Microsoft.Build.Utilities.Task;
using NetWasm.Compiler.Tasks.Artifacts;

namespace NetWasm.Compiler.Tasks.MsBuild;

public abstract class CompilerArtifactManifestTaskBase : MsBuildTask
{
    [Required]
    public string InputAssemblyPath { get; set; } = string.Empty;

    [Required]
    public string ArtifactManifestPath { get; set; } = string.Empty;

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    public string Profile { get; set; } = "netwasm0.1";
    public string Target { get; set; } = "wasm32";
    public string FeatureSet { get; set; } = "none";
    public string SdkVersion { get; set; } = "unknown";
    public string CompilerVersion { get; set; } = "unknown";
    public string RuntimeAbiVersion { get; set; } = "unknown";
    public string RuntimeVersion { get; set; } = "unknown";
    public ITaskItem[] References { get; set; } = [];
    public ITaskItem[] Sources { get; set; } = [];
    public string? GeneratedSourceRoot { get; set; }
    public string? RuntimeAbiManifestPath { get; set; }
    public ITaskItem[] Artifacts { get; set; } = [];

    [Output]
    public ITaskItem[] ResolvedArtifacts { get; protected set; } = [];

    [Output]
    public string SemanticBuildId { get; protected set; } = string.Empty;

    internal CompilerArtifactManifestTaskInput CreateManifestTaskInput() => new(
        ArtifactManifestPath,
        ProjectDirectory,
        Profile,
        Target,
        FeatureSet,
        SdkVersion,
        CompilerVersion,
        RuntimeAbiVersion,
        RuntimeVersion,
        InputAssemblyPath,
        References.ToImmutableArray(),
        Sources.ToImmutableArray(),
        RuntimeAbiManifestPath,
        Artifacts.ToImmutableArray(),
        GeneratedSourceRoot);

    internal void SetResolvedArtifacts(CompilerArtifactManifestBuildResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        SemanticBuildId = result.Manifest.SemanticBuildId;
        ResolvedArtifacts = result.Artifacts
            .Select(static result => CreateTaskItem(result))
            .ToArray();
    }

    private static TaskItem CreateTaskItem(CompilerArtifactResult result)
    {
        var item = new TaskItem(result.FullPath);
        var artifact = result.ManifestArtifact;
        item.SetMetadata("Kind", artifact.Kind);
        item.SetMetadata("ManifestPath", artifact.Path);
        item.SetMetadata("RelativePath", Path.GetFileName(result.FullPath));
        item.SetMetadata("MediaType", artifact.MediaType);
        item.SetMetadata("Digest", artifact.Sha256);
        item.SetMetadata("SchemaVersion", artifact.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        item.SetMetadata("WasmTarget", artifact.Target);
        item.SetMetadata("Profile", artifact.Profile);
        item.SetMetadata("SemanticBuildId", artifact.SemanticBuildId);
        item.SetMetadata("CopyToPublishDirectory", "PreserveNewest");
        return item;
    }
}
