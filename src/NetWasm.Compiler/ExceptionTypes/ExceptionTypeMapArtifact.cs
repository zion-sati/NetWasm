namespace NetWasm.Compiler.ExceptionTypes;

public sealed record ExceptionTypeMapArtifact(
    byte[] Bytes,
    string MediaType,
    string FileNameHint,
    string Sha256);
