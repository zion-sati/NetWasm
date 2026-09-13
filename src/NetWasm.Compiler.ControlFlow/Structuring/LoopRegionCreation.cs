using System.Collections.Immutable;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed record LoopRegionCreation(
    LoopRegion? Region,
    ImmutableHashSet<int> DispatcherOwnedBlocks);
