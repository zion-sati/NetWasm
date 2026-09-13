using System.Collections.Immutable;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Build.Deployment;

public sealed record DeploymentArtifactSource(
    string SourcePath,
    string RelativePath,
    string Role,
    string MediaType,
    int? SchemaVersion);

public sealed record DeploymentBuildRequest(
    string ManifestPath,
    string DeploymentRoot,
    string SemanticBuildId,
    DeploymentKind DeploymentKind,
    string Profile,
    string Target,
    string FeatureSet,
    string ExecutionContract,
    DeploymentVersions Versions,
    ImmutableArray<string> RuntimeFeatures,
    ImmutableArray<DeploymentArtifactSource> Artifacts,
    ImmutableArray<string> RequiredImportModules,
    ImmutableArray<DeploymentFunction> RequiredImports,
    ImmutableArray<DeploymentFunction> Exports);

public sealed record DeploymentBuildResult(
    DeploymentManifest Manifest,
    string ManifestSha256);

public interface IDeploymentBuildWriter
{
    DeploymentBuildResult Write(DeploymentBuildRequest request);
}

public sealed class DeploymentBuildWriter(
    IBuildArtifactStore artifacts,
    IContentHasher hashes,
    IDeploymentManifestWriter manifests) : IDeploymentBuildWriter
{
    private readonly IBuildArtifactStore _artifacts = artifacts ??
        throw new ArgumentNullException(nameof(artifacts));
    private readonly IContentHasher _hashes = hashes ??
        throw new ArgumentNullException(nameof(hashes));
    private readonly IDeploymentManifestWriter _manifests = manifests ??
        throw new ArgumentNullException(nameof(manifests));

    public DeploymentBuildResult Write(DeploymentBuildRequest request)
    {
        Validate(request);
        _artifacts.Delete(request.ManifestPath);

        var staged = request.Artifacts.Select(source =>
        {
            ArgumentNullException.ThrowIfNull(source);
            var bytes = _artifacts.Read(source.SourcePath);
            return new StagedArtifact(
                bytes,
                new(
                    source.RelativePath,
                    source.Role,
                    source.MediaType,
                    _hashes.Hash(bytes),
                    source.SchemaVersion));
        }).ToImmutableArray();

        var provisional = CreateManifest(
            request,
            new string('0', 64),
            [.. staged.Select(item => item.Artifact)]);
        var fingerprint = _hashes.Hash(_manifests.Write(provisional));
        var manifest = CreateManifest(
            request,
            fingerprint,
            [.. staged.Select(item => item.Artifact)]);
        var manifestBytes = _manifests.Write(manifest);

        foreach (var item in staged)
        {
            _artifacts.Write(
                ResolveDeploymentPath(request.DeploymentRoot, item.Artifact.RelativePath),
                item.Bytes);
        }
        _artifacts.Write(request.ManifestPath, manifestBytes);
        return new(manifest, _hashes.Hash(manifestBytes));
    }

    private static DeploymentManifest CreateManifest(
        DeploymentBuildRequest request,
        string buildFingerprint,
        ImmutableArray<DeploymentArtifact> artifacts) => new(
            1,
            request.SemanticBuildId,
            request.DeploymentKind,
            request.Profile,
            request.Target,
            request.FeatureSet,
            request.ExecutionContract,
            request.Versions,
            buildFingerprint,
            request.RuntimeFeatures,
            artifacts,
            request.RequiredImportModules,
            request.RequiredImports,
            request.Exports);

    private static void Validate(DeploymentBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAbsolutePath(request.ManifestPath, nameof(request));
        ValidateAbsolutePath(request.DeploymentRoot, nameof(request));
        if (!IsContained(request.DeploymentRoot, request.ManifestPath))
        {
            throw new ArgumentException(
                "The deployment manifest must be contained by the deployment root.",
                nameof(request));
        }
        if (request.Artifacts.IsDefault)
        {
            throw new ArgumentException("Deployment artifact sources must be explicit.", nameof(request));
        }
        foreach (var source in request.Artifacts)
        {
            ArgumentNullException.ThrowIfNull(source);
            ValidateAbsolutePath(source.SourcePath, nameof(request));
        }
    }

    private static string ResolveDeploymentPath(string root, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsContained(root, path))
        {
            throw new ArgumentException("Deployment artifact path escapes its root.", nameof(relativePath));
        }
        return path;
    }

    private static bool IsContained(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var canonicalPath = Path.GetFullPath(path);
        return canonicalPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static void ValidateAbsolutePath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Deployment build paths must be absolute.", parameterName);
        }
    }

    private sealed record StagedArtifact(byte[] Bytes, DeploymentArtifact Artifact);
}
