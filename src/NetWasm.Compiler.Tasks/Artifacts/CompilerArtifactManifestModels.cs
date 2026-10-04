using System.Collections.Immutable;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal static class CompilerArtifactKinds
{
    public const string NativeCallbackSupportObject =
        "NativeCallbackSupportObject";
}

internal sealed record CompilerArtifactInputRequest(string Kind, string Path);

internal sealed record CompilerArtifactOutputRequest(
    string Kind,
    string MediaType,
    string Path,
    string CopyToPublishDirectory = "PreserveNewest");

internal sealed record CompilerArtifactManifestBuildRequest(
    string ManifestPath,
    string ProjectDirectory,
    string Profile,
    string Target,
    string FeatureSet,
    string SdkVersion,
    string CompilerVersion,
    string RuntimeAbiVersion,
    string RuntimeVersion,
    ImmutableArray<CompilerArtifactInputRequest> Inputs,
    ImmutableArray<CompilerArtifactOutputRequest> Outputs);

internal sealed record CompilerArtifactManifestInput(
    string Kind,
    string Path,
    string Sha256);

internal sealed record CompilerArtifactManifestArtifact(
    string Kind,
    string Path,
    string MediaType,
    string Sha256,
    int SchemaVersion,
    string Target,
    string Profile,
    string SemanticBuildId);

internal sealed record CompilerArtifactManifest(
    int SchemaVersion,
    string SemanticBuildId,
    string Profile,
    string Target,
    string FeatureSet,
    string SdkVersion,
    string CompilerVersion,
    string RuntimeAbiVersion,
    string RuntimeVersion,
    ImmutableArray<CompilerArtifactManifestInput> Inputs,
    ImmutableArray<CompilerArtifactManifestArtifact> Artifacts);

internal sealed record CompilerArtifactResult(
    string FullPath,
    CompilerArtifactManifestArtifact ManifestArtifact,
    string CopyToPublishDirectory = "PreserveNewest");

internal sealed record CompilerArtifactManifestBuildResult(
    CompilerArtifactManifest Manifest,
    ImmutableArray<CompilerArtifactResult> Artifacts);

internal sealed record CompilerArtifactManifestWriteRequest(
    string Path,
    CompilerArtifactManifest Manifest);

internal sealed record CompilerArtifactManifestValidationRequest(
    CompilerArtifactManifestBuildRequest BuildRequest,
    string ManifestPath);
