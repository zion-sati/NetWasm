// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface ISharedArrayTargetDataBuilder
{
    void Build();
}

internal sealed class EmptySharedArrayTargetDataBuilder : ISharedArrayTargetDataBuilder
{
    public static EmptySharedArrayTargetDataBuilder Instance { get; } = new();

    private EmptySharedArrayTargetDataBuilder()
    {
    }

    public void Build()
    {
    }
}

// What the runtime's casts of a shared array (SharedArrayShapes) read: the shared type ids by shape (the vector, then
// ranks one to three), then for every array or vector interface type the program has that a shared array may convert
// to, its type id, the shared shape's type id it needs and the element its header must hold, sorted by type id. A
// shared array converts to one where its element converts to that element; the rest of its conversions, those every
// array of its shape has, are in its casting list. Only a program that allocates shared arrays has the table.
internal sealed class SharedArrayTargetDataBuilder(
    ReachableProgram program,
    ITypeDefinitionResolver typeDefinitions,
    ManagedTypeLayouts types,
    WasmTargetLayout target,
    ManagedStaticDataBuildState state) : ISharedArrayTargetDataBuilder
{
    // The vector, then the rank-one array with bounds and the rectangular ranks two and three.
    internal const int ShapeCount = 4;

    public void Build()
    {
        if (program.SharedArrays is not { } shared)
        {
            return;
        }
        var shapes = new int[ShapeCount];
        foreach (var type in shared.Types)
        {
            if (TypeId(type) is { } id && Shape(type) is var shape && shape < ShapeCount)
            {
                shapes[shape] = id;
            }
        }
        var entries = new List<(int Target, int Shared, int Element)>();
        foreach (var (type, layout) in types.ConstructedObjects)
        {
            if (type.ContainsGenericParameters || SharedArrayShapes.IsShared(type) ||
                SharedTarget(type) is not ({ } element, var shape) || shape >= ShapeCount || shapes[shape] == 0 ||
                TypeId(element) is not { } elementId)
            {
                continue;
            }
            entries.Add((layout.TypeId, shapes[shape], elementId));
        }
        entries.Sort((left, right) => left.Target.CompareTo(right.Target));
        var bytes = new byte[(ShapeCount + entries.Count * 3) * sizeof(int)];
        for (var index = 0; index < ShapeCount; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int)), shapes[index]);
        }
        for (var index = 0; index < entries.Count; index++)
        {
            var at = (ShapeCount + index * 3) * sizeof(int);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), entries[index].Target);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 4), entries[index].Shared);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 8), entries[index].Element);
        }
        state.Cursor = ManagedTypeLayoutCompiler.Align(state.Cursor, Math.Max(sizeof(int), target.ObjectReferenceAlignment));
        state.SharedArrayTargetsAddress = state.Cursor;
        state.SharedArrayTargetCount = entries.Count;
        state.Segments.Add(new DataSegment(state.Cursor, [.. bytes]));
        state.Cursor += bytes.Length;
    }

    // The shape index of an array type: 0 for a vector, else its rank.
    private static int Shape(CliTypeIdentity array) => array.Shape == CliTypeShape.SzArray ? 0 : array.ArrayRank;

    // The element a shared array's header must hold to convert to the type, and the shape it needs, or no element: an
    // array of a reference element, or IEnumerable<T>, ICollection<T>, IList<T>, IReadOnlyCollection<T> or
    // IReadOnlyList<T> of a reference type, which a vector converts to.
    private (CliTypeIdentity? Element, int Shape) SharedTarget(CliTypeIdentity type)
    {
        if (type.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
        {
            return (IsReference(type.ElementType!) ? type.ElementType : null, Shape(type));
        }
        return type.Shape == CliTypeShape.GenericInstantiation && type.TypeArguments.Length == 1 &&
            IsReference(type.TypeArguments[0]) && typeDefinitions.ResolveTypeIdentity(type).FullName is
                "System.Collections.Generic.IEnumerable`1" or
                "System.Collections.Generic.ICollection`1" or
                "System.Collections.Generic.IList`1" or
                "System.Collections.Generic.IReadOnlyCollection`1" or
                "System.Collections.Generic.IReadOnlyList`1"
            ? (type.TypeArguments[0], 0)
            : (null, 0);
    }

    private static bool IsReference(CliTypeIdentity type) =>
        !type.IsValueType && !type.ContainsGenericParameters && !SharedArrayShapes.IsElement(type) &&
        type.Shape is not (CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer or
            CliTypeShape.FunctionPointer or CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter);

    private int? TypeId(CliTypeIdentity type)
    {
        if (types.ConstructedObjects.TryGetValue(type, out var constructed))
        {
            return constructed.TypeId;
        }
        return type.Shape is CliTypeShape.Named or CliTypeShape.Primitive &&
            types.Objects.TryGetValue(typeDefinitions.ResolveTypeIdentity(type).Key, out var named)
                ? named.TypeId
                : null;
    }
}
