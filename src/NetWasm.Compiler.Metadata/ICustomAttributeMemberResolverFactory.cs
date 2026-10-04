using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ICustomAttributeMemberResolverFactory
{
    ICustomAttributeMemberResolver Create(MetadataCompilationSnapshot metadata,
        ITypeDefinitionResolver definitions, ITypeIdentityResolver identities,
        ITypeRepository types, IFieldRepository fields, IMethodRepository methods,
        IMethodInstanceResolver methodInstances);
}
