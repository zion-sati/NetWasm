using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed record LoopExitPartition(
    ImmutableHashSet<int> ConditionExitExtension,
    ImmutableHashSet<int> BodyComponent,
    bool Overlaps);
