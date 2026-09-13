namespace NetWasm.Toolchain.Resolution;

public sealed class JcoClosureIntegrityVerifier : IJcoClosureIntegrityVerifier
{
    public void Verify(string packageRoot, string manifestRelativePath) =>
        JavaScriptClosureIntegrityVerifier.Verify(
            packageRoot,
            manifestRelativePath,
            allowBundleCommand: false);
}
