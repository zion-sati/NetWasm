namespace NetWasm.Sdk.Pack.Archives;

public interface IInputFileStreamOpener
{
    Stream Open(string sourcePath);
}
