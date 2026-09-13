namespace NetWasm.Sdk.Pack.Archives;

public interface IArchivePlanner
{
    ArchivePlan Plan(CanonicalPackage package);
}
