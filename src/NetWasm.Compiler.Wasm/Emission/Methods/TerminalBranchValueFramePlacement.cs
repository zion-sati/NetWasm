namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record TerminalBranchValueFramePlacement(
    TerminalBranchValueFramePlan Plan,
    ValueFrameLayout FramedLayout)
{
    // The framed arm's map. Its unframed arm keeps the same storage in Wasm
    // locals, so the method's safepoints still publish it.
    public ValueFrameReferenceMap FramedReferences { get; init; } = ValueFrameReferenceMap.Empty;
}
