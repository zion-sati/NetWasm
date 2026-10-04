using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class CustomAttributeDescriptorReader(
    ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot> sources,
    ITypeDefinitionResolver definitions,
    IMetadataSignatureTypeResolver signatures) : ICustomAttributeDescriptorReader
{
    public ImmutableArray<CustomAttributeDescriptor> Read(CliTypeIdentity type)
    {
        var definition = definitions.ResolveTypeIdentity(type);
        var source = sources[definition.Key.Assembly];
        var target = source.Reader.GetTypeDefinition(
            (TypeDefinitionHandle)MetadataTokens.Handle(definition.Key.MetadataToken));
        var context = new CliGenericContext(type.TypeArguments, []);
        var results = ImmutableArray.CreateBuilder<CustomAttributeDescriptor>();
        foreach (var handle in target.GetCustomAttributes())
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
