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
    public Dictionary<string, int> TypeObjects { get; } = new(StringComparer.Ordinal);
    public int TypeObjectTableAddress { get; set; }
    public int TypeObjectCacheAddress { get; set; }
    public int TypeObjectCount { get; set; }
    public int TypeObjectSize { get; set; }
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
    public ImmutableArray<MetadataTypeDescriptorLayout>.Builder MetadataTypeDescriptors { get; } =
        ImmutableArray.CreateBuilder<MetadataTypeDescriptorLayout>();
    public Dictionary<CliTypeIdentity, PendingEnumMetadata> PendingEnumMetadata { get; } = [];
    public ImmutableArray<EnumMetadataLayout>.Builder EnumMetadata { get; } =
        ImmutableArray.CreateBuilder<EnumMetadataLayout>();
    public List<PendingStructMarshalling> PendingStructMarshalling { get; } = [];
    public ImmutableArray<StructMarshallingLayout>.Builder StructMarshalling { get; } =
        ImmutableArray.CreateBuilder<StructMarshallingLayout>();
    public Dictionary<string, int> MethodDescriptors { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, int> FieldDescriptors { get; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, int> PropertyDescriptors { get; } =
        new(StringComparer.Ordinal);
    public List<PendingRuntimeTypeFacts> PendingTypeFacts { get; } = [];
    public int TypeTableAddress { get; set; }
    // The assemblies of the registered types, at their type table index - 1.
    public ImmutableArray<AssemblyIdentity> RuntimeAssemblies { get; set; } = [];
    public int? MemberDescriptorDeclaringTypeIdOffset { get; set; }
    public int? MemberDescriptorRequiresDeclaringTypeOffset { get; set; }
    public ImmutableArray<int>.Builder StaticRoots { get; } =
        ImmutableArray.CreateBuilder<int>();
    public int Cursor { get; set; } = ManagedLayoutSnapshot.StaticDataStart;
}
