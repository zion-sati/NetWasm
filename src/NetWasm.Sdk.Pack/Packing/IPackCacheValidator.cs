namespace NetWasm.Sdk.Pack.Packing;

public interface IPackCacheValidator
{
    void Validate(CanonicalPackInputs inputs, CanonicalPackage package);
}
