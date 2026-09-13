namespace NetWasm.Sdk.Pack.Archives;

public interface IPackageFileReader
{
    byte[] Read(string sourcePath, long maxLength);
}
