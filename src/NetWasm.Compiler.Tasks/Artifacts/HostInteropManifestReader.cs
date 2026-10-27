using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed class HostInteropManifestReader : IHostInteropManifestReader
{
    public HostInteropManifest Read(string path, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var interop = JsonSerializer.Deserialize(
            File.ReadAllText(path),
            CompilerArtifactReadJsonContext.Default.HostInteropManifest) ?? throw new InvalidOperationException(
                "The NetWasm interop manifest is empty.");
        if (!string.Equals(interop.Target, target, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The NetWasm interop manifest target does not match the requested target.");
        }
        return interop;
    }
}
