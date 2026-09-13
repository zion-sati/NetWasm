using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataTypeResolver(
    ITypeDefinitionResolver typeDefinitions) : IMetadataTypeResolver
{
    public TypeDefinitionModel Resolve(
        MetadataAssemblySnapshot source,
        EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            return source.Types[MetadataTokens.GetToken(handle)];
        }
        var provider = new SignatureTypeProvider(
            source.Identity,
            source.Reader,
            source.AssemblyIdentityAliases);
        var identity = handle.Kind switch
        {
            HandleKind.TypeReference => provider.GetTypeFromReference(
                source.Reader,
                (TypeReferenceHandle)handle,
                rawTypeKind: 0x12),
            HandleKind.TypeSpecification => provider.GetTypeFromSpecification(
                source.Reader,
                genericContext: null,
                (TypeSpecificationHandle)handle,
                rawTypeKind: 0x12),
            _ => throw new CompilerException(
                new CompilerDiagnostic(
                    DiagnosticCode.UnsupportedMetadata,
                    $"unsupported type reference shape '{handle.Kind}'")),
        };
        return typeDefinitions.ResolveTypeIdentity(identity);
    }
}
