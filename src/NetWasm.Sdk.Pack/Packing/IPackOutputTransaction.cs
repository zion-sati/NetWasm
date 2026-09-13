namespace NetWasm.Sdk.Pack.Packing;

public interface IPackOutputTransaction
{
    CanonicalPackResult Commit(CanonicalPackage package, ArchivePlan plan);
}
