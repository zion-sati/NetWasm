using NetWasm.Hosting.Build.Deployment;
using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Build.Execution;

public interface IExecutionRequestBuildWriter
{
    void Write(string outputPath, NetWasmExecutionRequest request);
}

public sealed class ExecutionRequestBuildWriter(
    IBuildArtifactStore artifacts,
    INetWasmExecutionRequestWriter requests) : IExecutionRequestBuildWriter
{
    private readonly IBuildArtifactStore _artifacts = artifacts ??
        throw new ArgumentNullException(nameof(artifacts));
    private readonly INetWasmExecutionRequestWriter _requests = requests ??
        throw new ArgumentNullException(nameof(requests));

    public void Write(string outputPath, NetWasmExecutionRequest request) =>
        _artifacts.Write(Path.GetFullPath(outputPath), _requests.Write(request));
}
