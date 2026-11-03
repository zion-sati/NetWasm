namespace NetWasm.Compiler.Tasks.Artifacts;

// A manifest that no longer describes the build's inputs and outputs, and the first difference,
// named by the manifest's project-relative paths and settings.
internal sealed class StaleCompilerArtifactManifestException(string reason)
    : InvalidOperationException($"NetWasm artifact manifest is stale: {reason}.")
{
    public string Reason { get; } = reason;
}
