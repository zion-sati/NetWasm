using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ICustomAttributeValueDecoderFactory
{
    ICustomAttributeValueDecoder Create(MetadataCompilationSnapshot metadata,
        ITypeDefinitionResolver definitions, ITypeIdentityResolver identities,
        IMethodInstanceResolver methods, EntityKey systemType);
}
