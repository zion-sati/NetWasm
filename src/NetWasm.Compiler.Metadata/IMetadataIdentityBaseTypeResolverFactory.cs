using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMetadataIdentityBaseTypeResolverFactory
{
    IMetadataIdentityBaseTypeResolver Create(MetadataCompilationSnapshot snapshot);
}
