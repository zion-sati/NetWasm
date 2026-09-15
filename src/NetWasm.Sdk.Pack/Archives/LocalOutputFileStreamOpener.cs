namespace NetWasm.Sdk.Pack.Archives;

public sealed class LocalOutputFileStreamOpener : IOutputFileStreamOpener
{
    public Stream Open(string path) => File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
}
