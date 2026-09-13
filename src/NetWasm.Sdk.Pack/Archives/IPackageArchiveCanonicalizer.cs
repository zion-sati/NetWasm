namespace NetWasm.Sdk.Pack.Archives;

public interface IPackageArchiveCanonicalizer
{
    PackageOutput Canonicalize(PackageArchiveNormalizationRequest request);
}
