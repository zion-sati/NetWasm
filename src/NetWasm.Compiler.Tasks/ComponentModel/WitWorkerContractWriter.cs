using System.Text.Encodings.Web;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.Tasks.Artifacts;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal interface IWitWorkerContractWriter
{
    void Write(string path, WitWorkerContract contract);
}

internal sealed class WitWorkerContractWriter(
    IByteArtifactWriter artifacts) : IWitWorkerContractWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly IByteArtifactWriter _artifacts = artifacts ??
        throw new ArgumentNullException(nameof(artifacts));

    public void Write(string path, WitWorkerContract contract)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contract);
        var serialized = JsonSerializer.SerializeToUtf8Bytes(contract, JsonOptions);
        var content = new byte[serialized.Length + 1];
        serialized.CopyTo(content, 0);
        content[^1] = (byte)'\n';
        _artifacts.Write(path, content);
    }
}
