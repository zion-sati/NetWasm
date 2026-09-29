namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeMaterializationCacheWriter
{
    void Write(
        string cacheDirectory,
        RuntimeMaterializationCacheSlot slot,
        RuntimeMaterializationCacheKey key,
        byte[] bytes,
        string sha256);
}
