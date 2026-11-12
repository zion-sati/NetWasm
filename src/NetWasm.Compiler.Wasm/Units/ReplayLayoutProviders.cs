using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Units;

// The layout providers of a compile that replays the last one: each answers a query by the
// key its recording provider recorded it under.

internal sealed class ReplayTypeLayoutProvider(UnitAnswers answers) : ITypeLayoutProvider
{
    public int StringTypeId => answers.Get<int>(UnitQueryKind.StringTypeId, "");

    public int TypeTypeId => answers.Get<int>(UnitQueryKind.TypeTypeId, "");

    public ObjectLayout GetObjectLayout(CliTypeIdentity type) =>
        answers.Get<ObjectLayout>(UnitQueryKind.ObjectLayout, type.CanonicalName);

    public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout)
    {
        var lookup = answers.Get<UnitLookup<ObjectLayout>>(UnitQueryKind.ObjectLayoutLookup, type.CanonicalName);
        layout = lookup.Value;
        return lookup.Found;
    }

    public ObjectLayout GetObjectLayout(EntityKey type) =>
        answers.Get<ObjectLayout>(UnitQueryKind.ObjectLayoutOfDefinition, UnitQueryKeys.Of(type));
}

internal sealed class ReplayValueLayoutProvider(UnitAnswers answers) : IValueLayoutProvider
{
    public ValueLayout GetValueLayout(CliTypeIdentity type) =>
        answers.Get<ValueLayout>(UnitQueryKind.ValueLayout, type.CanonicalName);
}

internal sealed class ReplayInstanceFieldLayoutProvider(UnitAnswers answers) : IInstanceFieldLayoutProvider
{
    public FieldLayout GetFieldLayout(FieldInstanceModel field) =>
        answers.Get<FieldLayout>(UnitQueryKind.InstanceField, field.CanonicalName);

    public FieldLayout GetFieldLayout(EntityKey field) =>
        answers.Get<FieldLayout>(UnitQueryKind.InstanceFieldOfDefinition, UnitQueryKeys.Of(field));
}

internal sealed class ReplayStaticFieldLayoutProvider(UnitAnswers answers) : IStaticFieldLayoutProvider
{
    public StaticFieldLayout GetStaticFieldLayout(EntityKey field) =>
        answers.Get<StaticFieldLayout>(UnitQueryKind.StaticFieldOfDefinition, UnitQueryKeys.Of(field));

    public StaticFieldLayout GetStaticFieldLayout(FieldInstanceModel field) =>
        answers.Get<StaticFieldLayout>(UnitQueryKind.StaticField, field.CanonicalName);
}

internal sealed class ReplayStaticDataLayout(UnitAnswers answers) : IStaticDataLayout
{
    public StringLayout GetStringLayout(string value) => answers.Get<StringLayout>(UnitQueryKind.StringLayout, value);

    public int StaticDataEnd => answers.Get<int>(UnitQueryKind.StaticDataEnd, "");

    public ImmutableArray<int> StaticRootAddresses => answers.Get<ImmutableArray<int>>(UnitQueryKind.StaticRootAddresses, "");

    public ImmutableArray<DataSegment> DataSegments => answers.Get<ImmutableArray<DataSegment>>(UnitQueryKind.DataSegments, "");

    public int TypeTableAddress => answers.Get<int>(UnitQueryKind.TypeTable, "address");


    public IEntryLookup<string, int> TypeObjectAddresses { get; } =
        new ReplayLookup<string, int>(answers, UnitQueryKind.TypeObjectAddress, static key => key);

    public int TypeObjectTableAddress => answers.Get<int>(UnitQueryKind.TypeObjectTable, "address");

    public int TypeObjectCacheAddress => answers.Get<int>(UnitQueryKind.TypeObjectTable, "cache");

    public int TypeObjectCount => answers.Get<int>(UnitQueryKind.TypeObjectTable, "count");

    public int TypeObjectSize => answers.Get<int>(UnitQueryKind.TypeObjectTable, "size");
}

internal sealed class ReplayMemberDescriptorLayout(UnitAnswers answers) : IMemberDescriptorLayout
{
    public int GetDescriptorAddress(MethodInstanceModel method) =>
        answers.Get<int>(UnitQueryKind.MethodDescriptorAddress, method.CanonicalName);

    public int GetDescriptorAddress(FieldInstanceModel field) =>
        answers.Get<int>(UnitQueryKind.FieldDescriptorAddress, field.CanonicalName);

    public int DeclaringTypeIdOffset => answers.Get<int>(UnitQueryKind.MemberDescriptorOffsets, "declaring-type-id");

    public int RequiresDeclaringTypeOffset =>
        answers.Get<int>(UnitQueryKind.MemberDescriptorOffsets, "requires-declaring-type");
}

internal sealed class ReplayManagedExceptionObjectProvider(UnitAnswers answers) : IManagedExceptionObjectProvider
{
    public int GetExceptionObject(ManagedExceptionKind kind) => answers.Get<int>(UnitQueryKind.ExceptionObject, kind.ToString());
}

internal sealed class ReplayTypeDescriptorSource(UnitAnswers answers) : ITypeDescriptorSource, IEnumMetadataSource
{
    public ImmutableArray<TypeDescriptorLayout> TypeDescriptors =>
        answers.Get<ImmutableArray<TypeDescriptorLayout>>(UnitQueryKind.TypeDescriptors, "");

    public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors =>
        answers.Get<ImmutableArray<ConstructedTypeDescriptorLayout>>(UnitQueryKind.ConstructedTypeDescriptors, "");

    public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors =>
        answers.Get<ImmutableArray<ValueTypeDescriptorLayout>>(UnitQueryKind.ValueTypeDescriptors, "");

    public ImmutableArray<MetadataTypeDescriptorLayout> MetadataTypeDescriptors =>
        answers.Get<ImmutableArray<MetadataTypeDescriptorLayout>>(UnitQueryKind.MetadataTypeDescriptors, "");

    public ImmutableArray<EnumMetadataLayout> EnumMetadata =>
        answers.Get<ImmutableArray<EnumMetadataLayout>>(UnitQueryKind.EnumMetadata, "");
}

/// <summary>The target layout of a compile that lays nothing out itself.</summary>
internal sealed record UnitTargetLayout(WasmTargetLayout Target) : ITargetLayout;
