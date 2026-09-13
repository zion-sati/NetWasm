using System.Collections.Immutable;
using NetWasm.Hosting.Deployment;
using NetWasm.Hosting.Execution;
using NetWasm.Hosting.Build.Deployment;

namespace NetWasm.Hosting.Build.Execution;

public sealed record ExecutionToolPackageSource(
    string Id,
    string Version,
    string RootPath,
    string ArchivePath);

public sealed record LocalExecutionDescriptorBuildRequest(
    string OutputPath,
    string BuildFingerprint,
    string DeploymentManifestPath,
    string DeploymentManifestSha256,
    string HostingVersion,
    string HostExecutablePath,
    string LauncherPath,
    ImmutableArray<ExecutionToolPackageSource> ToolPackages);

public interface ILocalExecutionDescriptorBuildWriter
{
    void Write(LocalExecutionDescriptorBuildRequest request);
}

public sealed class LocalExecutionDescriptorBuildWriter(
    IBuildArtifactStore artifacts,
    IContentHasher hashes,
    IExecutionDescriptorWriter descriptors) : ILocalExecutionDescriptorBuildWriter
{
    private readonly IBuildArtifactStore _artifacts = artifacts ??
        throw new ArgumentNullException(nameof(artifacts));
    private readonly IContentHasher _hashes = hashes ??
        throw new ArgumentNullException(nameof(hashes));
    private readonly IExecutionDescriptorWriter _descriptors = descriptors ??
        throw new ArgumentNullException(nameof(descriptors));

    public void Write(LocalExecutionDescriptorBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ToolPackages.IsDefault)
        {
            throw new ArgumentException("Execution tool packages must be explicit.", nameof(request));
        }
        var packages = request.ToolPackages.Select(package =>
        {
            ArgumentNullException.ThrowIfNull(package);
            return new ExecutionToolPackage(
                package.Id,
                package.Version,
                Path.GetFullPath(package.RootPath),
                _hashes.Hash(_artifacts.Read(package.ArchivePath)));
        }).ToImmutableArray();
        var descriptor = new ExecutionDescriptor(
            1,
            request.BuildFingerprint,
            Path.GetFullPath(request.DeploymentManifestPath),
            request.DeploymentManifestSha256,
            request.HostingVersion,
            Path.GetFullPath(request.HostExecutablePath),
            Path.GetFullPath(request.LauncherPath),
            packages);
        _artifacts.Write(Path.GetFullPath(request.OutputPath), _descriptors.Write(descriptor));
    }
}
