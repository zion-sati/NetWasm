using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IBranchReachabilityOverlapClassifier
{
    bool Classify(ControlFlowStructuringState state, int first, int second, int? stop, ImmutableHashSet<int> allowed);
}
