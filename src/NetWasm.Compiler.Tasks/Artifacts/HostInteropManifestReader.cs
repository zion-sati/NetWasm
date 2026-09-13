using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed class HostInteropManifestReader : IHostInteropManifestReader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public HostInteropManifest Read(string path, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var interop = JsonSerializer.Deserialize<HostInteropManifest>(
            File.ReadAllText(path),
            ReadOptions) ?? throw new InvalidOperationException(
                "The NetWasm interop manifest is empty.");
        if (!string.Equals(interop.Target, target, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The NetWasm interop manifest target does not match the requested target.");
        }
        return interop;
    }
}
