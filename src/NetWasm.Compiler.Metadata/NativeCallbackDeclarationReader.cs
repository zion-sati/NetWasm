using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata;

public sealed class NativeCallbackDeclarationReader : INativeCallbackDeclarationReader
{
    private const string AttributeName =
        "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute";

    public NativeCallbackDeclaration? Read(
        MetadataReader metadata,
        MethodDefinition method)
    {
        NativeCallbackDeclaration? declaration = null;
        foreach (var handle in method.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (GetAttributeTypeName(metadata, attribute.Constructor) != AttributeName)
            {
                continue;
            }
            if (declaration is not null)
            {
                throw new BadImageFormatException(
                    "A method cannot declare multiple UnmanagedCallersOnly attributes.");
            }

            var value = attribute.DecodeValue(new CustomAttributeTypeNameProvider());
            string? entryPoint = null;
            var callingConventions = ImmutableArray<string>.Empty;
            var unsupported = false;
            foreach (var argument in value.NamedArguments)
            {
                if (argument.Name == "EntryPoint" && argument.Value is string name)
                {
                    entryPoint = name;
                    continue;
                }
                if (argument.Name == "CallConvs" &&
                    argument.Value is ImmutableArray<CustomAttributeTypedArgument<string>> items)
                {
                    callingConventions = [.. items.Select(item =>
                        CustomAttributeTypeNameProvider.NormalizeSerializedTypeName(
                        item.Value as string ?? string.Empty))];
                    continue;
                }
                unsupported = true;
            }
            var signature = metadata.GetBlobReader(method.Signature).ReadSignatureHeader();
            declaration = new(
                callingConventions,
                entryPoint,
                signature.CallingConvention == SignatureCallingConvention.VarArgs,
                unsupported);
        }
        return declaration;
    }

    private static string GetAttributeTypeName(
        MetadataReader metadata,
        EntityHandle constructor)
    {
        var type = constructor.Kind == HandleKind.MethodDefinition
            ? metadata.GetMethodDefinition(
                (MethodDefinitionHandle)constructor).GetDeclaringType()
            : metadata.GetMemberReference(
                (MemberReferenceHandle)constructor).Parent;
        return type.Kind switch
        {
            HandleKind.TypeDefinition => Name(
                metadata,
                metadata.GetTypeDefinition((TypeDefinitionHandle)type).Namespace,
                metadata.GetTypeDefinition((TypeDefinitionHandle)type).Name),
            HandleKind.TypeReference => Name(
                metadata,
                metadata.GetTypeReference((TypeReferenceHandle)type).Namespace,
                metadata.GetTypeReference((TypeReferenceHandle)type).Name),
            _ => string.Empty,
        };
    }

    private static string Name(
        MetadataReader metadata,
        StringHandle typeNamespace,
        StringHandle typeName) =>
        metadata.GetString(typeNamespace) + "." + metadata.GetString(typeName);

}
