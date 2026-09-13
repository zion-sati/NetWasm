namespace NetWasm.Compiler.StackTraces;

public sealed record StackTraceSymbolArtifact(
    byte[] Bytes,
    string MediaType,
    string FileNameHint,
    string Sha256);
