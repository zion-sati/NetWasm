namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeAssetDigestVerifier
{
    void Verify(string path, string expectedSha256);
}
