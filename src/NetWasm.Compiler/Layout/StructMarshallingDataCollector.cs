// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

// Classifies the program's marshalled types into the pending descriptors StructMarshallingDataBuilder
// writes; it runs before static strings are assigned, so marshalled field names get storage.
internal sealed class StructMarshallingDataCollector(
    ITypeDefinitionResolver typeDefinitions,
    IFieldRepository fields,
    ImmutableArray<MethodDefinitionModel> methods,
    IMetadataIdentityBaseTypeResolver baseTypes,
    INativeStructSource structs,
    ReachableProgram program,
    ManagedTypeLayouts types,
    ManagedStaticDataBuildState state) : IStructMarshallingDataCollector
{
    internal const int InvalidLayout = 1;
    internal const int RequiresConversion = 2;
    internal const int ValueType = 4;
    internal const int DefaultConstructor = 8;
    // A structure that holds a bool, char, string, delegate or inline array, or a formatted class: its size and field
    // offsets are its native layout's, and its conversion steps copy it to that layout and back, as desktop .NET's
    // runtime marshalling lays it out on Unix (NativeStructLayouts.PlanStructure).
    internal const int NativeLayout = 16;
    // A structure with a field desktop .NET's marshalling rejects (an object, an array without ByValArray, a
    // StringBuilder, a class without layout): SizeOf and OffsetOf throw ArgumentException, and the members that
    // convert throw TypeLoadException.
    internal const int UnmarshalableField = 32;
    // A formatted class on a base without layout: desktop .NET cannot load the type, so every member throws
    // TypeLoadException.
    internal const int InvalidType = 64;

    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IMetadataIdentityBaseTypeResolver _baseTypes = baseTypes ??
        throw new ArgumentNullException(nameof(baseTypes));
    private readonly INativeStructSource _structs = structs ?? throw new ArgumentNullException(nameof(structs));
    private readonly ReachableProgram _program = program ?? throw new ArgumentNullException(nameof(program));
    private readonly ManagedTypeLayouts _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly ManagedStaticDataBuildState _state = state ?? throw new ArgumentNullException(nameof(state));

    public void Collect()
    {
        if (_program.StructMarshallingTypes.IsEmpty)
            return;
        var constructors = methods
            .Where(method => method.Name == ".ctor" && !method.IsStatic &&
                method.Signature.ParameterTypes.IsEmpty)
            .Select(method => method.DeclaringType)
            .ToHashSet();
        foreach (var type in _program.StructMarshallingTypes)
        {
            var definition = _typeDefinitions.ResolveTypeIdentity(type);
            if (!_types.Objects.TryGetValue(definition.Key, out var objectLayout))
                continue;
            var flags = type.IsValueType ? ValueType : 0;
            if (constructors.Contains(definition.Key))
                flags |= DefaultConstructor;
            // An enum is blittable as a field but, as on CoreCLR, cannot be marshaled on its own.
            var shape = definition.IsEnum
                ? Shape.Invalid
                : type.IsValueType
                ? Describe(type, [])
                : definition.LayoutKind == CliTypeLayoutKind.Auto || definition.GenericArity != 0
                    ? Shape.Invalid
                    : Shape.Conversion;
            flags |= shape.Kind switch
            {
                ShapeKind.Invalid => InvalidLayout,
                ShapeKind.Conversion => RequiresConversion,
                _ => 0,
            };
            var size = 0;
            var payloadOffset = 0;
            ImmutableArray<(string Name, int Offset)> members = [];
            ImmutableArray<NativeConversionStep> steps = [];
            var plan = shape.Kind == ShapeKind.Conversion
                ? NativeStructLayouts.PlanStructure(type, _structs, _types.Target.AddressSize, _baseTypes)
                : null;
            if (plan is { Layout: { } native })
            {
                flags = (flags & ~RequiresConversion) | NativeLayout;
                size = native.Size;
                // A struct's managed offsets count from its boxed payload, a class's from the object.
                payloadOffset = type.IsValueType
                    ? WasmTargetLayout.Align(_types.Target.ObjectHeaderSize, ValueLayout(type).Alignment)
                    : 0;
                // OffsetOf finds the type's own fields and the non-private fields it inherits, as desktop .NET does.
                members = [.. native.Fields
                    .Where(field => field.Field.DeclaringType == definition.Key || !field.Field.IsPrivate)
                    .Select(field => (field.Field.Name, field.Offset))];
                steps = [.. Steps(native, 0, 0)];
            }
            else if (plan is { InvalidBase: true })
            {
                flags = (flags & ~RequiresConversion) | InvalidType;
            }
            else if (plan is { Rejected: true })
            {
                flags = (flags & ~RequiresConversion) | UnmarshalableField;
            }
            else if (shape.Kind == ShapeKind.Blittable)
            {
                var layout = ValueLayout(type);
                size = layout.Size;
                payloadOffset = WasmTargetLayout.Align(_types.Target.ObjectHeaderSize, layout.Alignment);
                members = [.. definition.Fields.Select(_fields.GetField)
                    .Where(field => !field.IsStatic)
                    .Select(field => (field.Name, _types.ValueLayoutState.Fields[field.Key].Offset))];
            }
            var constructor = _program.StructMarshallingConstructors.GetValueOrDefault(type);
            _state.PendingStructMarshalling.Add(new(type, objectLayout.TypeId, flags, size, payloadOffset, members)
            {
                Steps = steps,
                Constructor = constructor,
                BoxPayloadOffset = constructor is not null && type.IsValueType
                    ? WasmTargetLayout.Align(_types.Target.ObjectHeaderSize, ValueLayout(type).Alignment)
                    : 0,
            });
        }
    }

    // Blittable storage is copied as is; bool, char, references and formatted classes need
    // conversion stubs; generic, auto-layout, byref-like and recursive layouts cannot be marshaled.
    private Shape Describe(CliTypeIdentity type, HashSet<CliTypeIdentity> active)
    {
        if (type.Shape is CliTypeShape.UnmanagedPointer or CliTypeShape.FunctionPointer)
            return Shape.Blittable;
        if (type.Shape == CliTypeShape.Primitive)
        {
            return type.CanonicalName switch
            {
                "primitive:i1" or "primitive:u1" or "primitive:i2" or "primitive:u2" or
                "primitive:i4" or "primitive:u4" or "primitive:i8" or "primitive:u8" or
                "primitive:f4" or "primitive:f8" or "primitive:nativeint" or "primitive:nativeuint" =>
                    Shape.Blittable,
                "primitive:void" or "primitive:typedref" => Shape.Invalid,
                _ => Shape.Conversion,
            };
        }
        if (!type.IsValueType)
            return Shape.Conversion;
        if (type.Shape != CliTypeShape.Named || type.ContainsGenericParameters || !active.Add(type))
            return Shape.Invalid;
        var definition = _typeDefinitions.ResolveTypeIdentity(type);
        if (definition.IsEnum)
        {
            var underlying = Describe(definition.EnumUnderlyingType, active);
            active.Remove(type);
            return underlying;
        }
        if (definition.GenericArity != 0 || definition.IsByRefLike ||
            definition.LayoutKind == CliTypeLayoutKind.Auto ||
            definition.PackingSize is not (0 or 1 or 2 or 4 or 8 or 16 or 32 or 64 or 128) ||
            !_types.ValueLayoutState.Values.TryGetValue(type, out var layout) ||
            !layout.ByReferenceOffsets.IsEmpty)
        {
            active.Remove(type);
            return Shape.Invalid;
        }
        var result = Shape.Blittable;
        foreach (var field in definition.Fields.Select(_fields.GetField).Where(field => !field.IsStatic))
        {
            var child = Describe(field.SignatureType, active);
            if (child.Kind == ShapeKind.Invalid ||
                !_types.ValueLayoutState.Fields.ContainsKey(field.Key))
            {
                result = Shape.Invalid;
                break;
            }
            if (child.Kind == ShapeKind.Conversion)
                result = Shape.Conversion;
        }
        active.Remove(type);
        return result;
    }

    // A native layout's fields as copies and conversions between the managed value and the native copy, a nested
    // struct's fields at their offsets within it.
    private IEnumerable<NativeConversionStep> Steps(NativeStructLayout layout, int managedBase, int nativeBase)
    {
        foreach (var field in layout.Fields)
        {
            var managed = managedBase + _types.ValueLayoutState.Fields[field.Field.Key].Offset;
            var native = nativeBase + field.Offset;
            if (field.Nested is { CopiesWhole: false } nested)
            {
                foreach (var step in Steps(nested, managed, native))
                    yield return step;
                continue;
            }
            if (field.Reference is { } reference)
            {
                // An embedded class or an array of structures converts through its type's own descriptor, and an
                // array of an enum is created as one: the step names the type by its ID.
                yield return reference.Kind is NativeReferenceKind.EmbeddedClass or NativeReferenceKind.ArrayStructure or
                    NativeReferenceKind.ArrayEnum
                    ? new(Kind(reference.Kind), managed, native, field.Size)
                    {
                        Count = reference.Count,
                        TypeId = TypeId(reference.Element!),
                    }
                    : new(Kind(reference.Kind), managed, native, field.Size)
                    {
                        Count = reference.Count,
                        Element = reference.Element is { } element ? Elements[element.CanonicalName] : NativeArrayElement.None,
                    };
                continue;
            }
            yield return field.Conversion?.Conversion switch
            {
                NativeScalarConversion.Boolean => new(NativeConversionKind.Boolean, managed, native, field.Size),
                NativeScalarConversion.AnsiChar => new(NativeConversionKind.Ansi, managed, native, 1),
                _ => new(NativeConversionKind.Copy, managed, native, field.Size),
            };
        }
    }

    // The ID of a type the program lays out: the root planner makes every embedded class and array element a type the
    // program creates.
    private int TypeId(CliTypeIdentity type) => _types.Objects[_typeDefinitions.ResolveTypeIdentity(type).Key].TypeId;

    private static NativeConversionKind Kind(NativeReferenceKind kind) => kind switch
    {
        NativeReferenceKind.Utf8Pointer => NativeConversionKind.Utf8String,
        NativeReferenceKind.Utf16Pointer => NativeConversionKind.Utf16String,
        NativeReferenceKind.Utf8Inline => NativeConversionKind.Utf8Inline,
        NativeReferenceKind.Utf16Inline => NativeConversionKind.Utf16Inline,
        NativeReferenceKind.Delegate => NativeConversionKind.Delegate,
        NativeReferenceKind.ArrayCopy => NativeConversionKind.ArrayCopy,
        NativeReferenceKind.ArrayBoolean => NativeConversionKind.ArrayBoolean,
        NativeReferenceKind.ArrayAnsi => NativeConversionKind.ArrayAnsi,
        NativeReferenceKind.SafeHandle => NativeConversionKind.SafeHandle,
        NativeReferenceKind.CriticalHandle => NativeConversionKind.CriticalHandle,
        NativeReferenceKind.EmbeddedClass => NativeConversionKind.EmbeddedClass,
        NativeReferenceKind.ArrayStructure => NativeConversionKind.ArrayStructure,
        _ => NativeConversionKind.ArrayEnum,
    };

    // An inline array's element: a primitive, a bool or a char (NativeStructLayouts); a structure or an enum names its
    // type instead.
    private static readonly ImmutableDictionary<string, NativeArrayElement> Elements = new Dictionary<string, NativeArrayElement>
    {
        ["primitive:i1"] = NativeArrayElement.SByte,
        ["primitive:u1"] = NativeArrayElement.Byte,
        ["primitive:i2"] = NativeArrayElement.Int16,
        ["primitive:u2"] = NativeArrayElement.UInt16,
        ["primitive:i4"] = NativeArrayElement.Int32,
        ["primitive:u4"] = NativeArrayElement.UInt32,
        ["primitive:i8"] = NativeArrayElement.Int64,
        ["primitive:u8"] = NativeArrayElement.UInt64,
        ["primitive:f4"] = NativeArrayElement.Single,
        ["primitive:f8"] = NativeArrayElement.Double,
        ["primitive:nativeint"] = NativeArrayElement.IntPtr,
        ["primitive:nativeuint"] = NativeArrayElement.UIntPtr,
        ["primitive:bool"] = NativeArrayElement.Boolean,
        ["primitive:char"] = NativeArrayElement.Char,
    }.ToImmutableDictionary();

    private ValueLayout ValueLayout(CliTypeIdentity type) =>
        _types.ValueLayoutState.Values.TryGetValue(type, out var layout)
            ? layout
            : type.Shape == CliTypeShape.Primitive
                ? new ValueLayout(type, PrimitiveSize(type), PrimitiveSize(type), [])
                : throw new CompilerException(new CompilerDiagnostic(DiagnosticCode.RuntimeContract,
                    $"value layout for marshalled type '{type.CanonicalName}' was not generated"));

    private int PrimitiveSize(CliTypeIdentity type) => type.CanonicalName switch
    {
        "primitive:i1" or "primitive:u1" => 1,
        "primitive:i2" or "primitive:u2" => 2,
        "primitive:i4" or "primitive:u4" or "primitive:f4" => 4,
        "primitive:i8" or "primitive:u8" or "primitive:f8" => 8,
        _ => _types.Target.AddressSize,
    };

    private enum ShapeKind
    {
        Blittable,
        Conversion,
        Invalid,
    }

    private readonly record struct Shape(ShapeKind Kind)
    {
        public static Shape Blittable => new(ShapeKind.Blittable);
        public static Shape Conversion => new(ShapeKind.Conversion);
        public static Shape Invalid => new(ShapeKind.Invalid);
    }
}

internal interface IStructMarshallingDataCollector
{
    void Collect();
}

internal sealed class EmptyStructMarshallingDataCollector : IStructMarshallingDataCollector
{
    public static EmptyStructMarshallingDataCollector Instance { get; } = new();

    public void Collect()
    {
    }
}
