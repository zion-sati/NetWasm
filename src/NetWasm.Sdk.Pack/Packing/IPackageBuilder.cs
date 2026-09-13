namespace NetWasm.Sdk.Pack.Packing;

public interface IPackageBuilder
{
    CanonicalPackResult BuildPackage(CanonicalPackInputs inputs);
}
