using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedStaticDataBuildState
{
    public Dictionary<EntityKey, StaticFieldLayout> StaticFields { get; } = [];
    public Dictionary<string, StaticFieldLayout> ConstructedStaticFields { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, StringLayout> Strings { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<ManagedExceptionKind, int> ExceptionObjects { get; } = [];
    public ImmutableArray<DataSegment>.Builder Segments { get; } =
        ImmutableArray.CreateBuilder<DataSegment>();
    public ImmutableArray<TypeDescriptorLayout>.Builder TypeDescriptors { get; } =
        ImmutableArray.CreateBuilder<TypeDescriptorLayout>();
    public ImmutableArray<ConstructedTypeDescriptorLayout>.Builder
        ConstructedTypeDescriptors
    { get; } =
            ImmutableArray.CreateBuilder<ConstructedTypeDescriptorLayout>();
    public ImmutableArray<ValueTypeDescriptorLayout>.Builder ValueTypeDescriptors { get; } =
        ImmutableArray.CreateBuilder<ValueTypeDescriptorLayout>();
    public Dictionary<EntityKey, PendingEnumMetadata> PendingEnumMetadata { get; } = [];
    public ImmutableArray<EnumMetadataLayout>.Builder EnumMetadata { get; } =
        ImmutableArray.CreateBuilder<EnumMetadataLayout>();
    public ImmutableArray<int>.Builder StaticRoots { get; } =
        ImmutableArray.CreateBuilder<int>();
    public int Cursor { get; set; } = ManagedLayoutSnapshot.StaticDataStart;
}
