using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal interface IWitWorkerContractWriter
{
    void Write(string path, WitWorkerContract contract);
}

internal sealed class WitWorkerContractWriter(
    IByteArtifactWriter artifacts) : IWitWorkerContractWriter
{
    private readonly IByteArtifactWriter _artifacts = artifacts ??
        throw new ArgumentNullException(nameof(artifacts));

    public void Write(string path, WitWorkerContract contract)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contract);
        var serialized = JsonSerializer.SerializeToUtf8Bytes(contract, TaskJsonContext.CamelCaseMetadata.WitWorkerContract);
        var content = new byte[serialized.Length + 1];
        serialized.CopyTo(content, 0);
        content[^1] = (byte)'\n';
        _artifacts.Write(path, content);
    }
}
