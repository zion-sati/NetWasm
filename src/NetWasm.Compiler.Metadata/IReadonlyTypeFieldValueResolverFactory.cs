using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IReadonlyTypeFieldValueResolverFactory
{
    IReadonlyTypeFieldValueResolver Create(
        MetadataCompilationSnapshot metadata,
        IMethodInstanceResolver methods,
        ITypeDefinitionResolver definitions,
        ITypeRepository types,
        EntityKey typeDefinition);
}
