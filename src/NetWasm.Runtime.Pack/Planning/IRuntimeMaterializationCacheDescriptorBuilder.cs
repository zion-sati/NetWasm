namespace NetWasm.Runtime.Pack.Planning;

internal interface IRuntimeMaterializationCacheDescriptorBuilder
{
    RuntimeMaterializationCacheDescriptor Build(
        RuntimeMaterializationCacheDescriptorRequest request);
}
