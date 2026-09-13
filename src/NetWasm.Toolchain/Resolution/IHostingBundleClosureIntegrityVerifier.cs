namespace NetWasm.Toolchain.Resolution;

public interface IHostingBundleClosureIntegrityVerifier
{
    void Verify(string packageRoot, string manifestRelativePath);
}
