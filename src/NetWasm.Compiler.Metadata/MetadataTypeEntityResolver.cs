using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataTypeEntityResolver(IMetadataTypeResolver types)
    : IMetadataTypeEntityResolver
{
    public EntityKey Resolve(MetadataAssemblySnapshot source, EntityHandle handle) =>
        types.Resolve(source, handle).Key;
}
