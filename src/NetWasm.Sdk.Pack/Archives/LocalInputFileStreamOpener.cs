namespace NetWasm.Sdk.Pack.Archives;

public sealed class LocalInputFileStreamOpener : IInputFileStreamOpener
{
    public Stream Open(string sourcePath) => new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
}
