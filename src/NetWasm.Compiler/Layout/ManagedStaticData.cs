using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed partial record ManagedStaticData(
    int EndAddress,
    ImmutableDictionary<EntityKey, StaticFieldLayout> StaticFields,
    ImmutableDictionary<string, StaticFieldLayout> ConstructedStaticFields,
    ImmutableDictionary<string, StringLayout> Strings,
    ImmutableDictionary<ManagedExceptionKind, int> ExceptionObjects,
    ImmutableArray<DataSegment> Segments,
    ImmutableArray<TypeDescriptorLayout> TypeDescriptors,
    ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors,
    ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors,
    ImmutableArray<int> StaticRootAddresses);

internal sealed partial record ManagedStaticData
{
    public ImmutableArray<MetadataTypeDescriptorLayout> MetadataTypeDescriptors { get; init; } = [];

    public ImmutableArray<EnumMetadataLayout> EnumMetadata { get; init; } = [];

    public ImmutableDictionary<string, int> MethodDescriptors { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    public ImmutableDictionary<string, int> FieldDescriptors { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    public ImmutableDictionary<string, int> PropertyDescriptors { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    public int MemberDescriptorDeclaringTypeIdOffset { get; init; }

    public int MemberDescriptorRequiresDeclaringTypeOffset { get; init; }

    public int TypeTableAddress { get; init; }

    // The assemblies Type.Assembly can report, at their type table index - 1; empty where unreachable.
    public ImmutableArray<AssemblyIdentity> RuntimeAssemblies { get; init; } = [];

    // Static Type objects by type canonical name, contiguous from the table address.
    public ImmutableDictionary<string, int> TypeObjects { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    public int TypeObjectTableAddress { get; init; }

    public int TypeObjectCacheAddress { get; init; }

    public int TypeObjectCount { get; init; }

    public int TypeObjectSize { get; init; }

}
