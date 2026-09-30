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
    public Dictionary<string, int> MethodDescriptors { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, int> FieldDescriptors { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, int> PropertyDescriptors { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<int, int> TypeFacts { get; } = [];
    public List<PendingRuntimeTypeFacts> PendingTypeFacts { get; } = [];
    public int TypeFactsTableAddress { get; set; }
    public int TypeFactsTableCount { get; set; }
    public int? MemberDescriptorDeclaringTypeIdOffset { get; set; }
    public int? MemberDescriptorRequiresDeclaringTypeOffset { get; set; }
    public ImmutableArray<int>.Builder StaticRoots { get; } =
        ImmutableArray.CreateBuilder<int>();
    public int Cursor { get; set; } = ManagedLayoutSnapshot.StaticDataStart;
}
