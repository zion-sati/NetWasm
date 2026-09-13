namespace NetWasm.Sdk.Pack.Archives;

public interface IArchiveOutputValidator
{
    void Validate(string archivePath, ArchivePlan plan);
}
