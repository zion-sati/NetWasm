using System.Collections.Immutable;
using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.Composition;
using NetWasm.Hosting.Build.Execution;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Build.MsBuild;

public sealed class NetWasmWriteExecutionRequestTask : Microsoft.Build.Utilities.Task
{
    private readonly IExecutionRequestBuildWriter _writer;

    public NetWasmWriteExecutionRequestTask()
        : this(HostingBuildComposition.CreateExecutionRequestBuildWriter())
    {
    }

    internal NetWasmWriteExecutionRequestTask(IExecutionRequestBuildWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    [Required] public string OutputPath { get; set; } = string.Empty;
    [Required] public string BuildFingerprint { get; set; } = string.Empty;
    [Required] public string DeploymentManifestSha256 { get; set; } = string.Empty;
    public ITaskItem[] Arguments { get; set; } = [];
    public ITaskItem[] EnvironmentVariables { get; set; } = [];
    public ITaskItem[] Preopens { get; set; } = [];
    public ITaskItem[] Clocks { get; set; } =
    [
        new Microsoft.Build.Utilities.TaskItem("wall"),
        new Microsoft.Build.Utilities.TaskItem("monotonic")
    ];
    public ITaskItem[] ApplicationImports { get; set; } = [];
    public string Network { get; set; } = "allowAll";
    public bool Randomness { get; set; } = true;

    public override bool Execute()
    {
        try
        {
            var environment = EnvironmentVariables.Select(item =>
                new NetWasmEnvironmentVariable(item.ItemSpec, item.GetMetadata("Value")))
                .ToImmutableArray();
            _writer.Write(OutputPath, new(
                1,
                BuildFingerprint,
                DeploymentManifestSha256,
                [.. Arguments.Select(item => item.ItemSpec)],
                environment,
                new(
                    [.. environment.Select(variable => variable.Name)],
                    [.. Preopens.Select(item => new NetWasmPreopenGrant(
                        Path.GetFullPath(item.ItemSpec),
                        item.GetMetadata("GuestPath"),
                        ParseAccess(item.GetMetadata("Access"))))],
                    ParseNetwork(Network),
                    [.. Clocks.Select(item => ParseClock(item.ItemSpec))],
                    Randomness),
                [.. ApplicationImports.Select(item => new NetWasmApplicationImport(
                    item.GetMetadata("Module"),
                    item.GetMetadata("ArtifactPath"),
                    item.GetMetadata("Sha256")))]));
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK046: {exception.Message}");
            return false;
        }
    }

    private static NetWasmNetworkPolicy ParseNetwork(string value) => value switch
    {
        "denyAll" => NetWasmNetworkPolicy.DenyAll,
        "allowAll" => NetWasmNetworkPolicy.AllowAll,
        _ => throw new ArgumentException("Network must be denyAll or allowAll."),
    };

    private static NetWasmPreopenAccess ParseAccess(string value) => value switch
    {
        "readOnly" => NetWasmPreopenAccess.ReadOnly,
        "readWrite" => NetWasmPreopenAccess.ReadWrite,
        _ => throw new ArgumentException("Preopen access must be readOnly or readWrite."),
    };

    private static NetWasmClock ParseClock(string value) => value switch
    {
        "monotonic" => NetWasmClock.Monotonic,
        "wall" => NetWasmClock.Wall,
        _ => throw new ArgumentException("Clock must be monotonic or wall."),
    };
}
