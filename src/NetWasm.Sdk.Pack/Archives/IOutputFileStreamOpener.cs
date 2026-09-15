namespace NetWasm.Sdk.Pack.Archives;

public interface IOutputFileStreamOpener
{
    Stream Open(string path);
}
