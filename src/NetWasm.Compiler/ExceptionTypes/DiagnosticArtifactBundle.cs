namespace NetWasm.Compiler.ExceptionTypes;

public sealed record DiagnosticArtifactBundle(
    byte[] Wasm,
    ExceptionTypeMapArtifact ExceptionTypeMap,
    byte[] ManifestBytes,
    DiagnosticArtifactBindingManifest Manifest);
