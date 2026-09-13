namespace NetWasm.Sdk.Pack.Diagnostics;

public interface IPackDiagnosticWriter
{
    void Write(string? outputPath, NetWasmPackErrorCode code, string message);
}
