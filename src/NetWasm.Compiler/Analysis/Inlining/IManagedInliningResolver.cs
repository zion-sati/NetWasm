using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

internal interface IManagedInliningResolver
{
    ManagedInliningPlan? Resolve(ManagedInliningRequest request);
}

internal sealed record ManagedInliningRequest(
    MethodInstanceModel Caller,
    CilInstruction Call)
{
    // The call came from a callee inlined in an earlier round.
    public bool Nested { get; init; }

    // A round past the general depth, which takes only small callees that do not allocate.
    public bool SmallOnly { get; init; }

    // A backward branch of the caller spans the call, so it runs repeatedly.
    public bool InLoop { get; init; }
}

internal sealed record ManagedInliningPlan(
    MethodInstanceModel Method,
    CilMethodBody Body)
{
    public ImmutableDictionary<int, ImmutableArray<CliValueKind>>
        InstructionEntryStacks
    { get; init; } =
        ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty;
}
