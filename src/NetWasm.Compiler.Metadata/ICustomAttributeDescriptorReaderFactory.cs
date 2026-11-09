using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ICustomAttributeDescriptorReaderFactory
{
    ICustomAttributeDescriptorReader Create(MetadataCompilationSnapshot metadata,
        ITypeDefinitionResolver definitions, ITypeIdentityResolver identities);
}

public interface IAssemblyAttributeDescriptorReaderFactory
{
    IAssemblyAttributeDescriptorReader Create(MetadataCompilationSnapshot metadata,
        ITypeDefinitionResolver definitions, ITypeIdentityResolver identities);
}
