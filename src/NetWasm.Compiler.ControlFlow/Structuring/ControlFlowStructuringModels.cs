using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed record LoopRegion(
    int Header,
    int Condition,
    int Continue,
    int ConditionExit,
    int Exit,
    ImmutableHashSet<int> CoreComponent,
    ImmutableHashSet<int> BodyComponent,
    ImmutableHashSet<int> ConditionExitComponent)
{
    public ImmutableHashSet<int> Component =>
        BodyComponent.Union(ConditionExitComponent);
}

internal sealed record ControlFlowDomain(
    int Entry,
    ImmutableHashSet<int> Blocks);

internal sealed record NaturalLoopCandidate(
    ImmutableHashSet<int> Component,
    ControlFlowDomain Domain);

internal sealed record ExceptionGroupSource(
    int TryOffset,
    int TryLength,
    ImmutableArray<CilExceptionRegion> Regions)
{
    public int TryEnd => checked(TryOffset + TryLength);
    public int Extent => Regions.Max(region =>
        checked(region.HandlerOffset + region.HandlerLength));
}

internal enum ExceptionScopeKind
{
    Protected,
    Filter,
    Handler,
}

internal readonly record struct ExceptionScope(
    ExceptionScopeKind Kind,
    int ClauseIndex,
    int Start,
    int End)
{
    public int Length => checked(End - Start);
}
