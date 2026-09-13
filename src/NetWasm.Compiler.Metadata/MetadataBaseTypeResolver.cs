using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataBaseTypeResolver(
    IMetadataAssemblyResolver assemblies,
    IMetadataTypeResolver types) : IMetadataEntityBaseTypeResolver
{
    public EntityKey? GetBaseType(EntityKey typeKey)
    {
        var source = assemblies.Resolve(typeKey.Assembly);
        var handle = source.BaseTypes[typeKey.MetadataToken];
        return handle.IsNil ? null : types.Resolve(source, handle).Key;
    }
}
