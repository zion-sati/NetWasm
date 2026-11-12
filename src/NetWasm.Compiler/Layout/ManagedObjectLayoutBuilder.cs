using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedObjectLayoutBuilder : IManagedObjectLayoutBuilder
{
    private readonly ITypeRepository _typeRepository;
    private readonly IFieldRepository _fieldsRepository;
    private readonly ITypeFinder _types;
    private readonly ITypeDefinitionResolver _typeDefinitions;
    private readonly ITypeIdentityResolver _identities;
    private readonly IMetadataEntityBaseTypeResolver _entityBaseTypes;
    private readonly IMetadataIdentityBaseTypeResolver _identityBaseTypes;
    private readonly WasmTargetLayout _target;
    private readonly ManagedTypeLayoutBuildState _state;
    private readonly Dictionary<EntityKey, FieldLayout> _fields;
    private readonly Dictionary<string, FieldLayout> _constructedFields;
    private readonly IValueLayoutResolver _valueLayouts;
    private readonly IExplicitValueLayoutResolver _explicitLayouts;

    public ManagedObjectLayoutBuilder(
        ITypeRepository typeRepository,
        IFieldRepository fields,
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IMetadataEntityBaseTypeResolver entityBaseTypes,
        IMetadataIdentityBaseTypeResolver identityBaseTypes,
        WasmTargetLayout target,
        ManagedTypeLayoutBuildState state,
        IValueLayoutResolver valueLayouts,
        IExplicitValueLayoutResolver explicitLayouts)
    {
        _typeRepository = typeRepository ?? throw new ArgumentNullException(nameof(typeRepository));
        _fieldsRepository = fields ?? throw new ArgumentNullException(nameof(fields));
        _types = types ?? throw new ArgumentNullException(nameof(types));
        _typeDefinitions = typeDefinitions ?? throw new ArgumentNullException(nameof(typeDefinitions));
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _entityBaseTypes = entityBaseTypes ??
            throw new ArgumentNullException(nameof(entityBaseTypes));
        _identityBaseTypes = identityBaseTypes ??
            throw new ArgumentNullException(nameof(identityBaseTypes));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _fields = state.ValueLayouts.Fields;
        _constructedFields = state.ValueLayouts.ConstructedFields;
        _valueLayouts = valueLayouts ?? throw new ArgumentNullException(nameof(valueLayouts));
        _explicitLayouts = explicitLayouts ?? throw new ArgumentNullException(nameof(explicitLayouts));
    }

    public ObjectLayout Build(EntityKey typeKey)
    {
        if (_state.Objects.TryGetValue(typeKey, out var existing))
            return existing;

        var type = _typeRepository.GetTypeDefinition(typeKey);
        if (type.GenericArity > 0)
        {
            var identity = _identities.GetTypeIdentity(typeKey);
            var metadata = new ObjectLayout(
                NewTypeId(null),
                AlignObject(_target.ObjectHeaderSize),
                []);
            _state.Objects.Add(typeKey, metadata);
            _state.ObjectIdentities.Add(identity, metadata);
            return metadata;
        }
        if (type.IsValueType)
        {
            ObjectLayout? valueBaseLayout = _entityBaseTypes.GetBaseType(typeKey) is EntityKey valueBase
                ? Build(valueBase)
                : null;

            var identity = _identities.GetTypeIdentity(typeKey);
            var boxed = CreateValueTypeObjectLayout(identity, valueBaseLayout);
            _state.Objects.Add(typeKey, boxed);
            _state.ObjectIdentities.Add(identity, boxed);
            PublishWellKnownLayout(typeKey, type, boxed);
            return boxed;
        }

        var baseType = _identityBaseTypes.GetBaseType(_identities.GetTypeIdentity(typeKey));
        ObjectLayout? baseLayout = baseType is null ? null : BuildBaseObjectLayout(baseType);
        var offset = baseLayout?.Size ?? _target.ObjectHeaderSize;
        var referenceOffsets = baseLayout?.ReferenceOffsets.ToBuilder() ??
            ImmutableArray.CreateBuilder<int>();
        if (type.LayoutKind == CliTypeLayoutKind.Explicit)
        {
            offset = PlaceExplicitFields(_identities.GetTypeIdentity(typeKey), type, baseType, offset, referenceOffsets);
        }
        else foreach (var field in type.Fields
                     .Select(_fieldsRepository.GetField)
                     .Where(field => !field.IsStatic)
                     .OrderBy(field => field.Key.MetadataToken))
        {
            var storage = _valueLayouts.Resolve(field.SignatureType);
            offset = Align(offset, storage.Alignment);
            _fields.Add(field.Key, new FieldLayout(offset)
            {
                Size = storage.Size,
                Type = field.SignatureType,
            });
            foreach (var referenceOffset in storage.ReferenceOffsets)
                referenceOffsets.Add(offset + referenceOffset);
            offset += storage.Size;
        }
        if (type.FullName == "System.Array")
        {
            offset += sizeof(int);
            offset = Align(offset, _target.ObjectReferenceAlignment);
            referenceOffsets.Add(offset);
            offset += _target.ObjectReferenceSize;
            offset += sizeof(int);
        }
        else if (type.FullName == "System.String")
        {
            offset += sizeof(int);
        }

        var layout = new ObjectLayout(
            NewTypeId(baseLayout),
            AlignObject(offset),
            referenceOffsets.ToImmutable());
        _state.Objects.Add(typeKey, layout);
        _state.ObjectIdentities.Add(_identities.GetTypeIdentity(typeKey), layout);
        PublishWellKnownLayout(typeKey, type, layout);
        return layout;
    }

    public ObjectLayout Build(CliTypeIdentity type)
    {
        if (_state.ConstructedObjects.TryGetValue(type, out var existing))
            return existing;
        if (type.Shape is CliTypeShape.ManagedByReference or
            CliTypeShape.UnmanagedPointer)
        {
            var semanticIdentity = new ObjectLayout(
                NewTypeId(null),
                AlignObject(_target.ObjectHeaderSize),
                []);
            _state.ConstructedObjects.Add(type, semanticIdentity);
            return semanticIdentity;
        }
        if (type.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
        {
            var array = Build(_types.FindType("System.Array").Key);
            if (type.Shape == CliTypeShape.Array)
            {
                var rectangular = RectangularArrayLayout.Create(
                    _target,
                    GetArrayElementTypeIdOffset());
                var rectangularReferences = array.ReferenceOffsets.ToBuilder();
                rectangularReferences.Add(rectangular.ShapePointerOffset);
                var rectangularIdentity = new ObjectLayout(
                    NewTypeId(array),
                    rectangular.ObjectSize,
                    rectangularReferences.ToImmutable());
                _state.ConstructedObjects.Add(type, rectangularIdentity);
                return rectangularIdentity;
            }
            var arrayIdentity = new ObjectLayout(
                NewTypeId(array), array.Size, array.ReferenceOffsets);
            _state.ConstructedObjects.Add(type, arrayIdentity);
            return arrayIdentity;
        }

        var definition = _typeDefinitions.ResolveTypeIdentity(type);
        if (type.IsValueType)
        {
            // A value type instantiation derives from System.ValueType.
            var boxed = CreateValueTypeObjectLayout(type, BuildBaseObjectLayout(_identityBaseTypes.GetBaseType(type)!));
            _state.ConstructedObjects.Add(type, boxed);
            return boxed;
        }

        // Only generic instantiations reach here, and .NET loads no generic type
        // with explicit layout.
        if (definition.LayoutKind == CliTypeLayoutKind.Explicit)
        {
            throw UnsupportedLayout(type, "generic explicit layout");
        }
        var baseType = _identityBaseTypes.GetBaseType(type);
        ObjectLayout? baseLayout = baseType is null ? null : BuildBaseObjectLayout(baseType);
        var offset = baseLayout?.Size ?? _target.ObjectHeaderSize;
        var references = baseLayout?.ReferenceOffsets.ToBuilder() ??
            ImmutableArray.CreateBuilder<int>();
        foreach (var field in definition.Fields
                     .Select(_fieldsRepository.GetField)
                     .Where(field => !field.IsStatic)
                     .OrderBy(field => field.Key.MetadataToken))
        {
            var fieldType = field.SignatureType.Substitute(type.TypeArguments);
            var storage = _valueLayouts.Resolve(fieldType);
            offset = Align(offset, storage.Alignment);
            var instance = new FieldInstanceModel(field, type, fieldType);
            _constructedFields.Add(instance.CanonicalName, new FieldLayout(offset)
            {
                Size = storage.Size,
                Type = fieldType,
            });
            foreach (var referenceOffset in storage.ReferenceOffsets)
                references.Add(offset + referenceOffset);
            offset += storage.Size;
        }

        var layout = new ObjectLayout(
            NewTypeId(baseLayout),
            AlignObject(offset),
            references.ToImmutable());
        _state.ConstructedObjects.Add(type, layout);
        return layout;
    }

    private ObjectLayout CreateValueTypeObjectLayout(CliTypeIdentity type, ObjectLayout? baseLayout)
    {
        if (!type.HasRuntimeStorage)
        {
            return new ObjectLayout(
                NewTypeId(baseLayout),
                AlignObject(_target.ObjectHeaderSize),
                []);
        }

        var value = _valueLayouts.Resolve(type);
        var payloadOffset = Align(_target.ObjectHeaderSize, value.Alignment);
        return new ObjectLayout(
            NewTypeId(baseLayout),
            AlignObject(payloadOffset + value.Size),
            [.. value.ReferenceOffsets.Select(offset => payloadOffset + offset)]);
    }

    private int NewTypeId(ObjectLayout? baseLayout)
    {
        var typeId = _state.NextTypeId++;
        _state.BaseTypeIds.Add(typeId, baseLayout?.TypeId ?? 0);
        return typeId;
    }

    private ObjectLayout BuildBaseObjectLayout(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.GenericInstantiation
            ? Build(type)
            : Build(_typeDefinitions.ResolveTypeIdentity(type).Key);

    private void PublishWellKnownLayout(
        EntityKey typeKey,
        TypeDefinitionModel type,
        ObjectLayout layout)
    {
        if (type.FullName is not ("System.Array" or "System.String" or RuntimeTypeObjects.ClassName))
        {
            return;
        }
        _state.ObjectsByName.Add(type.FullName, layout);
    }

    // An explicit-layout class's offsets count from the end of its base's
    // fields, as CoreCLR's do, and the overlaps they allow are a struct's: no
    // reference may share bytes with anything but the same reference. As in
    // CoreCLR, its base must lay out its own fields, unless it is object.
    private int PlaceExplicitFields(
        CliTypeIdentity type,
        TypeDefinitionModel definition,
        CliTypeIdentity? baseType,
        int baseEnd,
        ImmutableArray<int>.Builder references)
    {
        if (baseType is not null && _typeDefinitions.ResolveTypeIdentity(baseType) is
            { LayoutKind: CliTypeLayoutKind.Auto, FullName: not "System.Object" })
        {
            throw UnsupportedLayout(type, $"explicit layout over the automatic layout of '{baseType}'");
        }
        var instanceFields = definition.Fields
            .Select(_fieldsRepository.GetField)
            .Where(field => !field.IsStatic)
            .OrderBy(field => field.Key.MetadataToken)
            .ToArray();
        var plan = _explicitLayouts.Resolve(type, definition,
            [.. instanceFields.Select(field => new ExplicitFieldStorage(field, _valueLayouts.Resolve(field.SignatureType)))],
            _target);
        var start = AlignObject(baseEnd);
        for (var index = 0; index < instanceFields.Length; index++)
        {
            _fields.Add(instanceFields[index].Key, plan.Fields[index] with { Offset = start + plan.Fields[index].Offset });
        }
        references.AddRange(plan.Value.ReferenceOffsets.Select(reference => start + reference));
        return start + plan.Value.Size;
    }

    private static CompilerException UnsupportedLayout(CliTypeIdentity type, string reason) =>
        new(new CompilerDiagnostic(DiagnosticCode.UnsupportedMetadata, $"unsupported layout for '{type}': {reason}"));

    private int AlignObject(int value) => Align(value, _target.ObjectReferenceAlignment);

    private int GetArrayElementTypeIdOffset()
    {
        var lengthOffset = _target.ObjectHeaderSize;
        var dataPointerOffset = WasmTargetLayout.Align(
            lengthOffset + WasmTargetLayout.SemanticLengthSize,
            _target.AddressSize);
        return dataPointerOffset + _target.AddressSize;
    }

    internal static int Align(int value, int alignment)
    {
        checked
        {
            return (value + alignment - 1) & -alignment;
        }
    }
}
