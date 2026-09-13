namespace NetWasm.Sdk.Pack.Packing;

public interface IPackManifestSerializer
{
    byte[] Serialize(CanonicalPackManifest manifest);
}
