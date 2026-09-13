namespace NetWasm.Compiler.ExceptionTypes;

public sealed record DiagnosticArtifactBindingManifest(
    int SchemaVersion,
    string BuildId,
    string WasmSha256,
    string ExceptionTypeMapSha256);
