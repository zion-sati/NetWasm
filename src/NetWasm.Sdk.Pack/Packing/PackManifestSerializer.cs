using System.Text.Json;

namespace NetWasm.Sdk.Pack.Packing;

public sealed class PackManifestSerializer : IPackManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NewLine = "\n",
    };

    public byte[] Serialize(CanonicalPackManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
    }
}
