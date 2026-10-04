using System;
using System.Buffers.Binary;
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
}

internal static class RuntimeTypeFactsEncoding
{
    private const RuntimeTypeNamePayload KnownNamePayload =
        RuntimeTypeNamePayload.Name |
        RuntimeTypeNamePayload.Namespace |
        RuntimeTypeNamePayload.FullName |
        RuntimeTypeNamePayload.DisplayName;

    internal const int FlagsOffset = 0;
    internal const int TypeCodeOffset = 4;
    internal const int BaseTypeIdOffset = 8;
    internal const int AssignableTypeCountOffset = 12;
    internal const int AssignableTypeIdsOffset = 16;

    internal static int DelegateInvokeOffset(WasmTargetLayout target) =>
        AssignableTypeIdsOffset + target.AddressSize;

    internal static int NameFactsOffset(WasmTargetLayout target) =>
        DelegateInvokeOffset(target) + target.AddressSize;

    internal static int GenericArgumentCountOffset(WasmTargetLayout target) =>
        NameFactsOffset(target) + target.AddressSize;

    internal static int GenericArgumentTypeIdsOffset(WasmTargetLayout target) =>
        ManagedTypeLayoutCompiler.Align(
            GenericArgumentCountOffset(target) + sizeof(int),
            target.AddressSize);

    internal static int Size(WasmTargetLayout target) =>
        GenericArgumentTypeIdsOffset(target) + target.AddressSize;

    internal const int NamePayloadOffset = 0;

    internal static int NameOffset(WasmTargetLayout target) =>
        ManagedTypeLayoutCompiler.Align(sizeof(int), target.AddressSize);

    internal static int NamespaceOffset(WasmTargetLayout target) =>
        NameOffset(target) + target.AddressSize;

    internal static int FullNameOffset(WasmTargetLayout target) =>
        NamespaceOffset(target) + target.AddressSize;

    internal static int DisplayNameOffset(WasmTargetLayout target) =>
        FullNameOffset(target) + target.AddressSize;

    internal static int NamesSize(WasmTargetLayout target) =>
        DisplayNameOffset(target) + target.AddressSize;

    internal static int AddNames(
        ManagedStaticDataBuildState state,
        WasmTargetLayout target,
        RuntimeTypeNames names)
    {
        if (names.Payload == RuntimeTypeNamePayload.None ||
            (names.Payload & ~KnownNamePayload) != 0)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"runtime type-name payload '{names.Payload}' is invalid"));
        }
        state.Cursor = ManagedTypeLayoutCompiler.Align(state.Cursor, target.AddressSize);
        var address = state.Cursor;
        var bytes = new byte[NamesSize(target)];
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(NamePayloadOffset),
            (int)names.Payload);
        WriteAddress(
            bytes,
            NameOffset(target),
            target,
            GetStringAddress(state, names.Name));
        WriteAddress(
            bytes,
            NamespaceOffset(target),
            target,
            GetStringAddress(state, names.Namespace));
        WriteAddress(
            bytes,
            FullNameOffset(target),
            target,
            GetStringAddress(state, names.FullName));
        WriteAddress(
            bytes,
            DisplayNameOffset(target),
            target,
            GetStringAddress(state, names.DisplayName));
        state.Segments.Add(new DataSegment(address, [.. bytes]));
        state.Cursor += bytes.Length;
        return address;
    }

    internal static void Add(
        ManagedStaticDataBuildState state,
        WasmTargetLayout target,
        CliTypeIdentity identity,
        TypeDefinitionModel? definition,
        int typeId,
        int baseTypeId,
        int assignableTypeIdsAddress,
        int assignableTypeIdCount,
        int delegateInvokeAddress = 0,
        int nameFactsAddress = 0,
        int genericArgumentTypeIdsAddress = 0,
        int genericArgumentCount = 0)
    {
        if (definition is null && identity.Shape is not (
                CliTypeShape.GenericTypeParameter or
                CliTypeShape.GenericMethodParameter))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"runtime type facts for '{identity}' require a type definition"));
        }
        if (genericArgumentCount < 0 ||
            (genericArgumentCount == 0) != (genericArgumentTypeIdsAddress == 0))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                "runtime generic-argument metadata is inconsistent"));
        }
        state.Cursor = ManagedTypeLayoutCompiler.Align(
            state.Cursor,
            target.AddressSize);
        var address = state.Cursor;
        var bytes = new byte[Size(target)];
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(FlagsOffset),
            (int)Flags(identity, definition));
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(TypeCodeOffset),
            TypeCode(identity, definition));
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(BaseTypeIdOffset),
            baseTypeId);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(AssignableTypeCountOffset),
            assignableTypeIdCount);
        WriteAddress(
            bytes.AsSpan(AssignableTypeIdsOffset),
            target,
            assignableTypeIdsAddress);
        WriteAddress(
            bytes.AsSpan(DelegateInvokeOffset(target)),
            target,
            delegateInvokeAddress);
        WriteAddress(
            bytes.AsSpan(NameFactsOffset(target)),
            target,
            nameFactsAddress);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(GenericArgumentCountOffset(target)),
            genericArgumentCount);
        WriteAddress(
            bytes.AsSpan(GenericArgumentTypeIdsOffset(target)),
            target,
            genericArgumentTypeIdsAddress);
        if (!state.TypeFacts.TryAdd(typeId, address))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"semantic type ID '{typeId}' has multiple type-facts records"));
        }
        state.Segments.Add(new DataSegment(address, [.. bytes]));
        state.Cursor += bytes.Length;
    }

    private static RuntimeTypeFactsFlags Flags(
        CliTypeIdentity identity,
        TypeDefinitionModel? definition)
    {
        var flags = RuntimeTypeFactsFlags.None;
        var hasDefinitionSemantics = identity.Shape is
            CliTypeShape.Named or CliTypeShape.GenericInstantiation;
        var isElementModifier = identity.Shape is
            CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer;
        var isRuntimeValueType = identity.IsValueType && !isElementModifier;
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
        else if (identity.Shape is not (
                     CliTypeShape.GenericTypeParameter or
                     CliTypeShape.GenericMethodParameter) &&
                 !isRuntimeValueType &&
                 (isElementModifier || !definition!.IsInterface))
        {
            flags |= RuntimeTypeFactsFlags.Class;
        }
        if (definition is { IsSealed: true } && !isElementModifier)
        {
            flags |= RuntimeTypeFactsFlags.Sealed;
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

    private static void WriteAddress(
        Span<byte> destination,
        WasmTargetLayout target,
        int value)
    {
        if (target.AddressSize == sizeof(int))
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteInt64LittleEndian(destination, value);
        }
    }

    private static void WriteAddress(
        byte[] destination,
        int offset,
        WasmTargetLayout target,
        int value) => WriteAddress(destination.AsSpan(offset), target, value);

    private static int GetStringAddress(
        ManagedStaticDataBuildState state,
        string? value)
    {
        if (value is null)
        {
            return 0;
        }
        return state.Strings.TryGetValue(value, out var layout)
            ? layout.Address
            : throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"type name '{value}' was not assigned static storage"));
    }
}
