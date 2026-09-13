namespace NetWasm.Sdk.Pack.Archives;

public interface IPackageArchiveReader
{
    PackageArchive Read(PackageArchiveNormalizationRequest request);
}
