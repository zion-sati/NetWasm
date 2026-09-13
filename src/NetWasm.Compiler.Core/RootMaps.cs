using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

public enum RootSourceKind
{
    Argument,
    Local,
    EvaluationStack,
    ManagedAddressArgument,
    ManagedAddressLocal,
    ManagedAddressEvaluationStack,
    AllocationTemporary,
}

public readonly record struct RootSource(
    RootSourceKind Kind,
    int Index,
    int ByteOffset = -1)
{
    public bool IsIndirect => ByteOffset >= 0;
}

public sealed record SafepointRootMap(
    int IlOffset,
    ImmutableArray<RootSource> Roots,
    ImmutableArray<RootSource> ConstructorCallRoots);

public sealed record MethodRootMap(
    EntityKey Method,
    ImmutableDictionary<RootSource, int> Slots,
    ImmutableDictionary<int, SafepointRootMap> Safepoints)
{
    public int SlotCount => Slots.Count;
}
