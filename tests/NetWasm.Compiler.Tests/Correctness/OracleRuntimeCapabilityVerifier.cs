namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class OracleRuntimeCapabilityVerifier : IOracleRuntimeCapabilityVerifier
{
    void IOracleRuntimeCapabilityVerifier.Verify(
        OracleRuntimeCapabilities required, OracleRuntimeCapabilities provided)
    {
        var missing = required & ~provided;
        if (missing != OracleRuntimeCapabilities.None)
        {
            throw new InvalidOperationException($"Oracle runtime lacks required capabilities: {missing}.");
        }
    }
}
