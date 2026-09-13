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
}
