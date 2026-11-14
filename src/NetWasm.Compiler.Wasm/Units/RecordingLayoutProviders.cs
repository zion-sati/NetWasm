using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Units;

internal sealed class RecordingValueLayoutProvider(
    IValueLayoutProvider inner,
    IUnitDependencyRecorder recorder) : IValueLayoutProvider
{
    public ValueLayout GetValueLayout(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var layout = inner.GetValueLayout(type);
        var answer = new UnitFingerprintBuilder();
        answer.Add(layout);
        recorder.Record(UnitQueryKind.ValueLayout, type.CanonicalName, answer.Finish(), layout);
        return layout;
    }
}

internal sealed class RecordingInstanceFieldLayoutProvider(
    IInstanceFieldLayoutProvider inner,
    IUnitDependencyRecorder recorder) : IInstanceFieldLayoutProvider
{
    public FieldLayout GetFieldLayout(FieldInstanceModel field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var layout = inner.GetFieldLayout(field);
        recorder.Record(UnitQueryKind.InstanceField, field.CanonicalName, Of(layout), layout);
        return layout;
    }

    public FieldLayout GetFieldLayout(EntityKey field)
    {
        var layout = inner.GetFieldLayout(field);
        recorder.Record(UnitQueryKind.InstanceFieldOfDefinition, UnitQueryKeys.Of(field), Of(layout), layout);
        return layout;
    }

    private static UnitFingerprint Of(FieldLayout layout)
    {
        var answer = new UnitFingerprintBuilder();
        answer.Add(layout);
        return answer.Finish();
    }
}

internal sealed class RecordingStaticFieldLayoutProvider(
    IStaticFieldLayoutProvider inner,
    IUnitDependencyRecorder recorder) : IStaticFieldLayoutProvider
{
    public StaticFieldLayout GetStaticFieldLayout(EntityKey field)
    {
        var layout = inner.GetStaticFieldLayout(field);
        recorder.Record(UnitQueryKind.StaticFieldOfDefinition, UnitQueryKeys.Of(field), Of(layout), layout);
        return layout;
    }

    public StaticFieldLayout GetStaticFieldLayout(FieldInstanceModel field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var layout = inner.GetStaticFieldLayout(field);
        recorder.Record(UnitQueryKind.StaticField, field.CanonicalName, Of(layout), layout);
        return layout;
    }

    private static UnitFingerprint Of(StaticFieldLayout layout)
    {
        var answer = new UnitFingerprintBuilder();
        answer.Add(layout);
        return answer.Finish();
    }
}

internal sealed class RecordingStaticDataLayout(
    IStaticDataLayout inner,
    IUnitDependencyRecorder recorder) : IStaticDataLayout
{
    // The collections are fixed for the compile; fingerprint each once per worker.
    private UnitFingerprint? _staticRoots;
    private UnitFingerprint? _segments;
    private RecordingLookup<string, int>? _typeObjects;

    public StringLayout GetStringLayout(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var layout = inner.GetStringLayout(value);
        var answer = new UnitFingerprintBuilder();
        answer.Add(layout);
        recorder.Record(UnitQueryKind.StringLayout, value, answer.Finish(), layout);
        return layout;
    }

    public int StaticDataEnd => UnitRecording.Scalar(recorder, UnitQueryKind.StaticDataEnd, "", inner.StaticDataEnd);

    public ImmutableArray<int> StaticRootAddresses
    {
        get
        {
            var roots = inner.StaticRootAddresses;
            _staticRoots ??= UnitRecording.Of(roots);
            recorder.Record(UnitQueryKind.StaticRootAddresses, "", _staticRoots.Value, roots);
            return roots;
        }
    }

    public ImmutableArray<DataSegment> DataSegments
    {
        get
        {
            var segments = inner.DataSegments;
            if (_segments is null)
            {
                var answer = new UnitFingerprintBuilder();
                answer.Add(segments.Length);
                foreach (var segment in segments)
                {
                    answer.Add(segment.Address);
                    answer.Add(segment.Data.AsSpan());
                }
                _segments = answer.Finish();
            }
            recorder.Record(UnitQueryKind.DataSegments, "", _segments.Value, segments);
            return segments;
        }
    }

    public int TypeTableAddress =>
        UnitRecording.Scalar(recorder, UnitQueryKind.TypeTable, "address", inner.TypeTableAddress);


    public IEntryLookup<string, int> TypeObjectAddresses =>
        _typeObjects ??= new(inner.TypeObjectAddresses, recorder, UnitQueryKind.TypeObjectAddress,
            static key => key, static (ref answer, address) => answer.Add(address));

    public int TypeObjectTableAddress =>
        UnitRecording.Scalar(recorder, UnitQueryKind.TypeObjectTable, "address", inner.TypeObjectTableAddress);

    public int TypeObjectCacheAddress =>
        UnitRecording.Scalar(recorder, UnitQueryKind.TypeObjectTable, "cache", inner.TypeObjectCacheAddress);

    public int TypeObjectCount =>
        UnitRecording.Scalar(recorder, UnitQueryKind.TypeObjectTable, "count", inner.TypeObjectCount);

    public int TypeObjectSize =>
        UnitRecording.Scalar(recorder, UnitQueryKind.TypeObjectTable, "size", inner.TypeObjectSize);
}

internal sealed class RecordingMemberDescriptorLayout(
    IMemberDescriptorLayout inner,
    IUnitDependencyRecorder recorder) : IMemberDescriptorLayout
{
    public int GetDescriptorAddress(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return UnitRecording.Scalar(recorder, UnitQueryKind.MethodDescriptorAddress,
            method.CanonicalName, inner.GetDescriptorAddress(method));
    }

    public int GetDescriptorAddress(FieldInstanceModel field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return UnitRecording.Scalar(recorder, UnitQueryKind.FieldDescriptorAddress,
            field.CanonicalName, inner.GetDescriptorAddress(field));
    }

    public int DeclaringTypeIdOffset => UnitRecording.Scalar(recorder,
        UnitQueryKind.MemberDescriptorOffsets, "declaring-type-id", inner.DeclaringTypeIdOffset);

    public int RequiresDeclaringTypeOffset => UnitRecording.Scalar(recorder,
        UnitQueryKind.MemberDescriptorOffsets, "requires-declaring-type", inner.RequiresDeclaringTypeOffset);
}

internal sealed class RecordingManagedExceptionObjectProvider(
    IManagedExceptionObjectProvider inner,
    IUnitDependencyRecorder recorder) : IManagedExceptionObjectProvider
{
    public int GetExceptionObject(ManagedExceptionKind kind) => UnitRecording.Scalar(recorder,
        UnitQueryKind.ExceptionObject, kind.ToString(), inner.GetExceptionObject(kind));
}

// Emission requires the descriptor source to publish enum metadata too, as the layout
// snapshot does, and reads the structure-marshalling and uninitialized-object tables from it
// where it publishes them; every table is recorded whole.
internal sealed class RecordingTypeDescriptorSource(
    ITypeDescriptorSource inner,
    IUnitDependencyRecorder recorder) :
    ITypeDescriptorSource, IEnumMetadataSource, IStructMarshallingSource, IUninitializedObjectSource
{
    private UnitFingerprint? _enums;
    private UnitFingerprint? _marshalling;
    private UnitFingerprint? _uninitialized;
    private UnitFingerprint? _types;
    private UnitFingerprint? _constructed;
    private UnitFingerprint? _values;
    private UnitFingerprint? _metadata;

    public ImmutableArray<TypeDescriptorLayout> TypeDescriptors
    {
        get
        {
            var descriptors = inner.TypeDescriptors;
            _types ??= UnitRecording.Of(descriptors, static (ref builder, descriptor) =>
            {
                builder.Add(descriptor.Type);
                builder.Add(descriptor.TypeId);
                builder.Add(descriptor.BaseTypeId);
                builder.Add(descriptor.ObjectSize);
                builder.Add(descriptor.ReferenceOffsets);
                builder.Add(descriptor.Finalizer);
                builder.Add(descriptor.AssignableTypeIds);
                builder.Add(descriptor.IsInterface);
            });
            recorder.Record(UnitQueryKind.TypeDescriptors, "", _types.Value, descriptors);
            return descriptors;
        }
    }

    public ImmutableArray<EnumMetadataLayout> EnumMetadata
    {
        get
        {
            var enums = ((IEnumMetadataSource)inner).EnumMetadata;
            _enums ??= UnitRecording.Of(enums, static (ref builder, layout) =>
            {
                builder.Add(layout.Type);
                builder.Add(layout.EnumType);
                builder.Add(layout.TypeId);
                builder.Add(layout.Address);
                builder.Add(layout.UnderlyingType);
                builder.Add(layout.IsFlags);
                builder.Add(layout.IsOpenDefinition);
                builder.Add(layout.Members.Length);
                foreach (var member in layout.Members)
                {
                    builder.Add(member.Name);
                    builder.Add((long)member.RawValue);
                    builder.Add(member.NameLayout);
                }
            });
            recorder.Record(UnitQueryKind.EnumMetadata, "", _enums.Value, enums);
            return enums;
        }
    }

    public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors
    {
        get
        {
            var descriptors = inner.ConstructedTypeDescriptors;
            _constructed ??= UnitRecording.Of(descriptors, static (ref builder, descriptor) =>
            {
                builder.Add(descriptor.Type);
                builder.Add(descriptor.TypeId);
                builder.Add(descriptor.BaseTypeId);
                builder.Add(descriptor.ObjectSize);
                builder.Add(descriptor.ReferenceOffsets);
                builder.Add(descriptor.Finalizer);
                builder.Add(descriptor.AssignableTypeIds);
                builder.Add(descriptor.IsInterface);
            });
            recorder.Record(UnitQueryKind.ConstructedTypeDescriptors, "", _constructed.Value, descriptors);
            return descriptors;
        }
    }

    public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors
    {
        get
        {
            var descriptors = inner.ValueTypeDescriptors;
            _values ??= UnitRecording.Of(descriptors, static (ref builder, descriptor) =>
            {
                builder.Add(descriptor.Type);
                builder.Add(descriptor.TypeId);
                builder.Add(descriptor.Size);
                builder.Add(descriptor.BoxedPayloadOffset);
                builder.Add(descriptor.ReferenceOffsets);
            });
            recorder.Record(UnitQueryKind.ValueTypeDescriptors, "", _values.Value, descriptors);
            return descriptors;
        }
    }

    public ImmutableArray<MetadataTypeDescriptorLayout> MetadataTypeDescriptors
    {
        get
        {
            var descriptors = inner.MetadataTypeDescriptors;
            _metadata ??= UnitRecording.Of(descriptors, static (ref builder, descriptor) =>
            {
                builder.Add(descriptor.Type);
                builder.Add(descriptor.TypeId);
            });
            recorder.Record(UnitQueryKind.MetadataTypeDescriptors, "", _metadata.Value, descriptors);
            return descriptors;
        }
    }

    public ImmutableArray<StructMarshallingLayout> StructMarshalling
    {
        get
        {
            var layouts = (inner as IStructMarshallingSource ?? EmptyRuntimeTypeDataSource.Instance).StructMarshalling;
            _marshalling ??= UnitRecording.Of(layouts, static (ref builder, layout) =>
            {
                builder.Add(layout.Type);
                builder.Add(layout.TypeId);
                builder.Add(layout.Address);
            });
            recorder.Record(UnitQueryKind.StructMarshalling, "", _marshalling.Value, layouts);
            return layouts;
        }
    }

    public ImmutableArray<CliTypeIdentity> UninitializedObjectTypes
    {
        get
        {
            var types = (inner as IUninitializedObjectSource ?? EmptyRuntimeTypeDataSource.Instance).UninitializedObjectTypes;
            _uninitialized ??= UnitRecording.Of(types, static (ref builder, type) => builder.Add(type));
            recorder.Record(UnitQueryKind.UninitializedObjectTypes, "", _uninitialized.Value, types);
            return types;
        }
    }
}

internal delegate void UnitFingerprintAdder<T>(ref UnitFingerprintBuilder builder, T value);

/// <summary>Fingerprints of recorded answers shared by the recording decorators.</summary>
internal static class UnitRecording
{
    public static int Scalar(IUnitDependencyRecorder recorder, UnitQueryKind kind, string key, int value)
    {
        var answer = new UnitFingerprintBuilder();
        answer.Add(value);
        recorder.Record(kind, key, answer.Finish(), value);
        return value;
    }

    public static UnitFingerprint Of(ImmutableArray<int> values)
    {
        var answer = new UnitFingerprintBuilder();
        answer.Add(values);
        return answer.Finish();
    }

    public static UnitFingerprint Of<T>(ImmutableArray<T> values, UnitFingerprintAdder<T> add)
    {
        var answer = new UnitFingerprintBuilder();
        answer.Add(values.Length);
        foreach (var value in values) add(ref answer, value);
        return answer.Finish();
    }
}
