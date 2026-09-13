using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMetadataEntityBaseTypeResolverFactory
{
    IMetadataEntityBaseTypeResolver Create(MetadataCompilationSnapshot snapshot);
}
