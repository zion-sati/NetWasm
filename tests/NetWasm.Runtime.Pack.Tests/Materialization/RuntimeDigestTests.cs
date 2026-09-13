using System.Security.Cryptography;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeDigestTests
{
    [Fact]
    public void VerifiesAndCalculatesSha256()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.WriteBytes("asset.a", [1, 2, 3]);
        var expected = Convert.ToHexString(SHA256.HashData([1, 2, 3])).ToLowerInvariant();

        var verifier = Assert.IsAssignableFrom<IRuntimeAssetDigestVerifier>(
            new RuntimeAssetDigestVerifier());
        verifier.Verify(path, expected.ToUpperInvariant());
        var calculator = Assert.IsAssignableFrom<IArtifactDigestCalculator>(
            new Sha256ArtifactDigestCalculator());
        Assert.Equal(expected, calculator.Calculate(path));
    }

    [Fact]
    public void RejectsMissingAndMismatchedAssets()
    {
        using var directory = new TemporaryDirectory();
        var verifier = new RuntimeAssetDigestVerifier();
        var missing = Assert.Throws<InvalidOperationException>(() =>
            verifier.Verify(directory.PathTo("missing.a"), RuntimePackTestData.Digest));
        Assert.Equal("A required NetWasm runtime pack asset is missing.", missing.Message);
        var path = directory.WriteBytes("asset.a", [1]);
        var mismatch = Assert.Throws<InvalidOperationException>(() =>
            verifier.Verify(path, RuntimePackTestData.Digest));
        Assert.Equal("A NetWasm runtime pack asset failed digest validation.", mismatch.Message);
    }

    [Theory]
    [InlineData("", "digest")]
    [InlineData("asset", "")]
    public void RejectsMissingDigestInputs(string path, string digest) =>
        Assert.Throws<ArgumentException>(() => new RuntimeAssetDigestVerifier().Verify(path, digest));

    [Fact]
    public void DigestCalculatorRejectsMissingPath() =>
        Assert.Throws<ArgumentException>(() => new Sha256ArtifactDigestCalculator().Calculate(" "));
}
