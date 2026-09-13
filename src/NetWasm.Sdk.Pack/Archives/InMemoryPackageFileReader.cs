namespace NetWasm.Sdk.Pack.Archives;

public sealed class InMemoryPackageFileReader : IPackageFileReader
{
    private readonly IReadOnlyDictionary<string, byte[]> files;

    public InMemoryPackageFileReader(IReadOnlyDictionary<string, byte[]> files)
    {
        this.files = files ?? throw new ArgumentNullException(nameof(files));
    }

    public byte[] Read(string sourcePath, long maxLength)
    {
        if (maxLength <= 0)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The package input size policy is invalid.");
        }

        if (!files.TryGetValue(sourcePath, out var content))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK011, "A package input file could not be read.");
        }

        if (content.LongLength > maxLength)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "A package input file exceeds the deterministic size policy.");
        }

        return content.ToArray();
    }
}
