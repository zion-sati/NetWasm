namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeMaterializationCacheKeyBuilder
{
    RuntimeMaterializationCacheKey Build(RuntimeMaterializationCacheKeyRequest request);
    RuntimeMaterializationCacheKey Build(RuntimeNativeMaterializationCacheKeyRequest request);
}
