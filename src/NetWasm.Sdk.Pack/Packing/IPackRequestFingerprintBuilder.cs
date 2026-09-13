namespace NetWasm.Sdk.Pack.Packing;

public interface IPackRequestFingerprintBuilder
{
    string Build(CanonicalPackage package);
}
