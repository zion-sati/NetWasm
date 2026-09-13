using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow;

public sealed record ControlFlowGraphAnalysis(
    int Entry,
    ImmutableHashSet<int> Blocks,
    ImmutableDictionary<int, ImmutableHashSet<int>> Dominators,
    ImmutableDictionary<int, ImmutableHashSet<int>> PostDominators,
    ImmutableArray<ImmutableHashSet<int>> StronglyConnectedComponents,
    ImmutableArray<ImmutableHashSet<int>> NaturalLoops);
