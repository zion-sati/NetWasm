namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeMaterializationCacheReader
{
    RuntimeMaterializationCacheRead Read(
        string cacheDirectory,
        RuntimeMaterializationCacheSlot slot,
        RuntimeMaterializationCacheKey key);
}
