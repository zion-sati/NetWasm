namespace NetWasm.Compiler.Tests.Correctness;

public sealed class OracleRuntimeCapabilityVerifierTests
{
    private readonly IOracleRuntimeCapabilityVerifier _verifier = new OracleRuntimeCapabilityVerifier();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 7)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(1, 7)]
    [InlineData(3, 7)]
    [InlineData(7, 7)]
    public void AcceptsOnlyRequirementsProvidedByTheBackend(int required, int provided) =>
        _verifier.Verify((OracleRuntimeCapabilities)required, (OracleRuntimeCapabilities)provided);

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(4, 0)]
    [InlineData(7, 0)]
    [InlineData(7, 1)]
    [InlineData(7, 3)]
    [InlineData(1, 6)]
    [InlineData(8, 7)]
    public void RejectsMissingRequirementsIncludingUnrecognizedBits(int required, int provided)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _verifier.Verify((OracleRuntimeCapabilities)required, (OracleRuntimeCapabilities)provided));

        Assert.StartsWith("Oracle runtime lacks required capabilities:", exception.Message, StringComparison.Ordinal);
    }
}
