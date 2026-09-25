namespace NetWasm.Compiler.Tests.Correctness;

internal interface IOracleRuntimeCapabilityVerifier
{
    void Verify(OracleRuntimeCapabilities required, OracleRuntimeCapabilities provided);
}
