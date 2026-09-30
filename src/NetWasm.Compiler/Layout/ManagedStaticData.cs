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
    public ImmutableArray<EnumMetadataLayout> EnumMetadata { get; init; } = [];

    public ImmutableDictionary<string, int> MethodDescriptors { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    public ImmutableDictionary<string, int> FieldDescriptors { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    public ImmutableDictionary<string, int> PropertyDescriptors { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    public int MemberDescriptorDeclaringTypeIdOffset { get; init; }

    public int MemberDescriptorRequiresDeclaringTypeOffset { get; init; }

    public int TypeFactsTableAddress { get; init; }

    public int TypeFactsTableCount { get; init; }

}
