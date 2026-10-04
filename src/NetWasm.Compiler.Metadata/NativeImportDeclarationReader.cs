using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata;

public sealed class NativeImportDeclarationReader : INativeImportDeclarationReader
{
    private const string UnmanagedCallConvAttribute =
        "System.Runtime.InteropServices.UnmanagedCallConvAttribute";
    private const string CallConvCdecl =
        "System.Runtime.CompilerServices.CallConvCdecl";

    public NativeImportDeclaration? Read(MetadataReader metadata, MethodDefinition method)
    {
        if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0)
        {
            return null;
        }
        var import = method.GetImport();
        var hasMarshalling = false;
        foreach (var parameterHandle in method.GetParameters())
        {
            var parameter = metadata.GetParameter(parameterHandle);
            hasMarshalling |= !parameter.GetMarshallingDescriptor().IsNil;
        }
        var suppressesGcTransition = false;
        var hasCustomCallingConvention = false;
        var attributes = import.Attributes;
        foreach (var handle in method.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            var name = AttributeName(metadata, attribute);
            suppressesGcTransition |= name ==
                "System.Runtime.InteropServices.SuppressGCTransitionAttribute";
            if (name == UnmanagedCallConvAttribute)
            {
                if (TryReadSupportedCallingConvention(
                    metadata,
                    attribute,
                    out var explicitCdecl))
                {
                    if (explicitCdecl)
                    {
                        attributes = (attributes &
                            ~MethodImportAttributes.CallingConventionMask) |
                            MethodImportAttributes.CallingConventionCDecl;
                    }
                }
                else
                {
                    hasCustomCallingConvention = true;
                }
            }
        }
        var signature = metadata.GetBlobReader(method.Signature).ReadSignatureHeader();
        return new(
            import.Module.IsNil ? string.Empty :
                metadata.GetString(metadata.GetModuleReference(import.Module).Name),
            metadata.GetString(import.Name),
            attributes,
            signature.CallingConvention == SignatureCallingConvention.VarArgs,
            hasMarshalling,
            suppressesGcTransition,
            hasCustomCallingConvention);
    }

    private static bool TryReadSupportedCallingConvention(
        MetadataReader metadata,
        CustomAttribute attribute,
        out bool explicitCdecl)
    {
        explicitCdecl = false;
        var value = attribute.DecodeValue(new CustomAttributeTypeNameProvider());
        if (!value.FixedArguments.IsEmpty)
            return false;
        var conventions = ImmutableArray<string>.Empty;
        var found = false;
        foreach (var argument in value.NamedArguments)
        {
            if (found || argument.Name != "CallConvs" ||
                argument.Value is not
                    ImmutableArray<CustomAttributeTypedArgument<string>> items)
            {
                return false;
            }
            conventions = [.. items.Select(item =>
                CustomAttributeTypeNameProvider.NormalizeSerializedTypeName(
                    item.Value as string ?? string.Empty))];
            found = true;
        }
        if (!found || conventions.IsEmpty)
            return true;
        explicitCdecl = conventions is [CallConvCdecl];
        return explicitCdecl;
    }

    private static string AttributeName(MetadataReader metadata, CustomAttribute attribute)
    {
        // CustomAttributeType encodes only MethodDef or MemberRef constructors.
        var type = attribute.Constructor.Kind == HandleKind.MethodDefinition
            ? metadata.GetMethodDefinition(
                (MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()
            : metadata.GetMemberReference(
                (MemberReferenceHandle)attribute.Constructor).Parent;
        return type.Kind switch
        {
            HandleKind.TypeDefinition => Name(
                metadata.GetTypeDefinition((TypeDefinitionHandle)type).Namespace,
                metadata.GetTypeDefinition((TypeDefinitionHandle)type).Name),
            HandleKind.TypeReference => Name(
                metadata.GetTypeReference((TypeReferenceHandle)type).Namespace,
                metadata.GetTypeReference((TypeReferenceHandle)type).Name),
            _ => string.Empty,
        };

        string Name(StringHandle typeNamespace, StringHandle typeName) =>
            metadata.GetString(typeNamespace) + "." + metadata.GetString(typeName);
    }
}
