using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

// Classifies the program's marshalled types into the pending descriptors StructMarshallingDataBuilder
// writes; it runs before static strings are assigned, so marshalled field names get storage.
internal sealed class StructMarshallingDataCollector(
    ITypeDefinitionResolver typeDefinitions,
    IFieldRepository fields,
    ImmutableArray<MethodDefinitionModel> methods,
    ReachableProgram program,
    ManagedTypeLayouts types,
    ManagedStaticDataBuildState state) : IStructMarshallingDataCollector
{
    internal const int InvalidLayout = 1;
    internal const int RequiresConversion = 2;
    internal const int ValueType = 4;
    internal const int DefaultConstructor = 8;

    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
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
            if (type.IsValueType && constructors.Contains(definition.Key))
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
            if (shape.Kind == ShapeKind.Blittable)
            {
                var layout = ValueLayout(type);
                size = layout.Size;
                payloadOffset = WasmTargetLayout.Align(_types.Target.ObjectHeaderSize, layout.Alignment);
                members = [.. definition.Fields.Select(_fields.GetField)
                    .Where(field => !field.IsStatic)
                    .Select(field => (field.Name, _types.ValueLayoutState.Fields[field.Key].Offset))];
            }
            _state.PendingStructMarshalling.Add(new(type, objectLayout.TypeId, flags, size, payloadOffset, members));
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
