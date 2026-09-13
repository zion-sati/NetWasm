namespace NetWasm.Toolchain.Resolution;

public sealed class HostingBundleClosureIntegrityVerifier :
    IHostingBundleClosureIntegrityVerifier
{
    public void Verify(string packageRoot, string manifestRelativePath) =>
        JavaScriptClosureIntegrityVerifier.Verify(
            packageRoot,
            manifestRelativePath,
            allowBundleCommand: true);
}
