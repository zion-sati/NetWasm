// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.StructResults;

// A value type returns in results when its instance fields, flattened through nested value types, are at most
// MaximumResults scalars (integers, floating point, native integers, references, byrefs and v128 values) that do not
// overlap. A value type with an explicit layout, a declared size, an inline array or no field keeps the buffer: each
// has bytes its fields do not cover, or covers some twice, which a copy field by field would not carry.
internal sealed class StructResultClassifier(
    ITypeDefinitionResolver types,
    IFieldRepository fields,
    IInstanceFieldLayoutProvider fieldLayouts) : IStructResultClassifier
{
    // Four results carry ValueTask<T>, Memory<T>, ArraySegment<T>, KeyValuePair<TKey, TValue> of two words and a
    // span; more would be more results than registers to return them in.
    internal const int MaximumResults = 4;

    private readonly ITypeDefinitionResolver _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IInstanceFieldLayoutProvider _fieldLayouts = fieldLayouts ??
        throw new ArgumentNullException(nameof(fieldLayouts));
    private readonly ConcurrentDictionary<CliTypeIdentity, StructResultShape?> _shapes = new();

    public StructResultShape? Classify(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.StackKind == CliValueKind.ValueType ? _shapes.GetOrAdd(type, Create) : null;
    }

    private StructResultShape? Create(CliTypeIdentity type)
    {
        var flattened = new List<StructResultField>();
        if (!Flatten(type, 0, flattened, []) || flattened.Count == 0) return null;
        var ordered = flattened.OrderBy(static field => field.Offset).ToImmutableArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index].Offset < ordered[index - 1].Offset + ordered[index - 1].Size) return null;
        }
        return new(type, ordered);
    }

    // Adds the value type's scalars at their offsets from the outermost value; false when it cannot return in results.
    private bool Flatten(CliTypeIdentity type, int offset, List<StructResultField> result, HashSet<CliTypeIdentity> active)
    {
        if (!type.IsValueType || type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation) ||
            type.ContainsGenericParameters || !active.Add(type))
        {
            return false;
        }
        var definition = _types.ResolveTypeIdentity(type);
        if (definition.IsEnum || definition.LayoutKind == CliTypeLayoutKind.Explicit || definition.DeclaredSize != 0 ||
            definition.InlineArrayLength != 0 ||
            definition.Namespace == "System" && definition.Name is "TypedReference" or "ArgIterator" or "RuntimeArgumentHandle")
        {
            return false;
        }
        var typeArguments = type.Shape == CliTypeShape.GenericInstantiation ? type.TypeArguments : [];
        foreach (var key in definition.Fields)
        {
            var field = _fields.GetField(key);
            if (field.IsStatic || field.IsLiteral) continue;
            var fieldType = field.SignatureType.Substitute(typeArguments, []);
            if (fieldType.Shape == CliTypeShape.Modified) return false;
            var layout = _fieldLayouts.GetFieldLayout(new FieldInstanceModel(field, type, fieldType));
            var at = offset + layout.Offset;
            // An enum a type reference names (CoreLib's DayOfWeek in a user assembly's ValueTask<DayOfWeek>) is a value type
            // there; only its definition says it is an integer. It is that integer, so the value classifies the same
            // however its identity was decoded, at a call and in the callee's signature alike.
            if (fieldType.StackKind == CliValueKind.ValueType && _types.ResolveTypeIdentity(fieldType) is { IsEnum: true } enumeration)
            {
                fieldType = CliTypeIdentity.FromDefinition(enumeration);
            }
            switch (fieldType.StackKind)
            {
                case CliValueKind.ValueType:
                    if (!Flatten(fieldType, at, result, active)) return false;
                    break;
                case CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or CliValueKind.NativeInt or
                    CliValueKind.ManagedReference or CliValueKind.ManagedAddress or CliValueKind.V128:
                    result.Add(new(at, layout.Size, fieldType, fieldType.StackKind));
                    break;
                default:
                    return false;
            }
            if (result.Count > MaximumResults) return false;
        }
        active.Remove(type);
        return true;
    }
}
