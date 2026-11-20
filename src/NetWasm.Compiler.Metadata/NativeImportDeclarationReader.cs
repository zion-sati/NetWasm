// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
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
        var signature = metadata.GetBlobReader(method.Signature);
        var header = signature.ReadSignatureHeader();
        if (header.IsGeneric)
        {
            signature.ReadCompressedInteger();
        }
        var flows = new NativeParameterFlow[signature.ReadCompressedInteger()];
        var marshalAs = new UnmanagedType?[flows.Length];
        UnmanagedType? resultMarshalAs = null;
        foreach (var parameterHandle in method.GetParameters())
        {
            var parameter = metadata.GetParameter(parameterHandle);
            var descriptor = parameter.GetMarshallingDescriptor();
            UnmanagedType? native = null;
            if (!descriptor.IsNil)
            {
                // A descriptor of one native type the stubs know is recorded; anything longer (an array's size, a
                // custom marshaler) or unknown is runtime marshalling NetWasm does not have.
                var blob = metadata.GetBlobReader(descriptor);
                if (blob.Length == 1 && (UnmanagedType)blob.ReadByte() is var type && NativeMarshalAsTypes.IsExpressible(type))
                    native = type;
                else
                    hasMarshalling = true;
            }
            if (parameter.SequenceNumber == 0)
            {
                resultMarshalAs = native;
            }
            // Sequence 0 is the result; parameters count from 1.
            if (parameter.SequenceNumber > 0 && parameter.SequenceNumber <= flows.Length)
            {
                marshalAs[parameter.SequenceNumber - 1] = native;
                flows[parameter.SequenceNumber - 1] =
                    ((parameter.Attributes & ParameterAttributes.In) != 0 ? NativeParameterFlow.In : NativeParameterFlow.None) |
                    ((parameter.Attributes & ParameterAttributes.Out) != 0 ? NativeParameterFlow.Out : NativeParameterFlow.None);
            }
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
        return new(
            import.Module.IsNil ? string.Empty :
                metadata.GetString(metadata.GetModuleReference(import.Module).Name),
            metadata.GetString(import.Name),
            import.Attributes,
            header.CallingConvention == SignatureCallingConvention.VarArgs,
            hasMarshalling,
            unsupportedCallingConvention)
        {
            HasWasmImportLinkage = hasWasmImportLinkage,
            TranslatesHResult = (method.ImplAttributes & MethodImplAttributes.PreserveSig) == 0,
            ParameterFlows = new([.. flows]),
            MarshalAs = resultMarshalAs is null && marshalAs.All(type => type is null)
                ? NativeMarshalAsTypes.None
                : new(resultMarshalAs, [.. marshalAs]),
        };
    }

    // Why the C ABI cannot honor the conventions an [UnmanagedCallConv] names, or null when it can.
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
