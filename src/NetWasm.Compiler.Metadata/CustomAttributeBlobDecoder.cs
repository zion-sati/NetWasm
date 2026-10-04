// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Payload readers adapted from dotnet/runtime v10.0.0:
// https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/Ecma335/CustomAttributeDecoder.cs
//
// Compiler-side workaround for https://github.com/dotnet/runtime/issues/123878:
// SRM CustomAttribute.DecodeValue rejects valid fixed arguments whose enum is
// nested in a closed generic type. Its constructor-signature parser does not
// handle GenericTypeInstance. This does not change the installed .NET library.
//
// NetWasm adaptations:
// - Receive closed constructor parameter identities from IMethodInstanceResolver
//   instead of duplicating SRM's partial signature/generic-context parser.
// - Classify those identities in DecodeFixedArgumentType and normalize enum
//   storage, including vector elements and named/boxed enum values.
// - Retain SRM's named-argument, scalar, tagged-object and array payload readers.
// - Reject null enum names and trailing payload bytes explicitly. Null Type
//   values remain valid; trailing bytes are rejected by desktop materialization.
// All this code runs during compilation; it adds no guest reflection metadata.
//
// Maintenance: this is a source snapshot, so package upgrades do not update it.
// Compare the payload readers with upstream when updating System.Reflection.Metadata.
// Once #123878 is fixed in the supported dependency, evaluate removing this port
// and delegating to DecodeValue again. Require the blob/metadata decoder tests,
// AttributeNestedEnumSemanticsTests and the existing attribute payload suite to
// pass first, preserving closed identities, enum storage, null/empty vectors,
// named/boxed values, generic constructor arguments and malformed-input rejection.

using System;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class CustomAttributeBlobDecoder(
    ICustomAttributeTypeProvider<CliTypeIdentity> provider,
    IMetadataStackTypeResolver stacks) : ICustomAttributeBlobDecoder
{
    public CustomAttributeValue<CliTypeIdentity> Decode(BlobReader value, ImmutableArray<CliTypeIdentity> parameters)
    {
        if (value.ReadUInt16() != 1) throw new BadImageFormatException();
        var arguments = ImmutableArray.CreateBuilder<CustomAttributeTypedArgument<CliTypeIdentity>>(parameters.Length);
        foreach (var parameter in parameters)
        {
            arguments.Add(DecodeArgument(ref value, DecodeFixedArgumentType(parameter)));
        }
        var named = DecodeNamedArguments(ref value);
        if (value.RemainingBytes != 0) throw new BadImageFormatException();
        return new(arguments.MoveToImmutable(), named);
    }

    private ArgumentTypeInfo DecodeFixedArgumentType(CliTypeIdentity parameter, bool isElementType = false)
    {
        var type = stacks.Resolve(parameter);
        if (type.Shape == CliTypeShape.SzArray)
        {
            if (isElementType) throw new BadImageFormatException();
            var element = DecodeFixedArgumentType(type.ElementType!, isElementType: true);
            return new()
            {
                Type = provider.GetSZArrayType(element.Type),
                TypeCode = SerializationTypeCode.SZArray,
                ElementType = element.Type,
                ElementTypeCode = element.TypeCode,
            };
        }
        var code = type.CanonicalName switch
        {
            "primitive:bool" => SerializationTypeCode.Boolean,
            "primitive:char" => SerializationTypeCode.Char,
            "primitive:i1" => SerializationTypeCode.SByte,
            "primitive:u1" => SerializationTypeCode.Byte,
            "primitive:i2" => SerializationTypeCode.Int16,
            "primitive:u2" => SerializationTypeCode.UInt16,
            "primitive:i4" => SerializationTypeCode.Int32,
            "primitive:u4" => SerializationTypeCode.UInt32,
            "primitive:i8" => SerializationTypeCode.Int64,
            "primitive:u8" => SerializationTypeCode.UInt64,
            "primitive:f4" => SerializationTypeCode.Single,
            "primitive:f8" => SerializationTypeCode.Double,
            "primitive:string" => SerializationTypeCode.String,
            "primitive:object" => SerializationTypeCode.TaggedObject,
            _ when provider.IsSystemType(type) => SerializationTypeCode.Type,
            _ when type.IsValueType && type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation =>
                (SerializationTypeCode)provider.GetUnderlyingEnumType(type),
            _ => throw new BadImageFormatException(),
        };
        return new() { Type = type, TypeCode = code };
    }

    private ImmutableArray<CustomAttributeNamedArgument<CliTypeIdentity>> DecodeNamedArguments(ref BlobReader valueReader)
    {
        int count = valueReader.ReadUInt16();
        if (count == 0)
        {
            return ImmutableArray<CustomAttributeNamedArgument<CliTypeIdentity>>.Empty;
        }

        var arguments = ImmutableArray.CreateBuilder<CustomAttributeNamedArgument<CliTypeIdentity>>(count);
        for (int i = 0; i < count; i++)
        {
            CustomAttributeNamedArgumentKind kind = (CustomAttributeNamedArgumentKind)valueReader.ReadSerializationTypeCode();
            if (kind != CustomAttributeNamedArgumentKind.Field && kind != CustomAttributeNamedArgumentKind.Property)
            {
                throw new BadImageFormatException();
            }

            ArgumentTypeInfo info = DecodeNamedArgumentType(ref valueReader);
            string? name = valueReader.ReadSerializedString();
            CustomAttributeTypedArgument<CliTypeIdentity> argument = DecodeArgument(ref valueReader, info);
            arguments.Add(new CustomAttributeNamedArgument<CliTypeIdentity>(name, kind, argument.Type, argument.Value));
        }

        return arguments.MoveToImmutable();
    }

    private struct ArgumentTypeInfo
    {
        public CliTypeIdentity Type;
        public CliTypeIdentity ElementType;
        public SerializationTypeCode TypeCode;
        public SerializationTypeCode ElementTypeCode;
    }

    private ArgumentTypeInfo DecodeNamedArgumentType(ref BlobReader valueReader, bool isElementType = false)
    {
        var info = new ArgumentTypeInfo
        {
            TypeCode = valueReader.ReadSerializationTypeCode(),
        };

        switch (info.TypeCode)
        {
            case SerializationTypeCode.Boolean:
            case SerializationTypeCode.Byte:
            case SerializationTypeCode.Char:
            case SerializationTypeCode.Double:
            case SerializationTypeCode.Int16:
            case SerializationTypeCode.Int32:
            case SerializationTypeCode.Int64:
            case SerializationTypeCode.SByte:
            case SerializationTypeCode.Single:
            case SerializationTypeCode.String:
            case SerializationTypeCode.UInt16:
            case SerializationTypeCode.UInt32:
            case SerializationTypeCode.UInt64:
                info.Type = provider.GetPrimitiveType((PrimitiveTypeCode)info.TypeCode);
                break;

            case SerializationTypeCode.Type:
                info.Type = provider.GetSystemType();
                break;

            case SerializationTypeCode.TaggedObject:
                info.Type = provider.GetPrimitiveType(PrimitiveTypeCode.Object);
                break;

            case SerializationTypeCode.SZArray:
                if (isElementType)
                {
                    // jagged arrays are not allowed.
                    throw new BadImageFormatException();
                }

                var elementInfo = DecodeNamedArgumentType(ref valueReader, isElementType: true);
                info.ElementType = elementInfo.Type;
                info.ElementTypeCode = elementInfo.TypeCode;
                info.Type = provider.GetSZArrayType(info.ElementType);
                break;

            case SerializationTypeCode.Enum:
                string? typeName = valueReader.ReadSerializedString();
                if (typeName is null) throw new BadImageFormatException();
                info.Type = stacks.Resolve(provider.GetTypeFromSerializedName(typeName));
                info.TypeCode = (SerializationTypeCode)provider.GetUnderlyingEnumType(info.Type);
                break;

            default:
                throw new BadImageFormatException();
        }

        return info;
    }

    private CustomAttributeTypedArgument<CliTypeIdentity> DecodeArgument(ref BlobReader valueReader, ArgumentTypeInfo info)
    {
        if (info.TypeCode == SerializationTypeCode.TaggedObject)
        {
            info = DecodeNamedArgumentType(ref valueReader);
        }

        // PERF_TODO: https://github.com/dotnet/runtime/issues/16551
        //   Cache /reuse common arguments to avoid boxing (small integers, true, false).
        object? value;
        switch (info.TypeCode)
        {
            case SerializationTypeCode.Boolean:
                value = valueReader.ReadBoolean();
                break;

            case SerializationTypeCode.Byte:
                value = valueReader.ReadByte();
                break;

            case SerializationTypeCode.Char:
                value = valueReader.ReadChar();
                break;

            case SerializationTypeCode.Double:
                value = valueReader.ReadDouble();
                break;

            case SerializationTypeCode.Int16:
                value = valueReader.ReadInt16();
                break;

            case SerializationTypeCode.Int32:
                value = valueReader.ReadInt32();
                break;

            case SerializationTypeCode.Int64:
                value = valueReader.ReadInt64();
                break;

            case SerializationTypeCode.SByte:
                value = valueReader.ReadSByte();
                break;

            case SerializationTypeCode.Single:
                value = valueReader.ReadSingle();
                break;

            case SerializationTypeCode.UInt16:
                value = valueReader.ReadUInt16();
                break;

            case SerializationTypeCode.UInt32:
                value = valueReader.ReadUInt32();
                break;

            case SerializationTypeCode.UInt64:
                value = valueReader.ReadUInt64();
                break;

            case SerializationTypeCode.String:
                value = valueReader.ReadSerializedString();
                break;

            case SerializationTypeCode.Type:
                string? typeName = valueReader.ReadSerializedString();
                value = provider.GetTypeFromSerializedName(typeName!);
                break;

            case SerializationTypeCode.SZArray:
                value = DecodeArrayArgument(ref valueReader, info);
                break;

            default:
                throw new BadImageFormatException();
        }

        return new CustomAttributeTypedArgument<CliTypeIdentity>(info.Type, value);
    }

    private ImmutableArray<CustomAttributeTypedArgument<CliTypeIdentity>>? DecodeArrayArgument(ref BlobReader blobReader, ArgumentTypeInfo info)
    {
        int count = blobReader.ReadInt32();
        if (count == -1)
        {
            return null;
        }

        if (count == 0)
        {
            return ImmutableArray<CustomAttributeTypedArgument<CliTypeIdentity>>.Empty;
        }

        if (count < 0)
        {
            throw new BadImageFormatException();
        }

        var elementInfo = new ArgumentTypeInfo
        {
            Type = info.ElementType,
            TypeCode = info.ElementTypeCode,
        };

        var array = ImmutableArray.CreateBuilder<CustomAttributeTypedArgument<CliTypeIdentity>>(count);

        for (int i = 0; i < count; i++)
        {
            array.Add(DecodeArgument(ref blobReader, elementInfo));
        }

        return array.MoveToImmutable();
    }

}
