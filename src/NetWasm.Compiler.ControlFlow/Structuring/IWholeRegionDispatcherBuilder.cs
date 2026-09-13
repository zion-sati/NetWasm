using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal interface IWholeRegionDispatcherBuilder
{
    StructuredDispatcherDraft Build(ControlFlowStructuringState state, int? entry, ImmutableHashSet<int> component);
}
