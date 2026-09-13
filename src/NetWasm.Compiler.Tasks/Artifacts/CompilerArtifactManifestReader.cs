using System.Text.Json;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed class CompilerArtifactManifestReader : ICompilerArtifactManifestReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public CompilerArtifactManifest Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var manifest = JsonSerializer.Deserialize<CompilerArtifactManifest>(File.ReadAllText(path), JsonOptions);
        return manifest ?? throw new InvalidOperationException("NetWasm artifact manifest is empty.");
    }
}
