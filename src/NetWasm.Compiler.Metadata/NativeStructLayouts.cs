// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

// Where a struct's definition and instance fields come from: the assembly being read, or the whole compilation.
public interface INativeStructSource
{
    // The value type's definition and its instance fields in declaration order, or null when they cannot be read here.
    (TypeDefinitionModel Definition, ImmutableArray<FieldDefinitionModel> Fields)? Describe(CliTypeIdentity type);
}

// A struct as desktop .NET's runtime marshalling lays it out natively on Unix, for a target's pointer size: each field
// in declaration order (or at its FieldOffset) at its native size and C alignment, capped by the struct's Pack; a bool
// as a 4-byte BOOL or, with [MarshalAs(U1 or I1)], a byte; a char as a UTF-16 code unit when the struct's CharSet is
// Unicode or the field says U2 or I2, and otherwise as one ANSI byte; a nested struct by its own native layout.
// Converts: some field converts, so a stub passes the struct through its native copy and copies it field by field.
internal sealed record NativeStructLayout(int Size, int Alignment, ImmutableArray<NativeStructField> Fields, bool Converts)
{
    // The scalar the Wasm C ABI passes the struct as, when one scalar at offset 0 fills it; null passes it indirectly.
    public CliTypeIdentity? Scalar { get; init; }

    // A struct that converts only through UTF-16 chars, whose native copy holds the managed value's bytes. Desktop .NET
    // copies such a struct whole where another holds it, so a fixed buffer of UTF-16 chars copies every element, while
    // a stub still passes it through its native copy, since the C ABI takes no char.
    public bool CopiesWhole { get; init; }
}

// One field of a native struct layout: copied as its managed type (Conversion and Nested null, or Nested CopiesWhole),
// converted as a bool or char (Conversion), or copied field by field as a nested struct that converts (Nested).
internal sealed record NativeStructField(
    FieldDefinitionModel Field, CliTypeIdentity Type, int Offset, int Size, NativeScalarForm? Conversion,
    NativeStructLayout? Nested)
{
    // A reference field Marshal's structure members convert (PlanStructure only): a string, a delegate or an inline array.
    public NativeReference? Reference { get; init; }
}

// How Marshal's structure members convert a reference field: a string as a pointer to a native copy (UTF-8, as ANSI
// and LPUTF8Str are on Unix, or UTF-16) or inline in Count characters (ByValTStr); a delegate as a function pointer;
// an array inline in Count elements of Element (ByValArray), copied, as native booleans of ElementSize bytes, or as
// ANSI characters, as structures laid out by their own layout (Element the structure), or as an enum's underlying
// values (Element the enum); a formatted class inline,
// laid out as its own structure (Element the class); a SafeHandle or a CriticalHandle as its handle, which desktop
// .NET writes only for a CriticalHandle and never reads back.
internal sealed record NativeReference(NativeReferenceKind Kind, int Count = 0, CliTypeIdentity? Element = null,
    int ElementSize = 0);

internal enum NativeReferenceKind
{
    Utf8Pointer,
    Utf16Pointer,
    Utf8Inline,
    Utf16Inline,
    Delegate,
    ArrayCopy,
    ArrayBoolean,
    ArrayAnsi,
    SafeHandle,
    CriticalHandle,
    EmbeddedClass,
    ArrayStructure,
    ArrayEnum,
}

// A structure Marshal's structure members can convert (Layout), or why it cannot: Rejected when desktop .NET's
// marshalling rejects one of its fields (an object, an array without ByValArray, a StringBuilder, a class without
// layout); InvalidBase when a formatted class derives from a class without layout, which desktop .NET cannot load;
// otherwise a field NetWasm does not convert yet.
internal sealed record NativeStructurePlan(NativeStructLayout? Layout, bool Rejected)
{
    public bool InvalidBase { get; init; }
}

internal static class NativeStructLayouts
{
    private const int StringFormatMask = 0x30000;
    private const int UnicodeClass = 0x10000;

    // The struct's native layout, or null when runtime marshalling cannot pass it through a stub: a field of a
    // reference type, a [MarshalAs] beyond a native type, auto layout, an inline array, or a type not readable here.
    public static NativeStructLayout? Plan(CliTypeIdentity type, INativeStructSource source, int pointerSize)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(source);
        return Plan(type, new Context(source, pointerSize, null));
    }

    // The structure as Marshal's structure members lay it out, as desktop .NET's runtime marshalling does on Unix: as a
    // stub's struct, and also a formatted class (a sequential or explicit class, its base classes' fields first), string
    // fields (LPStr, LPUTF8Str, LPWStr, the CharSet's default, ByValTStr), delegate fields and ByValArray arrays.
    public static NativeStructurePlan PlanStructure(CliTypeIdentity type, INativeStructSource source, int pointerSize,
        IMetadataIdentityBaseTypeResolver baseTypes)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(baseTypes);
        var context = new Context(source, pointerSize, baseTypes);
        var layout = Plan(type, context);
        return new(layout, layout is null && context.Rejected) { InvalidBase = layout is null && context.InvalidBase };
    }

    // What one planning pass reads and records: Structure (BaseTypes set) when Marshal's structure members plan it.
    private sealed class Context(INativeStructSource source, int pointerSize, IMetadataIdentityBaseTypeResolver? baseTypes)
    {
        public INativeStructSource Source { get; } = source;
        public int PointerSize { get; } = pointerSize;
        public IMetadataIdentityBaseTypeResolver? BaseTypes { get; } = baseTypes;
        public bool Structure => BaseTypes is not null;
        public HashSet<string> Active { get; } = [];
        public bool Rejected { get; set; }
        public bool InvalidBase { get; set; }
    }

    private static NativeStructLayout? Plan(CliTypeIdentity type, Context context)
    {
        if (!type.IsValueType && !context.Structure || type.Shape != CliTypeShape.Named ||
            context.Source.Describe(type) is not { } described ||
            described.Definition is not { IsEnum: false, InlineArrayLength: 0 } definition ||
            definition.GenericArity != 0)
        {
            return null;
        }
        if (definition.LayoutKind == CliTypeLayoutKind.Auto)
        {
            // A class without layout is no structure; desktop .NET rejects it as a field or a type to marshal.
            context.Rejected |= !type.IsValueType;
            return null;
        }
        if (!context.Active.Add(type.CanonicalName))
            return null;
        var layout = PlanFields(type, definition, described.Fields, context);
        context.Active.Remove(type.CanonicalName);
        return layout;
    }

    private static NativeStructLayout? PlanFields(CliTypeIdentity type, TypeDefinitionModel definition,
        ImmutableArray<FieldDefinitionModel> fields, Context context)
    {
        var unicode = (definition.Attributes & StringFormatMask) == UnicodeClass;
        var pack = definition.PackingSize;
        var offset = 0;
        var end = 0;
        var alignment = 1;
        var converts = !type.IsValueType;
        var transforms = false;
        var planned = ImmutableArray.CreateBuilder<NativeStructField>(fields.Length);
        // A formatted class lays out its base classes' fields first, as desktop .NET does; a base without layout other
        // than object makes it no structure.
        if (!type.IsValueType && context.BaseTypes!.GetBaseType(type) is { } baseType &&
            baseType.CanonicalName != "primitive:object")
        {
            if (Plan(baseType, context) is not { } inherited)
            {
                // A sequential or explicit class on a base without layout is a type desktop .NET cannot load.
                context.InvalidBase |= context.Source.Describe(baseType)?.Definition.LayoutKind == CliTypeLayoutKind.Auto;
                return null;
            }
            planned.AddRange(inherited.Fields);
            offset = end = inherited.Size;
            alignment = inherited.Alignment;
        }
        var start = offset;
        foreach (var field in fields)
        {
            var inline = context.Structure && field.MarshalAsSizeConst is not null;
            if (field.HasComplexMarshalAs && !inline || Field(field, unicode, context) is not { } native)
                return null;
            var fieldAlignment = pack == 0 ? native.Alignment : Math.Min(pack, native.Alignment);
            var at = definition.LayoutKind == CliTypeLayoutKind.Explicit
                ? field.ExplicitOffset is { } explicitOffset ? start + explicitOffset : -1
                : Align(offset, fieldAlignment);
            if (at < 0)
                return null;
            planned.Add(native.Field with { Offset = at });
            offset = at + native.Field.Size;
            end = Math.Max(end, offset);
            alignment = Math.Max(alignment, fieldAlignment);
            converts |= native.Field.Conversion is not null || native.Field.Nested is not null ||
                native.Field.Reference is not null;
            // A UTF-16 char keeps its bytes, so only a boolean, an ANSI char, a nested struct that changes or a
            // reference makes the native copy's bytes differ.
            transforms |= native.Field.Conversion is { Conversion: not NativeScalarConversion.None } ||
                native.Field.Nested is { CopiesWhole: false } || native.Field.Reference is not null;
        }
        var size = Align(Math.Max(end, definition.DeclaredSize), alignment);
        var layout = new NativeStructLayout(size, alignment, planned.ToImmutable(), converts)
        {
            CopiesWhole = type.IsValueType && converts && !transforms,
        };
        return layout with { Scalar = type.IsValueType ? SingleScalar(layout) : null };
    }

    // A field's native size and alignment, with how a stub copies it.
    private static (NativeStructField Field, int Alignment)? Field(FieldDefinitionModel field, bool unicode, Context context)
    {
        var source = context.Source;
        var pointerSize = context.PointerSize;
        var type = field.SignatureType;
        while (type.Shape == CliTypeShape.Modified)
            type = type.ElementType!;
        if (NativeScalarMarshalling.Converts(type))
        {
            var attributes = unicode ? MethodImportAttributes.CharSetUnicode : MethodImportAttributes.CharSetAnsi;
            if (NativeScalarMarshalling.Form(type, field.MarshalAs, attributes) is not { } form)
                return null;
            var size = Size(form.Native, pointerSize);
            return (new(field, type, 0, size, form, null), size);
        }
        if (field.MarshalAs is not null && type.Shape == CliTypeShape.Primitive &&
            NativeScalarMarshalling.Mismatch(type, field.MarshalAs) is not null)
        {
            return null;
        }
        if (type.Shape is CliTypeShape.UnmanagedPointer or CliTypeShape.FunctionPointer)
            return (new(field, type, 0, pointerSize, null, null), pointerSize);
        if (!type.IsValueType && context.Structure)
            return ReferenceField(field, type, unicode, context);
        if (type.Shape == CliTypeShape.Primitive)
            return Size(type, pointerSize) is var size and > 0 ? (new(field, type, 0, size, null, null), size) : null;
        if (!type.IsValueType || type.Shape != CliTypeShape.Named || source.Describe(type) is not { } described)
            return null;
        var definition = described.Definition;
        if (definition.IsEnum)
        {
            var underlying = Size(definition.EnumUnderlyingType, pointerSize);
            return underlying > 0 ? (new(field, type, 0, underlying, null, null), underlying) : null;
        }
        if (Plan(type, context) is not { } nested)
            return null;
        // A nested struct that converts nothing is blittable: its managed value is its native copy.
        return (new(field, type, 0, nested.Size, null, nested.Converts ? nested : null), nested.Alignment);
    }

    // A reference field of a structure, as desktop .NET's runtime marshalling converts it on Unix; null, and Rejected
    // where desktop rejects the field, when Marshal's structure members do not convert it.
    private static (NativeStructField Field, int Alignment)? ReferenceField(FieldDefinitionModel field,
        CliTypeIdentity type, bool unicode, Context context)
    {
        var pointerSize = context.PointerSize;
        (NativeStructField, int) Pointer(NativeReferenceKind kind) =>
            (new(field, type, 0, pointerSize, null, null) { Reference = new(kind) }, pointerSize);
        if (type.CanonicalName == "primitive:string")
        {
            switch (field.MarshalAs)
            {
                case null:
                    return Pointer(unicode ? NativeReferenceKind.Utf16Pointer : NativeReferenceKind.Utf8Pointer);
                case UnmanagedType.LPStr or UnmanagedType.LPUTF8Str:
                    return Pointer(NativeReferenceKind.Utf8Pointer);
                case UnmanagedType.LPWStr or UnmanagedType.LPTStr:
                    return Pointer(NativeReferenceKind.Utf16Pointer);
                case UnmanagedType.ByValTStr when field.MarshalAsSizeConst is > 0 and var count:
                    var width = unicode ? 2 : 1;
                    return (new(field, type, 0, count * width, null, null)
                    {
                        Reference = new(unicode ? NativeReferenceKind.Utf16Inline : NativeReferenceKind.Utf8Inline, count),
                    }, width);
                default:
                    return null;
            }
        }
        if (type.Shape == CliTypeShape.SzArray)
            return ArrayField(field, type, unicode, context);
        if (type.CanonicalName == "primitive:object")
        {
            context.Rejected = true;
            return null;
        }
        if (type.Shape != CliTypeShape.Named)
            return null;
        if (type.FullName == "System.Text.StringBuilder")
        {
            context.Rejected = true;
            return null;
        }
        for (var ancestor = context.BaseTypes!.GetBaseType(type); ancestor is not null;
             ancestor = context.BaseTypes.GetBaseType(ancestor))
        {
            if (ancestor.FullName == "System.MulticastDelegate")
                return Pointer(NativeReferenceKind.Delegate);
        }
        for (CliTypeIdentity? handle = type; handle is not null; handle = context.BaseTypes.GetBaseType(handle))
        {
            switch (handle.FullName)
            {
                case "System.Runtime.InteropServices.SafeHandle":
                    return Pointer(NativeReferenceKind.SafeHandle);
                case "System.Runtime.InteropServices.CriticalHandle":
                    return Pointer(NativeReferenceKind.CriticalHandle);
            }
        }
        // A class without layout, or an interface, is a field desktop .NET rejects; a formatted class embeds inline, at
        // its own layout's size and alignment.
        if (context.Source.Describe(type) is not { Definition: var described })
            return null;
        if (described.IsInterface || described.LayoutKind == CliTypeLayoutKind.Auto)
        {
            context.Rejected = true;
            return null;
        }
        return Plan(type, context) is { } embedded
            ? (new(field, type, 0, embedded.Size, null, null)
            {
                Reference = new(NativeReferenceKind.EmbeddedClass, Element: type),
            }, embedded.Alignment)
            : null;
    }

    // A ByValArray of blittable primitives, booleans (4-byte BOOL by default, one byte with U1 or I1) or characters (one
    // ANSI byte each unless the structure is Unicode or the subtype says U2 or I2); an array without ByValArray is one
    // desktop .NET rejects.
    private static (NativeStructField Field, int Alignment)? ArrayField(FieldDefinitionModel field, CliTypeIdentity type,
        bool unicode, Context context)
    {
        if (field.MarshalAs != UnmanagedType.ByValArray || field.MarshalAsSizeConst is not { } count)
        {
            context.Rejected = field.MarshalAs is null;
            return null;
        }
        var element = type.ElementType!;
        var subtype = field.MarshalAsArraySubType;
        (NativeStructField, int) Inline(NativeReferenceKind kind, int elementSize) =>
            (new(field, type, 0, count * elementSize, null, null)
            {
                Reference = new(kind, count, element, elementSize),
            }, elementSize);
        switch (element.CanonicalName)
        {
            case "primitive:bool":
                return subtype switch
                {
                    null or UnmanagedType.Bool => Inline(NativeReferenceKind.ArrayBoolean, 4),
                    UnmanagedType.U1 or UnmanagedType.I1 => Inline(NativeReferenceKind.ArrayBoolean, 1),
                    _ => null,
                };
            case "primitive:char":
                return subtype is UnmanagedType.U2 or UnmanagedType.I2 || subtype is null && unicode
                    ? Inline(NativeReferenceKind.ArrayCopy, 2)
                    : subtype is null or UnmanagedType.U1 or UnmanagedType.I1
                        ? Inline(NativeReferenceKind.ArrayAnsi, 1)
                        : null;
        }
        // An enum's elements are its underlying values, read back into an array of the enum.
        if (element.IsValueType && element.Shape == CliTypeShape.Named && subtype is null &&
            context.Source.Describe(element) is { Definition: { IsEnum: true } enumeration } &&
            Size(enumeration.EnumUnderlyingType, context.PointerSize) is > 0 and var underlying)
        {
            return Inline(NativeReferenceKind.ArrayEnum, underlying);
        }
        // A structure's elements are laid out one after another at its own layout's size and alignment.
        if (element.IsValueType && element.Shape == CliTypeShape.Named && subtype is null &&
            context.Source.Describe(element) is { Definition.IsEnum: false } &&
            Plan(element, context) is { } structure)
        {
            return (new(field, type, 0, count * structure.Size, null, null)
            {
                Reference = new(NativeReferenceKind.ArrayStructure, count, element, structure.Size),
            }, structure.Alignment);
        }
        if (element.Shape != CliTypeShape.Primitive || subtype is not null ||
            Size(element, context.PointerSize) is not (> 0 and var size))
        {
            return null;
        }
        return Inline(NativeReferenceKind.ArrayCopy, size);
    }

    // A primitive's native size, which is also its C alignment; 0 for a type that is not one.
    private static int Size(CliTypeIdentity type, int pointerSize) => type.CanonicalName switch
    {
        "primitive:i1" or "primitive:u1" or "primitive:bool" => 1,
        "primitive:i2" or "primitive:u2" or "primitive:char" => 2,
        "primitive:i4" or "primitive:u4" or "primitive:f4" => 4,
        "primitive:i8" or "primitive:u8" or "primitive:f8" => 8,
        "primitive:nativeint" or "primitive:nativeuint" => pointerSize,
        _ => 0,
    };

    // The one native scalar at offset 0 that fills the struct, as the Wasm C ABI's single-element struct rule finds it.
    private static CliTypeIdentity? SingleScalar(NativeStructLayout layout)
    {
        if (layout.Fields.Length != 1 || layout.Fields[0] is not { Offset: 0 } field || field.Size != layout.Size)
            return null;
        if (field.Conversion is { } form)
            return form.Native;
        if (field.Nested is { } nested)
            return nested.Scalar;
        return field.Type.Shape is CliTypeShape.Primitive or CliTypeShape.UnmanagedPointer or CliTypeShape.FunctionPointer
            ? field.Type
            : null;
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
