using System;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class CustomAttributeValueDecoder(
    ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot> sources,
    ImmutableDictionary<AssemblyIdentity, ICustomAttributeBlobDecoder> blobs,
    IMethodInstanceResolver methods)
    : ICustomAttributeValueDecoder
{
    public CustomAttributeValue<CliTypeIdentity> Decode(CustomAttributeDescriptor attribute)
    {
        var source = sources[attribute.Source];
        var handle = MetadataTokens.EntityHandle(attribute.ConstructorToken);
        var signature = handle.Kind switch
        {
            HandleKind.MethodDefinition => source.Reader.GetMethodDefinition((MethodDefinitionHandle)handle).Signature,
            HandleKind.MemberReference => source.Reader.GetMemberReference((MemberReferenceHandle)handle).Signature,
            _ => throw new BadImageFormatException(),
        };
        var header = source.Reader.GetBlobReader(signature).ReadSignatureHeader();
        if (header.Kind != SignatureKind.Method || header.IsGeneric || !header.IsInstance)
            throw new BadImageFormatException();
        // Keep signature resolution on the ordinary metadata path. SRM's own
        // DecodeValue signature parser rejects valid nested generic enums; see
        // CustomAttributeBlobDecoder for the upstream issue and removal criteria.
        var constructor = methods.ResolveMethodInstance(attribute.Source, attribute.ConstructorToken,
            "custom attribute constructor", 0, attribute.GenericContext);
        if (constructor.Definition.Name != ".ctor" || constructor.Definition.IsStatic ||
            constructor.Definition.GenericArity != 0 || constructor.Signature.ReturnType != CliValueKind.Void)
            throw new BadImageFormatException();
        var value = source.Reader.GetCustomAttribute(
            (CustomAttributeHandle)MetadataTokens.Handle(attribute.AttributeToken)).Value;
        return blobs[attribute.Source].Decode(source.Reader.GetBlobReader(value),
            constructor.Signature.ParameterSignatureTypes);
    }
}
