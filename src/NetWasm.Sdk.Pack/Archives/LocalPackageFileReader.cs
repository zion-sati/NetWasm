namespace NetWasm.Sdk.Pack.Archives;

public sealed class LocalPackageFileReader : IPackageFileReader
{
    private readonly IInputFileStreamOpener streamOpener;

    public LocalPackageFileReader(IInputFileStreamOpener streamOpener)
    {
        this.streamOpener = streamOpener ?? throw new ArgumentNullException(nameof(streamOpener));
    }

    public byte[] Read(string sourcePath, long maxLength)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK011, "A package input file path is missing.");
        }

        if (maxLength <= 0)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK013, "The package input size policy is invalid.");
        }

        try
        {
            using var stream = streamOpener.Open(sourcePath);
            var before = stream.Length;
            if (before > maxLength || before > int.MaxValue)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "A package input file exceeds the deterministic size policy.");
            }

            using var memory = new MemoryStream((int)before);
            var buffer = new byte[81920];
            var total = 0L;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
            {
                total += read;
                if (total > maxLength)
                {
                    throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "A package input file exceeds the deterministic size policy.");
                }

                memory.Write(buffer, 0, read);
            }

            if (before != stream.Length || before != total)
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK010, "A package input file changed while it was being read.");
            }

            return memory.ToArray();
        }
        catch (NetWasmPackException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK011, "A package input file could not be read.");
        }
    }
}
