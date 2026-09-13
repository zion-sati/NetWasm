namespace NetWasm.Toolchain.Resolution;

public interface IJcoClosureIntegrityVerifier
{
    void Verify(string packageRoot, string manifestRelativePath);
}
