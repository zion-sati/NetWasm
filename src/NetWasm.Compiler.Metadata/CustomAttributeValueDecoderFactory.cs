using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class CustomAttributeValueDecoderFactory : ICustomAttributeValueDecoderFactory
{
    public ICustomAttributeValueDecoder Create(MetadataCompilationSnapshot metadata,
        ITypeDefinitionResolver definitions, ITypeIdentityResolver identities,
        IMethodInstanceResolver methods, EntityKey systemType)
    {
        var sources = metadata.Assemblies.ToImmutableDictionary(assembly => assembly.Identity,
            assembly => assembly.Metadata);
        var names = new SerializedTypeNameResolver(metadata.Types.ToImmutableDictionary(
            type => (type.Key.Assembly, type.FullName), type => type.Key), definitions, identities,
            systemType.Assembly);
        var signatures = new MetadataSignatureTypeResolver(new MetadataTypeResolver(definitions), identities);
        var typeIdentity = identities.GetTypeIdentity(systemType);
        var stacks = new MetadataStackTypeResolver(definitions);
        var blobs = sources.ToImmutableDictionary(pair => pair.Key,
            pair => (ICustomAttributeBlobDecoder)new CustomAttributeBlobDecoder(new CustomAttributeTypeProvider(pair.Value,
                new SignatureTypeProvider(pair.Key, pair.Value.Reader, pair.Value.AssemblyIdentityAliases),
                signatures, names, definitions, typeIdentity), stacks));
        return new CustomAttributeValueDecoder(sources, blobs, methods);
    }
}
