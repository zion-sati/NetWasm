using NetWasm.Toolchain.Resolution;

namespace NetWasm.Toolchain.Tests;

public sealed class ArtifactVerifierTests
{
    [Fact]
    public void FilePresenceCheckerReportsExistingAndMissingPaths()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netwasm-toolchain-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, [42]);
        try
        {
            IFilePresenceChecker checker = CreatePresenceChecker();
            Assert.True(checker.Exists(path));
            Assert.False(checker.Exists(path + ".missing"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sha256VerifierAcceptsMatchingDigest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netwasm-toolchain-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, [42]);
        try
        {
            IArtifactDigestVerifier verifier = CreateDigestVerifier();
            Assert.Throws<InvalidDataException>(() => verifier.Verify(path, "00"));

            var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            verifier.Verify(path, actual);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sha256VerifierRejectsMismatch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netwasm-toolchain-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, [42]);
        try
        {
            IArtifactDigestVerifier verifier = CreateDigestVerifier();
            Assert.Throws<InvalidDataException>(() => verifier.Verify(path, "00"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sha256VerifierRejectsBlankInputs()
    {
        IArtifactDigestVerifier verifier = CreateDigestVerifier();

        Assert.Throws<ArgumentException>(() => verifier.Verify("", "abc"));
        Assert.Throws<ArgumentException>(() => verifier.Verify("path", ""));
    }

    [Fact]
    public void Sha256VerifierRejectsAReparsePointAsset()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"netwasm-toolchain-outside-{Guid.NewGuid():N}.bin");
        var link = Path.Combine(Path.GetTempPath(), $"netwasm-toolchain-link-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(outside, [42]);
        try
        {
            File.CreateSymbolicLink(link, outside);
            var verifier = CreateDigestVerifier();
            Assert.Throws<InvalidDataException>(() => verifier.Verify(link, "00"));
        }
        finally
        {
            File.Delete(link);
            File.Delete(outside);
        }
    }

    private static IFilePresenceChecker CreatePresenceChecker()
    {
        IFilePresenceChecker checker = new FilePresenceChecker();
        return checker;
    }

    private static IArtifactDigestVerifier CreateDigestVerifier()
    {
        IArtifactDigestVerifier verifier = new Sha256ArtifactDigestVerifier();
        return verifier;
    }
}
