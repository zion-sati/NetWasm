namespace NetWasm.Toolchain.Manifest;

public interface IToolchainManifestReader
{
    PinnedToolchainManifest Read(string manifestPath);
}
