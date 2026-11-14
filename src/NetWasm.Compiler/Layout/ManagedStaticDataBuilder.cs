using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedStaticDataBuilder : IManagedStaticDataBuilder
{
    private readonly ManagedTypeLayouts _types;
    private readonly ManagedStaticDataBuildState _state;
    private readonly IStaticFieldStorageBuilder _staticFields;
    private readonly ITypeDescriptorBuilder _typeDescriptors;
    private readonly IConstructedTypeDescriptorBuilder _constructedTypeDescriptors;
    private readonly IValueTypeDescriptorBuilder _valueTypeDescriptors;
    private readonly IRuntimeGenericArgumentMetadataBuilder _genericArguments;
    private readonly IRuntimeTypeTableBuilder _typeTable;
    private readonly IMemberDescriptorDataBuilder _memberDescriptors;
    private readonly IStringDataBuilder _strings;
    private readonly IExceptionObjectBuilder _exceptionObjects;
    private readonly ITypeObjectDataBuilder _typeObjects;
    private readonly IEnumMetadataCollector _enumMetadataCollector;
    private readonly IEnumMetadataBuilder _enumMetadata;
    private readonly IStructMarshallingDataCollector _structMarshallingCollector;
    private readonly IStructMarshallingDataBuilder _structMarshalling;
    private readonly ImmutableArray<CliTypeIdentity> _uninitializedObjectTypes;
    private readonly WasmTargetLayout _target;

    internal ManagedStaticDataBuilder(
        ManagedTypeLayouts types,
        ManagedStaticDataBuildState state,
        IStaticFieldStorageBuilder staticFields,
        ITypeDescriptorBuilder typeDescriptors,
        IConstructedTypeDescriptorBuilder constructedTypeDescriptors,
        IValueTypeDescriptorBuilder valueTypeDescriptors,
        IRuntimeGenericArgumentMetadataBuilder genericArguments,
        IRuntimeTypeTableBuilder typeTable,
        IMemberDescriptorDataBuilder memberDescriptors,
        IStringDataBuilder strings,
        IExceptionObjectBuilder exceptionObjects,
        ITypeObjectDataBuilder typeObjects,
        IEnumMetadataCollector enumMetadataCollector,
        IEnumMetadataBuilder enumMetadata,
        IStructMarshallingDataCollector? structMarshallingCollector = null,
        IStructMarshallingDataBuilder? structMarshalling = null,
        ImmutableArray<CliTypeIdentity> uninitializedObjectTypes = default)
    {
        _structMarshallingCollector = structMarshallingCollector ?? EmptyStructMarshallingDataCollector.Instance;
        _structMarshalling = structMarshalling ?? EmptyStructMarshallingDataBuilder.Instance;
        _uninitializedObjectTypes = uninitializedObjectTypes.IsDefault ? [] : uninitializedObjectTypes;
        _types = types ?? throw new ArgumentNullException(nameof(types));
        _target = types.Target ?? throw new ArgumentNullException(nameof(types));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _staticFields = staticFields ?? throw new ArgumentNullException(nameof(staticFields));
        _typeDescriptors = typeDescriptors ??
            throw new ArgumentNullException(nameof(typeDescriptors));
        _constructedTypeDescriptors = constructedTypeDescriptors ??
            throw new ArgumentNullException(nameof(constructedTypeDescriptors));
        _valueTypeDescriptors = valueTypeDescriptors ??
            throw new ArgumentNullException(nameof(valueTypeDescriptors));
        _genericArguments = genericArguments ??
            throw new ArgumentNullException(nameof(genericArguments));
        _typeTable = typeTable ?? throw new ArgumentNullException(nameof(typeTable));
        _memberDescriptors = memberDescriptors ??
            throw new ArgumentNullException(nameof(memberDescriptors));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _exceptionObjects = exceptionObjects ??
            throw new ArgumentNullException(nameof(exceptionObjects));
        _typeObjects = typeObjects ?? throw new ArgumentNullException(nameof(typeObjects));
        _enumMetadataCollector = enumMetadataCollector ??
            throw new ArgumentNullException(nameof(enumMetadataCollector));
        _enumMetadata = enumMetadata ?? throw new ArgumentNullException(nameof(enumMetadata));
    }

    public ManagedStaticData Build()
    {
        _staticFields.Build();
        _typeDescriptors.Build();
        _constructedTypeDescriptors.Build();
        _valueTypeDescriptors.Build();
        _genericArguments.Build();
        _enumMetadataCollector.Collect();
        _structMarshallingCollector.Collect();
        _strings.Build();
        _memberDescriptors.Build();
        _typeTable.Build();
        _enumMetadata.Build();
        _structMarshalling.Build();
        _exceptionObjects.Build();
        _typeObjects.Build();

        return new ManagedStaticData(
            ManagedTypeLayoutCompiler.Align(
                _state.Cursor,
                _target.ObjectReferenceAlignment),
            _state.StaticFields.ToImmutableDictionary(),
            _state.ConstructedStaticFields.ToImmutableDictionary(StringComparer.Ordinal),
            _state.Strings.ToImmutableDictionary(StringComparer.Ordinal),
            _state.ExceptionObjects.ToImmutableDictionary(),
            _state.Segments.ToImmutable(),
            _state.TypeDescriptors.ToImmutable(),
            _state.ConstructedTypeDescriptors.ToImmutable(),
            _state.ValueTypeDescriptors.ToImmutable(),
            [.. _state.StaticRoots.Order()])
        {
            MetadataTypeDescriptors = _state.MetadataTypeDescriptors.ToImmutable(),
            EnumMetadata = _state.EnumMetadata.ToImmutable(),
            StructMarshalling = _state.StructMarshalling.ToImmutable(),
            UninitializedObjectTypes = _uninitializedObjectTypes,
            MethodDescriptors = _state.MethodDescriptors.ToImmutableDictionary(
                StringComparer.Ordinal),
            FieldDescriptors = _state.FieldDescriptors.ToImmutableDictionary(
                StringComparer.Ordinal),
            PropertyDescriptors = _state.PropertyDescriptors.ToImmutableDictionary(
                StringComparer.Ordinal),
            MemberDescriptorDeclaringTypeIdOffset =
                _state.MemberDescriptorDeclaringTypeIdOffset ?? 0,
            MemberDescriptorRequiresDeclaringTypeOffset =
                _state.MemberDescriptorRequiresDeclaringTypeOffset ?? 0,
            TypeTableAddress = _state.TypeTableAddress,
            RuntimeAssemblies = _state.RuntimeAssemblies,
            TypeObjects = _state.TypeObjects.ToImmutableDictionary(StringComparer.Ordinal),
            TypeObjectTableAddress = _state.TypeObjectTableAddress,
            TypeObjectCacheAddress = _state.TypeObjectCacheAddress,
            TypeObjectCount = _state.TypeObjectCount,
            TypeObjectSize = _state.TypeObjectSize,
        };
    }
}
