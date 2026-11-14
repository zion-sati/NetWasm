using System;
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
    public ImmutableArray<CustomAttributeDescriptor> Read(AssemblyIdentity assembly, string attributeFullName)
    {
        ArgumentException.ThrowIfNullOrEmpty(attributeFullName);
        var source = sources[assembly];
        return Describe(
            source,
            source.Reader.GetAssemblyDefinition().GetCustomAttributes(),
            new CliGenericContext([], []),
            attributeFullName);
    }
}

// Describes a handle collection's attributes: each one's owner, token, constructor and type.
internal abstract class CustomAttributeDescriptions(IMetadataSignatureTypeResolver signatures)
{
    private protected ImmutableArray<CustomAttributeDescriptor> Describe(
        MetadataAssemblySnapshot source,
        CustomAttributeHandleCollection attributes,
        CliGenericContext context,
        string? attributeFullName = null)
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
            if (attributeFullName is not null && MetadataName(source.Reader, declaringType) is { } name &&
                name != attributeFullName)
            {
                continue;
            }
            results.Add(new(source.Identity, MetadataTokens.GetToken(handle),
                MetadataTokens.GetToken(attribute.Constructor), signatures.Resolve(source, declaringType, context), context));
        }
        return results.ToImmutable();
    }

    // A type definition's or reference's namespace-qualified name as metadata spells it (nested types have no
    // namespace); null for a type specification, whose type is resolved to be compared.
    private static string? MetadataName(MetadataReader reader, EntityHandle type) => type.Kind switch
    {
        HandleKind.TypeDefinition => Qualified(reader, reader.GetTypeDefinition((TypeDefinitionHandle)type).Namespace,
            reader.GetTypeDefinition((TypeDefinitionHandle)type).Name),
        HandleKind.TypeReference => Qualified(reader, reader.GetTypeReference((TypeReferenceHandle)type).Namespace,
            reader.GetTypeReference((TypeReferenceHandle)type).Name),
        _ => null,
    };

    // A nil namespace reads as the empty string, as the global namespace's is.
    private static string Qualified(MetadataReader reader, StringHandle ns, StringHandle name)
    {
        var space = reader.GetString(ns);
        return space.Length == 0 ? reader.GetString(name) : space + "." + reader.GetString(name);
    }
}
