using NetWasm.Compiler.ControlFlow.Draft;
using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed record ExceptionAwareDispatcherShell(
    StructuredDispatcherDraft Dispatcher,
    ImmutableHashSet<int> OwnedBlocks);
