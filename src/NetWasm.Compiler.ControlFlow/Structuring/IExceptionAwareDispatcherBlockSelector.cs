using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IExceptionAwareDispatcherBlockSelector
{
    ImmutableHashSet<int> Select(
        ControlFlowStructuringState state,
        ImmutableHashSet<int> component,
        ImmutableHashSet<int> allowed);
}
