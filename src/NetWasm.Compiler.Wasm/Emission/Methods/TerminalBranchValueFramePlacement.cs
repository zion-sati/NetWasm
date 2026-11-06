using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record TerminalBranchValueFramePlacement(
    TerminalBranchValueFramePlan Plan,
    ValueFrameLayout FramedLayout)
{
    // The framed arm's map. Its unframed arm keeps the same storage in Wasm
    // locals, so the method's safepoints still publish it.
    public ValueFrameReferenceMap FramedReferences { get; init; } = ValueFrameReferenceMap.Empty;

    // With the root frame deferred, the framed arm's roots and authoritative slots; the
    // unframed arm has neither, as nothing it reaches holds a root.
    public MethodRootMap? FramedRootMap { get; init; }

    public ImmutableDictionary<RootSource, int> FramedAuthoritativeRootSlots { get; init; } = [];
}
