using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

[Flags]
internal enum RuntimeTypeFactsFlags
{
    None = 0,
    ValueType = 1 << 0,
    Enum = 1 << 1,
    Interface = 1 << 2,
    ByReference = 1 << 3,
    Pointer = 1 << 4,
    ContainsGenericParameters = 1 << 5,
    GenericTypeDefinition = 1 << 6,
    GenericType = 1 << 7,
    Array = 1 << 8,
    SzArray = 1 << 9,
    Class = 1 << 10,
    Sealed = 1 << 11,
    Nullable = 1 << 12,
    Abstract = 1 << 13,
    GenericTypeParameter = 1 << 14,
    GenericMethodParameter = 1 << 15,
    ByRefLike = 1 << 16,
    TypeDefinition = 1 << 23,
}

// The flag word of a type's runtime type table entry: CoreLib's flags, the array rank (a generic
// parameter's GenericParameterAttributes, as it has no rank) and the TypeCode, which Type's queries
// read in place.
internal static class RuntimeTypeFactsEncoding
{
    internal const int ArrayRankShift = 17;
    internal const int TypeCodeShift = 24;

    // A generic type parameter's facts come from its owner's definition.
    internal static uint Word(CliTypeIdentity identity, TypeDefinitionModel? definition, TypeDefinitionModel? owner = null)
    {
        if (definition is null && identity.Shape is not (
                CliTypeShape.GenericTypeParameter or
                CliTypeShape.GenericMethodParameter))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"runtime type facts for '{identity}' require a type definition"));
        }
        var parameter = identity.Shape == CliTypeShape.GenericTypeParameter &&
            owner is not null && identity.GenericParameterIndex < owner.GenericParameterAttributes.Length
            ? owner.GenericParameterAttributes[identity.GenericParameterIndex]
            : 0;
        var rank = identity.Shape switch
        {
            CliTypeShape.SzArray => 1,
            CliTypeShape.Array => identity.ArrayRank,
            _ => parameter,
        };
        return (uint)Flags(identity, definition, parameter) | (uint)rank << ArrayRankShift |
            (uint)TypeCode(identity, definition) << TypeCodeShift;
    }

    private const int NotNullableValueTypeConstraint = 0x08;

    private static RuntimeTypeFactsFlags Flags(
        CliTypeIdentity identity,
        TypeDefinitionModel? definition,
        int parameter)
    {
        var flags = RuntimeTypeFactsFlags.None;
        var hasDefinitionSemantics = identity.Shape is
            CliTypeShape.Named or CliTypeShape.GenericInstantiation;
        var isElementModifier = identity.Shape is
            CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer;
        // A generic parameter with the struct constraint is a value type, as .NET reports it.
        var isRuntimeValueType = identity.IsValueType && !isElementModifier ||
            (parameter & NotNullableValueTypeConstraint) != 0;
        if (isRuntimeValueType)
        {
            flags |= RuntimeTypeFactsFlags.ValueType;
        }
        if (hasDefinitionSemantics && definition!.IsEnum)
        {
            flags |= RuntimeTypeFactsFlags.Enum;
        }
        if (hasDefinitionSemantics && definition!.IsInterface)
        {
            flags |= RuntimeTypeFactsFlags.Interface;
        }
        if (identity.Shape == CliTypeShape.ManagedByReference)
        {
            flags |= RuntimeTypeFactsFlags.ByReference;
        }
        if (identity.Shape == CliTypeShape.UnmanagedPointer)
        {
            flags |= RuntimeTypeFactsFlags.Pointer;
        }
        if (identity.ContainsGenericParameters ||
            identity.Shape == CliTypeShape.Named && definition!.GenericArity != 0)
        {
            flags |= RuntimeTypeFactsFlags.ContainsGenericParameters;
        }
        if (identity.Shape == CliTypeShape.Named && definition!.GenericArity != 0)
        {
            flags |= RuntimeTypeFactsFlags.GenericTypeDefinition;
        }
        if (identity.Shape == CliTypeShape.GenericInstantiation ||
            identity.Shape == CliTypeShape.Named && definition!.GenericArity != 0)
        {
            flags |= RuntimeTypeFactsFlags.GenericType;
        }
        if (identity.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
        {
            flags |= RuntimeTypeFactsFlags.Array |
                RuntimeTypeFactsFlags.Class |
                RuntimeTypeFactsFlags.Sealed;
        }
        else if (!isRuntimeValueType &&
                 (isElementModifier || identity.Shape is CliTypeShape.GenericTypeParameter or
                     CliTypeShape.GenericMethodParameter || !definition!.IsInterface))
        {
            flags |= RuntimeTypeFactsFlags.Class;
        }
        if (definition is { IsSealed: true } && !isElementModifier)
        {
            flags |= RuntimeTypeFactsFlags.Sealed;
        }
        if (hasDefinitionSemantics && definition!.IsByRefLike)
        {
            flags |= RuntimeTypeFactsFlags.ByRefLike;
        }
        if (hasDefinitionSemantics && definition!.IsAbstract)
        {
            flags |= RuntimeTypeFactsFlags.Abstract;
        }
        if (identity.Shape == CliTypeShape.SzArray)
        {
            flags |= RuntimeTypeFactsFlags.SzArray;
        }
        if (identity.Shape == CliTypeShape.GenericInstantiation &&
            definition!.FullName == "System.Nullable`1")
        {
            flags |= RuntimeTypeFactsFlags.Nullable;
        }
        if (identity.Shape == CliTypeShape.GenericTypeParameter)
        {
            flags |= RuntimeTypeFactsFlags.GenericTypeParameter;
        }
        if (identity.Shape == CliTypeShape.GenericMethodParameter)
        {
            flags |= RuntimeTypeFactsFlags.GenericMethodParameter;
        }
        // A type definition: a named or primitive type, generic definitions included, never a
        // constructed, array, pointer, by-reference or parameter type.
        if (identity.Shape is CliTypeShape.Named or CliTypeShape.Primitive)
        {
            flags |= RuntimeTypeFactsFlags.TypeDefinition;
        }
        return flags;
    }

    private static int TypeCode(
        CliTypeIdentity identity,
        TypeDefinitionModel? definition)
    {
        if (identity.Shape is (
                CliTypeShape.Named or CliTypeShape.GenericInstantiation) &&
            definition!.IsEnum)
        {
            return PrimitiveTypeCode(definition.EnumUnderlyingType);
        }
        return identity.FullName switch
        {
            "System.DBNull" => 2,
            "System.Decimal" => 15,
            "System.DateTime" => 16,
            _ => PrimitiveTypeCode(identity),
        };
    }

    private static int PrimitiveTypeCode(CliTypeIdentity identity) =>
        identity.CanonicalName switch
        {
            "primitive:bool" => 3,
            "primitive:char" => 4,
            "primitive:i1" => 5,
            "primitive:u1" => 6,
            "primitive:i2" => 7,
            "primitive:u2" => 8,
            "primitive:i4" => 9,
            "primitive:u4" => 10,
            "primitive:i8" => 11,
            "primitive:u8" => 12,
            "primitive:f4" => 13,
            "primitive:f8" => 14,
            "primitive:string" => 18,
            _ => 1,
        };
}
