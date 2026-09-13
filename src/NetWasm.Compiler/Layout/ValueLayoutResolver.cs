using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ValueLayoutResolver(
    ITypeDefinitionResolver types,
    IFieldRepository fields,
    IExplicitValueLayoutResolver explicitLayouts,
    WasmTargetLayout target,
    ManagedValueLayoutState state) : IValueLayoutResolver
{
    private readonly ITypeDefinitionResolver _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly IFieldRepository _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly IExplicitValueLayoutResolver _explicitLayouts =
        explicitLayouts ?? throw new ArgumentNullException(nameof(explicitLayouts));
    private readonly WasmTargetLayout _target = target ??
        throw new ArgumentNullException(nameof(target));
    private readonly ManagedValueLayoutState _state = state ??
        throw new ArgumentNullException(nameof(state));

    public ValueLayout Resolve(CliTypeIdentity type)
        => Resolve(type, [], suppliedDefinition: null);

    public ValueLayout Resolve(CliTypeIdentity type, TypeDefinitionModel definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Resolve(type, [], definition);
    }

    private ValueLayout Resolve(
        CliTypeIdentity type,
        ImmutableHashSet<string> activeTypes)
        => Resolve(type, activeTypes, suppliedDefinition: null);

    private ValueLayout Resolve(
        CliTypeIdentity type,
        ImmutableHashSet<string> activeTypes,
        TypeDefinitionModel? suppliedDefinition)
    {
        var hasCachedLayout = _state.Values.TryGetValue(type, out var existing);
        if (_state.CompletedMetadataLayouts.Contains(type))
        {
            if (!hasCachedLayout)
            {
                throw new InvalidOperationException(
                    $"Metadata-complete layout '{type}' has no cached scalar layout.");
            }

            return existing;
        }

        if (type.Shape is CliTypeShape.GenericTypeParameter or
            CliTypeShape.GenericMethodParameter)
        {
            throw Unsupported(type, "open generic parameter");
        }
        var seed = ResolveSeed(type);
        var layout = seed.Layout;
        if (suppliedDefinition is not null || seed.PublishDefinitionFields)
        {
            if (activeTypes.Contains(type.CanonicalName))
                throw Unsupported(type, "recursive value containment");

            var next = activeTypes.Add(type.CanonicalName);
            var definition = suppliedDefinition ?? _types.ResolveTypeIdentity(type);
            var instanceFields = definition.Fields
                .Select(_fields.GetField)
                .Where(field => !field.IsStatic)
                .OrderBy(field => field.Key.MetadataToken)
                .ToArray();
            if (definition.LayoutKind == CliTypeLayoutKind.Explicit)
            {
                layout = _explicitLayouts.Resolve(type, definition, [.. instanceFields]);
                return PublishLayout(type, layout, hasCachedLayout, existing);
            }
            if (definition.PackingSize != 0)
                throw Unsupported(type, $"non-default packing size {definition.PackingSize}");
            if (definition.InlineArrayLength != 0 && instanceFields.Length != 1)
                throw Unsupported(type, "inline array must declare exactly one instance field");

            var typeArguments = type.Shape == CliTypeShape.GenericInstantiation
                ? type.TypeArguments
                : [];
            var offset = 0;
            var alignment = layout.Alignment;
            var references = ImmutableArray.CreateBuilder<int>();
            foreach (var field in instanceFields)
            {
                var fieldType = field.SignatureType.Substitute(typeArguments);
                var fieldLayout = ResolveFieldLayout(type, layout, fieldType, next);
                offset = ManagedTypeLayoutCompiler.Align(offset, fieldLayout.Alignment);
                var resolvedField = new FieldLayout(offset)
                {
                    Size = fieldLayout.Size,
                    Type = fieldType,
                };
                if (type.Shape == CliTypeShape.GenericInstantiation)
                {
                    var instance = new FieldInstanceModel(field, type, fieldType);
                    _state.ConstructedFields.TryAdd(instance.CanonicalName, resolvedField);
                }
                else
                {
                    _state.Fields.TryAdd(field.Key, resolvedField);
                }
                var repetitions = definition.InlineArrayLength == 0
                    ? 1
                    : definition.InlineArrayLength;
                for (var element = 0; element < repetitions; element++)
                {
                    foreach (var referenceOffset in fieldLayout.ReferenceOffsets)
                        references.Add(offset + element * fieldLayout.Size + referenceOffset);
                }
                offset += checked(fieldLayout.Size * repetitions);
                alignment = Math.Max(alignment, fieldLayout.Alignment);
            }
            if (definition.DeclaredSize != 0 && definition.DeclaredSize < offset)
                throw Unsupported(
                    type,
                    $"declared size {definition.DeclaredSize} is smaller than its fields");
            var totalSize = Math.Max(layout.Size, Math.Max(offset, definition.DeclaredSize));
            var size = totalSize == 0
                ? 1
                : ManagedTypeLayoutCompiler.Align(totalSize, alignment);
            layout = new ValueLayout(type, size, alignment, references.ToImmutable());
        }

        return PublishLayout(
            type,
            layout,
            hasCachedLayout,
            existing,
            completeMetadataLayout: suppliedDefinition is not null
                || type.Shape != CliTypeShape.Primitive
                || !type.IsValueType);
    }

    private ValueLayoutSeed ResolveSeed(CliTypeIdentity type)
    {
        if (!type.IsValueType)
        {
            return new ValueLayoutSeed(
                new ValueLayout(
                    type,
                    _target.ObjectReferenceSize,
                    _target.ObjectReferenceAlignment,
                    [0]),
                PublishDefinitionFields: false);
        }

        return type.Shape switch
        {
            CliTypeShape.Primitive => new ValueLayoutSeed(
                ResolvePrimitive(type),
                PublishDefinitionFields: false),
            CliTypeShape.ManagedByReference or CliTypeShape.UnmanagedPointer => new ValueLayoutSeed(
                new ValueLayout(type, _target.AddressSize, _target.AddressSize, type.Shape == CliTypeShape.ManagedByReference ? [0] : []),
                PublishDefinitionFields: false),
            _ => new ValueLayoutSeed(
                new ValueLayout(type, Size: 0, Alignment: 1, []),
                PublishDefinitionFields: true)
        };
    }

    private ValueLayout ResolveFieldLayout(
        CliTypeIdentity owner,
        ValueLayout ownerLayout,
        CliTypeIdentity field,
        ImmutableHashSet<string> activeTypes)
        => owner.Shape == CliTypeShape.Primitive && owner.Equals(field)
            ? ownerLayout
            : Resolve(field, activeTypes);


    private ValueLayout ResolvePrimitive(CliTypeIdentity type) =>
        type.CanonicalName switch
        {
            "primitive:bool" or "primitive:i1" or "primitive:u1" =>
                new ValueLayout(type, 1, 1, []),
            "primitive:char" or "primitive:i2" or "primitive:u2" =>
                new ValueLayout(type, 2, 2, []),
            "primitive:i4" or "primitive:u4" or "primitive:f4" =>
                new ValueLayout(type, 4, 4, []),
            "primitive:i8" or "primitive:u8" or "primitive:f8" =>
                new ValueLayout(type, 8, 8, []),
            "primitive:nativeint" or "primitive:nativeuint" =>
                new ValueLayout(type, _target.AddressSize, _target.AddressSize, []),
            "primitive:string" or "primitive:object" => new ValueLayout(
                type,
                _target.ObjectReferenceSize,
                _target.ObjectReferenceAlignment,
                [0]),
            "primitive:void" => throw Unsupported(type, "void storage"),
            _ => throw Unsupported(type, "unsupported primitive"),
        };

    private static CompilerException Unsupported(
        CliTypeIdentity type,
        string reason) => new(new CompilerDiagnostic(
        DiagnosticCode.UnsupportedMetadata,
        $"unsupported layout for '{type}': {reason}"));

    private ValueLayout PublishLayout(
        CliTypeIdentity type,
        ValueLayout computedLayout,
        bool hasCachedLayout,
        ValueLayout cachedLayout,
        bool completeMetadataLayout = true)
    {
        if (!hasCachedLayout)
        {
            _state.Values.Add(type, computedLayout);
        }

        if (completeMetadataLayout)
        {
            _state.CompletedMetadataLayouts.Add(type);
        }
        return hasCachedLayout ? cachedLayout : computedLayout;
    }

    private readonly record struct ValueLayoutSeed(
        ValueLayout Layout,
        bool PublishDefinitionFields);
}
