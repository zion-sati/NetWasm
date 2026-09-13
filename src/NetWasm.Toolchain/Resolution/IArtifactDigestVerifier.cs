namespace NetWasm.Toolchain.Resolution;

public interface IArtifactDigestVerifier
{
    void Verify(string path, string expectedSha256);
}
