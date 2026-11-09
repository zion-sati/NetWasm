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
    public const int Wasm32ArrayLengthOffset = 4;
    // Static data starts above the first KiB, which nothing then uses, so wasm-opt may assume
    // it is unused (--low-memory-unused) and fold constant address adds into load and store
    // offsets, as it does for Emscripten's default layout. 1024 is Binaryen's LowMemoryBound.
    internal const int StaticDataStart = 1024;

    internal ManagedLayoutSnapshot(
        ManagedTypeLayouts types,
        ManagedStaticData staticData)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(staticData);
        TypeLayouts = types;
        StaticData = staticData;
        Target = types.Target;
        StringTypeId = GetTypeId("System.String");
        TypeTypeId = GetTypeId("System.Type");
    }

    internal ManagedTypeLayouts TypeLayouts { get; }
    internal ManagedStaticData StaticData { get; }
    public WasmTargetLayout Target { get; }
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
    // Span<T> and ReadOnlySpan<T> hold a reference to their first element, then their length.
    public const int SpanReferenceOffset = 0;
    public int SpanLengthOffset => Target.AddressSize;
    public int DelegateTargetOffset => TypeLayouts.DelegateTargetOffset;
    public int DelegateMethodIdOffset => TypeLayouts.DelegateMethodIdOffset;
    public int DelegateInvocationListOffset => TypeLayouts.DelegateInvocationListOffset;
    public int DelegateInvocationCountOffset => TypeLayouts.DelegateInvocationCountOffset;
    public ImmutableArray<DataSegment> DataSegments => StaticData.Segments;
    public ImmutableArray<TypeDescriptorLayout> TypeDescriptors =>
        StaticData.TypeDescriptors;
    public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors =>
        StaticData.ConstructedTypeDescriptors;
    public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors =>
        StaticData.ValueTypeDescriptors;
    public ImmutableArray<MetadataTypeDescriptorLayout> MetadataTypeDescriptors =>
        StaticData.MetadataTypeDescriptors;
    public ImmutableArray<EnumMetadataLayout> EnumMetadata => StaticData.EnumMetadata;
    public ImmutableArray<int> StaticRootAddresses => StaticData.StaticRootAddresses;
    public IReadOnlyDictionary<EntityKey, ObjectLayout> ObjectLayouts =>
        TypeLayouts.Objects;
    public IReadOnlyDictionary<CliTypeIdentity, ValueLayout> ValueLayouts =>
        TypeLayouts.ValueLayoutState.Values;

    private int GetTypeId(string fullName) => TypeLayouts.ObjectsByName.TryGetValue(
        fullName,
        out var layout)
        ? layout.TypeId
        : 0;
}
