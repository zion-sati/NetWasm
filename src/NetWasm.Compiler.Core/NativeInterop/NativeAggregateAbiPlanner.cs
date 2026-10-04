using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.Core.NativeInterop;

/// <summary>Classifies completed managed storage against the Wasm Basic C ABI.</summary>
public sealed class NativeAggregateAbiPlanner(
    ITypeDefinitionResolver types,
    IFieldRepository fields,
    IValueLayoutProvider values,
    IInstanceFieldLayoutProvider fieldLayouts) : INativeAggregateAbiPlanner
{
    private readonly ITypeDefinitionResolver _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IValueLayoutProvider _values = values ?? throw new ArgumentNullException(nameof(values));
    private readonly IInstanceFieldLayoutProvider _fieldLayouts = fieldLayouts ?? throw new ArgumentNullException(nameof(fieldLayouts));

    public NativeAbiValuePlan Plan(CliTypeIdentity type, string methodName)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        if (!type.IsValueType || type.ContainsGenericParameters ||
            type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation))
            throw Unsupported(methodName, "Native aggregates must be closed value types.");

        var shape = Describe(type, methodName, []);
        if (shape.IsEmpty)
            return new(NativeAbiValueKind.IgnoredAggregate, type, null,
                shape.Layout.Size, shape.Layout.Alignment);
        if (shape.ScalarCount == 1 && shape.ScalarOffset == 0 &&
            shape.Layout.Size == shape.ScalarSize &&
            shape.Layout.Alignment <= shape.NaturalAlignment)
            return new(type.StackKind == CliValueKind.ValueType
                    ? NativeAbiValueKind.ScalarizedAggregate : NativeAbiValueKind.Scalar,
                type, shape.ScalarType, shape.Layout.Size, shape.Layout.Alignment,
                shape.ScalarType, shape.ScalarOffset);

        return new(NativeAbiValueKind.IndirectAggregate, type,
            CliTypeIdentity.FromStackKind(CliValueKind.NativeInt),
            shape.Layout.Size, shape.Layout.Alignment);
    }

    private AggregateShape Describe(CliTypeIdentity type, string methodName, HashSet<CliTypeIdentity> active)
    {
        if (type.ContainsGenericParameters)
            throw Unsupported(methodName, "Native aggregate fields must be closed.");
        if (type.Shape == CliTypeShape.UnmanagedPointer ||
            type.Shape == CliTypeShape.Primitive && IsBlittableScalar(type))
        {
            var scalar = _values.GetValueLayout(type);
            ValidateStorage(scalar, methodName);
            return new(scalar, 1, type.Shape == CliTypeShape.UnmanagedPointer
                ? CliTypeIdentity.FromStackKind(CliValueKind.NativeInt) : type,
                0, scalar.Size, scalar.Alignment, false);
        }
        if (!type.IsValueType || type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation))
            throw Unsupported(methodName, "Native aggregate fields must contain only blittable scalars, pointers or aggregates.");
        if (!active.Add(type))
            throw Unsupported(methodName, "A native aggregate cannot recursively contain itself.");

        var definition = _types.ResolveTypeIdentity(type);
        if (definition.IsEnum)
        {
            var underlying = Describe(definition.EnumUnderlyingType, methodName, active);
            active.Remove(type);
            return underlying;
        }
        if (definition.LayoutKind == CliTypeLayoutKind.Auto ||
            definition.PackingSize is not (0 or 1 or 2 or 4 or 8 or 16 or 32 or 64 or 128) ||
            definition.DeclaredSize < 0 || definition.InlineArrayLength < 0)
            throw Unsupported(methodName, "Native aggregates require valid sequential or explicit layout.");
        var layout = _values.GetValueLayout(type);
        ValidateStorage(layout, methodName);
        var instanceFields = definition.Fields.Select(_fields.GetField)
            .Where(field => !field.IsStatic).ToArray();
        if (definition.InlineArrayLength != 0 && instanceFields.Length != 1)
            throw Unsupported(methodName, "A native inline array must have one instance field.");

        var scalarCount = 0;
        CliTypeIdentity? scalarType = null;
        var scalarOffset = 0;
        var scalarSize = 0;
        var naturalAlignment = 1;
        var aggregateAlignment = 1;
        foreach (var field in instanceFields)
        {
            var fieldType = field.SignatureType.Substitute(type.TypeArguments);
            var child = Describe(fieldType, methodName, active);
            if (child.IsEmpty)
                throw Unsupported(methodName, "Embedded empty aggregates have different managed and C storage and are not supported.");
            var position = _fieldLayouts.GetFieldLayout(new FieldInstanceModel(field, type, fieldType));
            var repetitions = definition.InlineArrayLength == 0 ? 1 : definition.InlineArrayLength;
            var alignment = definition.PackingSize == 0
                ? child.Layout.Alignment : Math.Min(definition.PackingSize, child.Layout.Alignment);
            if (position.Offset < 0 || position.Offset % alignment != 0 ||
                checked((long)position.Offset + (long)child.Layout.Size * repetitions) > layout.Size)
                throw Unsupported(methodName, "Native aggregate field storage is outside its qualified layout.");
            aggregateAlignment = Math.Max(aggregateAlignment, alignment);
            naturalAlignment = Math.Max(naturalAlignment, child.NaturalAlignment);
            if (scalarCount == 0 && child.ScalarCount == 1 && repetitions == 1)
            {
                scalarType = child.ScalarType;
                scalarOffset = checked(position.Offset + child.ScalarOffset);
                scalarSize = child.ScalarSize;
            }
            // Only zero, one and multiple matter. Avoid arithmetic proportional
            // to an inline-array count while preserving the exact ABI category.
            scalarCount = child.ScalarCount == 0 ? scalarCount
                : scalarCount != 0 || child.ScalarCount > 1 || repetitions > 1 ? 2 : 1;
        }
        if (instanceFields.Length != 0 && layout.Alignment < aggregateAlignment)
            throw Unsupported(methodName, "Managed aggregate alignment does not match the declared native field layout.");
        active.Remove(type);
        return new(layout, scalarCount, scalarType, scalarOffset, scalarSize,
            naturalAlignment, instanceFields.Length == 0 && definition.DeclaredSize == 0);
    }

    private static void ValidateStorage(ValueLayout layout, string methodName)
    {
        if (layout.ContainsReferences || !layout.ByReferenceOffsets.IsEmpty ||
            layout.Size <= 0 || layout.Alignment <= 0 ||
            (layout.Alignment & (layout.Alignment - 1)) != 0 ||
            layout.Alignment > 16 || layout.Size % layout.Alignment != 0)
            throw Unsupported(methodName, "Native storage must be reference-free, aligned, and representable in a 16-byte-aligned value frame.");
    }

    private static bool IsBlittableScalar(CliTypeIdentity type) =>
        type.CanonicalName is "primitive:i1" or "primitive:u1" or
            "primitive:i2" or "primitive:u2" or "primitive:i4" or "primitive:u4" or
            "primitive:i8" or "primitive:u8" or "primitive:f4" or "primitive:f8" or
            "primitive:nativeint" or "primitive:nativeuint";

    private static CompilerException Unsupported(string methodName, string message) =>
        new(new(DiagnosticCode.NativeInterop, message, methodName));

    private sealed record AggregateShape(ValueLayout Layout, int ScalarCount,
        CliTypeIdentity? ScalarType, int ScalarOffset, int ScalarSize,
        int NaturalAlignment, bool IsEmpty);
}
