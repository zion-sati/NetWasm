using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public sealed record LoopConditionSelection(
    ControlFlowGraph Graph,
    ImmutableHashSet<int> Component,
    int Header);
