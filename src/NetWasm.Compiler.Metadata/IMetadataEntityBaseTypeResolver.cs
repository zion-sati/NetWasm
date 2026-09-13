using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMetadataEntityBaseTypeResolver
{
    EntityKey? GetBaseType(EntityKey typeKey);
}
