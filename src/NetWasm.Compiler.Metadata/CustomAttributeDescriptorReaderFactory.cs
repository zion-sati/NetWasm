using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class CustomAttributeDescriptorReaderFactory : ICustomAttributeDescriptorReaderFactory
{
    public ICustomAttributeDescriptorReader Create(MetadataCompilationSnapshot metadata,
        ITypeDefinitionResolver definitions, ITypeIdentityResolver identities) =>
        new CustomAttributeDescriptorReader(metadata.Assemblies.ToImmutableDictionary(
            assembly => assembly.Identity, assembly => assembly.Metadata), definitions,
            new MetadataSignatureTypeResolver(new MetadataTypeResolver(definitions), identities));
}
