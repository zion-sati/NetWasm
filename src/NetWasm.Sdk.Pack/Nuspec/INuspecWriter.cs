namespace NetWasm.Sdk.Pack.Nuspec;

public interface INuspecWriter
{
    byte[] Write(CanonicalPackage package);
}
