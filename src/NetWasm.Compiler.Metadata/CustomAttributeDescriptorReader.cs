using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class CustomAttributeDescriptorReader(
    ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot> sources,
    ITypeDefinitionResolver definitions,
    IMetadataSignatureTypeResolver signatures) : CustomAttributeDescriptions(signatures), ICustomAttributeDescriptorReader
{
    public ImmutableArray<CustomAttributeDescriptor> Read(CliTypeIdentity type)
    {
        var definition = definitions.ResolveTypeIdentity(type);
        var source = sources[definition.Key.Assembly];
        var target = source.Reader.GetTypeDefinition(
            (TypeDefinitionHandle)MetadataTokens.Handle(definition.Key.MetadataToken));
        return Describe(source, target.GetCustomAttributes(), new CliGenericContext(type.TypeArguments, []));
    }
}

// The attributes an assembly declares on itself.
internal sealed class AssemblyAttributeDescriptorReader(
    ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot> sources,
    IMetadataSignatureTypeResolver signatures) : CustomAttributeDescriptions(signatures), IAssemblyAttributeDescriptorReader
{
    public ImmutableArray<CustomAttributeDescriptor> Read(AssemblyIdentity assembly)
    {
        var source = sources[assembly];
        return Describe(
            source,
            source.Reader.GetAssemblyDefinition().GetCustomAttributes(),
            new CliGenericContext([], []));
    }
}

// Describes a handle collection's attributes: each one's owner, token, constructor and type.
internal abstract class CustomAttributeDescriptions(IMetadataSignatureTypeResolver signatures)
{
    private protected ImmutableArray<CustomAttributeDescriptor> Describe(
        MetadataAssemblySnapshot source,
        CustomAttributeHandleCollection attributes,
        CliGenericContext context)
    {
        var results = ImmutableArray.CreateBuilder<CustomAttributeDescriptor>();
        foreach (var handle in attributes)
        {
            var attribute = source.Reader.GetCustomAttribute(handle);
            // SRM validates the CustomAttributeType coded index and exposes
            // only MethodDefinition or MemberReference constructor handles.
            var constructor = attribute.Constructor;
            EntityHandle declaringType = constructor.Kind == HandleKind.MethodDefinition
                ? source.Reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType()
                : source.Reader.GetMemberReference((MemberReferenceHandle)constructor).Parent;
            results.Add(new(source.Identity, MetadataTokens.GetToken(handle),
                MetadataTokens.GetToken(attribute.Constructor), signatures.Resolve(source, declaringType, context), context));
        }
        return results.ToImmutable();
    }
}
