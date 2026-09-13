using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface ILoopActiveBlockRetentionSelector
{
    ImmutableHashSet<int> Select(
        ControlFlowStructuringState state,
        LoopRegion loop);
}
