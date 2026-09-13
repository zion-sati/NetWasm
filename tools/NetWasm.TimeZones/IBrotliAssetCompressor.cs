namespace NetWasm.TimeZones;

internal interface IBrotliAssetCompressor
{
    byte[] Compress(byte[] contents);
}
