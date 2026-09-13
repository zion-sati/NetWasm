namespace NetWasm.Sdk.Pack.Packing;

public interface IPackManifestBuilder
{
    CanonicalPackManifest Build(CanonicalPackage package, ArchivePlan plan, PackageOutput output, SymbolPackageOutput? symbols);
}
