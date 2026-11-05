namespace NetWasm.Compiler.ExceptionTypes;

public sealed record DiagnosticArtifactBundle(
    byte[] Wasm,
    ExceptionTypeMapArtifact ExceptionTypeMap,
    byte[] ManifestBytes,
    DiagnosticArtifactBindingManifest Manifest)
{
    // The exception types the map names, as planned from the compile's layouts.
    public System.Collections.Immutable.ImmutableArray<ReachableExceptionType> ExceptionTypes { get; init; } = [];
}
