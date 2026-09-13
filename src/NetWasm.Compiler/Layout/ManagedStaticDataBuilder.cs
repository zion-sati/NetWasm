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
    private readonly IStringDataBuilder _strings;
    private readonly IExceptionObjectBuilder _exceptionObjects;
    private readonly IEnumMetadataCollector _enumMetadataCollector;
    private readonly IEnumMetadataBuilder _enumMetadata;
    private readonly WasmTargetLayout _target;

    internal ManagedStaticDataBuilder(
        ManagedTypeLayouts types,
        ManagedStaticDataBuildState state,
        IStaticFieldStorageBuilder staticFields,
        ITypeDescriptorBuilder typeDescriptors,
        IConstructedTypeDescriptorBuilder constructedTypeDescriptors,
        IValueTypeDescriptorBuilder valueTypeDescriptors,
        IStringDataBuilder strings,
        IExceptionObjectBuilder exceptionObjects,
        IEnumMetadataCollector enumMetadataCollector,
        IEnumMetadataBuilder enumMetadata)
    {
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
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _exceptionObjects = exceptionObjects ??
            throw new ArgumentNullException(nameof(exceptionObjects));
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
        _enumMetadataCollector.Collect();
        _strings.Build();
        _enumMetadata.Build();
        _exceptionObjects.Build();

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
            EnumMetadata = _state.EnumMetadata.ToImmutable(),
        };
    }
}
