using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ITypeDefinitionResolverFactory
{
    ITypeDefinitionResolver Create(MetadataCompilationSnapshot snapshot);
}
