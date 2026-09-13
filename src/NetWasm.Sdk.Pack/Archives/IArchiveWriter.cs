namespace NetWasm.Sdk.Pack.Archives;

public interface IArchiveWriter
{
    PackageOutput Write(ArchivePlan plan);
}
