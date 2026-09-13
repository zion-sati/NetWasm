using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataBaseTypeIdentityResolver(
    ITypeDefinitionResolver typeDefinitions,
    IMetadataAssemblyResolver assemblies,
    IMetadataSignatureTypeResolver signatureTypes) : IBaseTypeIdentityResolver
{
    public CliTypeIdentity? GetBaseTypeIdentity(CliTypeIdentity type)
    {
        var definition = typeDefinitions.ResolveTypeIdentity(type);
        var source = assemblies.Resolve(definition.Key.Assembly);
        var handle = source.BaseTypes[definition.Key.MetadataToken];
        if (handle.IsNil)
        {
            return null;
        }
        var context = new CliGenericContext(
            type.Shape == CliTypeShape.GenericInstantiation ? type.TypeArguments : [],
            []);
        return signatureTypes.Resolve(source, handle, context);
    }
}
