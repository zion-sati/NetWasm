using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

public sealed class ManagedLayoutSnapshot :
    ITargetLayout,
    IRuntimeObjectLayout,
    ITypeDescriptorSource,
    IEnumMetadataSource
{
    public const int Wasm32ObjectHeaderSize = 4;
    public const int Wasm32StringLengthOffset = 4;
    public const int Wasm32StringDataOffset = 8;
    public const int Wasm32ArrayLengthOffset = 4;
    public const int Wasm32ArrayDataPointerOffset = 8;
    internal const int StaticDataStart = 16;

    internal ManagedLayoutSnapshot(
        ManagedTypeLayouts types,
        ManagedStaticData staticData)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(staticData);
        TypeLayouts = types;
        StaticData = staticData;
        Target = types.Target;
        ReferenceArrayTypeId = GetTypeId("System.Array");
        StringTypeId = GetTypeId("System.String");
        TypeTypeId = GetTypeId("System.Type");
    }

    internal ManagedTypeLayouts TypeLayouts { get; }
    internal ManagedStaticData StaticData { get; }
    public WasmTargetLayout Target { get; }
    public int ReferenceArrayTypeId { get; }
    public int StringTypeId { get; }
    public int TypeTypeId { get; }
    public int StaticDataEnd => StaticData.EndAddress;
    public int StringLengthOffset => Target.ObjectHeaderSize;
    public int StringDataOffset => WasmTargetLayout.Align(
        StringLengthOffset + WasmTargetLayout.SemanticLengthSize,
        sizeof(int));
    public int ArrayLengthOffset => Target.ObjectHeaderSize;
    public int ArrayDataPointerOffset => WasmTargetLayout.Align(
        ArrayLengthOffset + WasmTargetLayout.SemanticLengthSize,
        Target.AddressSize);
    public int ArrayElementTypeIdOffset => ArrayDataPointerOffset + Target.AddressSize;
    public int DelegateTargetOffset => TypeLayouts.DelegateTargetOffset;
    public int DelegateMethodIdOffset => TypeLayouts.DelegateMethodIdOffset;
    public int DelegateLeftOffset => TypeLayouts.DelegateLeftOffset;
    public int DelegateRightOffset => TypeLayouts.DelegateRightOffset;
    public ImmutableArray<DataSegment> DataSegments => StaticData.Segments;
    public ImmutableArray<TypeDescriptorLayout> TypeDescriptors =>
        StaticData.TypeDescriptors;
    public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors =>
        StaticData.ConstructedTypeDescriptors;
    public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors =>
        StaticData.ValueTypeDescriptors;
    public ImmutableArray<EnumMetadataLayout> EnumMetadata => StaticData.EnumMetadata;
    public ImmutableArray<int> StaticRootAddresses => StaticData.StaticRootAddresses;
    public IReadOnlyDictionary<EntityKey, ObjectLayout> ObjectLayouts =>
        TypeLayouts.Objects;
    public IReadOnlyDictionary<CliTypeIdentity, ObjectLayout> ObjectIdentityLayouts =>
        TypeLayouts.ObjectIdentities;
    public IReadOnlyDictionary<CliTypeIdentity, ObjectLayout> ConstructedObjectLayouts =>
        TypeLayouts.ConstructedObjects;
    public IReadOnlyDictionary<CliTypeIdentity, ValueLayout> ValueLayouts =>
        TypeLayouts.ValueLayoutState.Values;
    public IReadOnlyDictionary<EntityKey, FieldLayout> InstanceFieldLayouts =>
        TypeLayouts.ValueLayoutState.Fields;
    public IReadOnlyDictionary<string, FieldLayout> ConstructedInstanceFieldLayouts =>
        TypeLayouts.ValueLayoutState.ConstructedFields;
    public IReadOnlyDictionary<EntityKey, StaticFieldLayout> StaticFieldLayouts =>
        StaticData.StaticFields;
    public IReadOnlyDictionary<string, StaticFieldLayout> ConstructedStaticFieldLayouts =>
        StaticData.ConstructedStaticFields;
    public IReadOnlyDictionary<string, StringLayout> StringLayouts => StaticData.Strings;

    private int GetTypeId(string fullName) => TypeLayouts.ObjectsByName.TryGetValue(
        fullName,
        out var layout)
        ? layout.TypeId
        : 0;
}
