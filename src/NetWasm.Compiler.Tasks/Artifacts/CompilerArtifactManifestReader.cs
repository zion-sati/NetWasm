using System.Text.Json;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed class CompilerArtifactManifestReader : ICompilerArtifactManifestReader
{
    public CompilerArtifactManifest Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var manifest = JsonSerializer.Deserialize(File.ReadAllText(path), CompilerArtifactReadJsonContext.Default.CompilerArtifactManifest);
        return manifest ?? throw new InvalidOperationException("NetWasm artifact manifest is empty.");
    }
}
