namespace NetWasm.Sdk.Pack.Packing;

public interface IPackRequestBuilder
{
    CanonicalPackage Build(CanonicalPackInputs inputs);
}
