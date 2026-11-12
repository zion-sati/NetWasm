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
    private const string WasmImportLinkageAttribute =
        "System.Runtime.InteropServices.WasmImportLinkageAttribute";

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
        var hasWasmImportLinkage = false;
        string? unsupportedCallingConvention = null;
        foreach (var handle in method.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            var name = AttributeName(metadata, attribute);
            hasWasmImportLinkage |= name == WasmImportLinkageAttribute;
            // As on desktop .NET, [UnmanagedCallConv] names the convention only of an import whose
            // DllImport leaves it Winapi.
            if (name == UnmanagedCallConvAttribute &&
                (import.Attributes & MethodImportAttributes.CallingConventionMask) ==
                    MethodImportAttributes.CallingConventionWinApi)
            {
                unsupportedCallingConvention = ReadUnsupportedCallingConvention(attribute);
            }
        }
        var signature = metadata.GetBlobReader(method.Signature).ReadSignatureHeader();
        return new(
            import.Module.IsNil ? string.Empty :
                metadata.GetString(metadata.GetModuleReference(import.Module).Name),
            metadata.GetString(import.Name),
            import.Attributes,
            signature.CallingConvention == SignatureCallingConvention.VarArgs,
            hasMarshalling,
            unsupportedCallingConvention)
        {
            HasWasmImportLinkage = hasWasmImportLinkage,
        };
    }

    // Why the C ABI cannot honour the conventions an [UnmanagedCallConv] names, or null when it can.
    private static string? ReadUnsupportedCallingConvention(CustomAttribute attribute)
    {
        const string Malformed = "[UnmanagedCallConv] sets more than its CallConvs";
        var value = attribute.DecodeValue(new CustomAttributeTypeNameProvider());
        if (!value.FixedArguments.IsEmpty)
            return Malformed;
        var conventions = ImmutableArray<string>.Empty;
        var found = false;
        foreach (var argument in value.NamedArguments)
        {
            if (found || argument.Name != "CallConvs" ||
                argument.Value is not
                    ImmutableArray<CustomAttributeTypedArgument<string>> items)
            {
                return Malformed;
            }
            conventions = [.. items.Select(item =>
                CustomAttributeTypeNameProvider.NormalizeSerializedTypeName(
                    item.Value as string ?? string.Empty))];
            found = true;
        }
        return UnmanagedCallingConventions.Unsupported(conventions);
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
