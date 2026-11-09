using NetWasm.Hosting.Execution;
using NetWasm.Hosting.Build.Deployment;

namespace NetWasm.Hosting.Build.Execution;

public sealed record LocalExecutionDescriptorBuildRequest(
    string OutputPath,
    string BuildFingerprint,
    string DeploymentManifestPath,
    string DeploymentManifestSha256,
    string HostingVersion,
    string HostExecutablePath,
    string LauncherPath);

public interface ILocalExecutionDescriptorBuildWriter
{
    void Write(LocalExecutionDescriptorBuildRequest request);
}

public sealed class LocalExecutionDescriptorBuildWriter(
    IBuildArtifactStore artifacts,
    IExecutionDescriptorWriter descriptors) : ILocalExecutionDescriptorBuildWriter
{
    private readonly IBuildArtifactStore _artifacts = artifacts ??
        throw new ArgumentNullException(nameof(artifacts));
    private readonly IExecutionDescriptorWriter _descriptors = descriptors ??
        throw new ArgumentNullException(nameof(descriptors));

    public void Write(LocalExecutionDescriptorBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var descriptor = new ExecutionDescriptor(
            1,
            request.BuildFingerprint,
            Path.GetFullPath(request.DeploymentManifestPath),
            request.DeploymentManifestSha256,
            request.HostingVersion,
            Path.GetFullPath(request.HostExecutablePath),
            Path.GetFullPath(request.LauncherPath));
        _artifacts.Write(Path.GetFullPath(request.OutputPath), _descriptors.Write(descriptor));
    }
}
