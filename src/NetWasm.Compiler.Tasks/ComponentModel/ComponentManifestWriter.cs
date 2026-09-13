using System.Text.Json;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal sealed class ComponentManifestWriter : IComponentManifestWriter
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
    };

    public void Write(ComponentManifestWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Path))!);
        File.WriteAllText(
            request.Path,
            JsonSerializer.Serialize(request.Manifest, WriteOptions));
    }
}
