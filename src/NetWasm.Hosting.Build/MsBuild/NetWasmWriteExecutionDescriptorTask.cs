using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.Composition;
using NetWasm.Hosting.Build.Execution;

namespace NetWasm.Hosting.Build.MsBuild;

public sealed class NetWasmWriteExecutionDescriptorTask : Microsoft.Build.Utilities.Task
{
    private readonly ILocalExecutionDescriptorBuildWriter _writer;

    public NetWasmWriteExecutionDescriptorTask()
        : this(HostingBuildComposition.CreateLocalExecutionDescriptorBuildWriter())
    {
    }

    internal NetWasmWriteExecutionDescriptorTask(
        ILocalExecutionDescriptorBuildWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    [Required] public string OutputPath { get; set; } = string.Empty;
    [Required] public string BuildFingerprint { get; set; } = string.Empty;
    [Required] public string DeploymentManifestPath { get; set; } = string.Empty;
    [Required] public string DeploymentManifestSha256 { get; set; } = string.Empty;
    [Required] public string HostingVersion { get; set; } = string.Empty;
    [Required] public string HostExecutablePath { get; set; } = string.Empty;
    [Required] public string LauncherPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            _writer.Write(new(
                Path.GetFullPath(OutputPath),
                BuildFingerprint,
                Path.GetFullPath(DeploymentManifestPath),
                DeploymentManifestSha256,
                HostingVersion,
                Path.GetFullPath(HostExecutablePath),
                Path.GetFullPath(LauncherPath)));
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK044: {exception.Message}");
            return false;
        }
    }
}
