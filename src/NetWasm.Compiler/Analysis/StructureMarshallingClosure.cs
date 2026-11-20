// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// The types Marshal's structure members reach through the fields of the types they receive: a formatted class
// embedded in a structure or a class, and the structure each element of a ByValArray is, each converted by a
// descriptor of its own and created by PtrToStructure (the class, and the element's boxed value as the array fills).
// A nested structure's fields are walked as part of its holder, and a formatted class's base classes' fields as part
// of it.
internal interface IStructureMarshallingClosure
{
    StructureMarshallingReach Reach(IEnumerable<CliTypeIdentity> types);
}

// The types reached, which need descriptors, and of them the classes PtrToStructure creates and the structures whose
// arrays it creates.
internal sealed record StructureMarshallingReach(
    ImmutableArray<CliTypeIdentity> Types,
    ImmutableArray<CliTypeIdentity> EmbeddedClasses,
    ImmutableArray<CliTypeIdentity> ArrayElements)
{
    // The enums of ByValArray arrays, whose arrays PtrToStructure creates.
    public ImmutableArray<CliTypeIdentity> EnumArrayElements { get; init; } = [];
}

internal sealed class StructureMarshallingClosure(
    ITypeDefinitionResolver typeDefinitions,
    IFieldRepository fields,
    IBaseTypeResolver baseTypes) : IStructureMarshallingClosure
{
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IFieldRepository _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IBaseTypeResolver _baseTypes = baseTypes ?? throw new ArgumentNullException(nameof(baseTypes));

    public StructureMarshallingReach Reach(IEnumerable<CliTypeIdentity> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var walked = new HashSet<CliTypeIdentity>();
        var embedded = new HashSet<CliTypeIdentity>();
        var elements = new HashSet<CliTypeIdentity>();
        var enums = new HashSet<CliTypeIdentity>();
        var pending = new Stack<CliTypeIdentity>(types);
        while (pending.TryPop(out var type))
        {
            if (!walked.Add(type) || Definition(type) is not { } definition)
            {
                continue;
            }
            foreach (var field in Fields(type, definition))
            {
                var fieldType = field.SignatureType;
                while (fieldType.Shape == CliTypeShape.Modified)
                    fieldType = fieldType.ElementType!;
                if (fieldType.Shape == CliTypeShape.SzArray && field.MarshalAs == UnmanagedType.ByValArray &&
                    IsStructure(fieldType.ElementType!))
                {
                    elements.Add(fieldType.ElementType!);
                    pending.Push(fieldType.ElementType!);
                }
                else if (fieldType.Shape == CliTypeShape.SzArray && field.MarshalAs == UnmanagedType.ByValArray &&
                    IsEnum(fieldType.ElementType!))
                {
                    enums.Add(fieldType.ElementType!);
                }
                else if (IsFormattedClass(fieldType))
                {
                    embedded.Add(fieldType);
                    pending.Push(fieldType);
                }
                else if (IsStructure(fieldType))
                {
                    pending.Push(fieldType);
                }
            }
        }
        return new(
            [.. embedded.Concat(elements).Distinct().OrderBy(type => type.CanonicalName, StringComparer.Ordinal)],
            [.. embedded.OrderBy(type => type.CanonicalName, StringComparer.Ordinal)],
            [.. elements.OrderBy(type => type.CanonicalName, StringComparer.Ordinal)])
        {
            EnumArrayElements = [.. enums.OrderBy(type => type.CanonicalName, StringComparer.Ordinal)],
        };
    }

    // A non-generic named type's definition; others carry no structure layout.
    private TypeDefinitionModel? Definition(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.Named && !type.ContainsGenericParameters
            ? _typeDefinitions.ResolveTypeIdentity(type) is { GenericArity: 0 } definition ? definition : null
            : null;

    // A class's instance fields with its base classes' first, as a formatted class lays them out.
    private IEnumerable<FieldDefinitionModel> Fields(CliTypeIdentity type, TypeDefinitionModel definition)
    {
        if (!type.IsValueType && _baseTypes.Resolve(type) is { } baseType && Definition(baseType) is { } baseDefinition)
        {
            foreach (var field in Fields(baseType, baseDefinition))
                yield return field;
        }
        foreach (var field in definition.Fields.Select(_fields.GetField).Where(field => !field.IsStatic))
            yield return field;
    }

    private bool IsStructure(CliTypeIdentity type) =>
        type.IsValueType && Definition(type) is { IsEnum: false };

    private bool IsEnum(CliTypeIdentity type) =>
        type.IsValueType && Definition(type) is { IsEnum: true };

    private bool IsFormattedClass(CliTypeIdentity type) =>
        !type.IsValueType && Definition(type) is { IsInterface: false, LayoutKind: not CliTypeLayoutKind.Auto };
}
