namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusArtifactFingerprintTests
{
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void ComputeReturnsLowercaseSha256OfExactBytes(string content)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(content));
            var fingerprint = Assert.IsAssignableFrom<ICorpusArtifactFingerprint>(
                new CorpusArtifactFingerprint());
            var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(content)));
            Assert.Equal(expected, fingerprint.Compute(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ComputeRejectsInvalidPathsAndPreservesReadFailure()
    {
        var fingerprint = Assert.IsAssignableFrom<ICorpusArtifactFingerprint>(
            new CorpusArtifactFingerprint());
        Assert.Throws<ArgumentNullException>(() => fingerprint.Compute(null!));
        Assert.Throws<ArgumentException>(() => fingerprint.Compute(" "));
        Assert.Throws<FileNotFoundException>(() => fingerprint.Compute("missing-artifact.bin"));
    }
}
