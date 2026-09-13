using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record FilterEnvironmentLayout(
    int Size,
    int RootSlotCount,
    int RootFrameOffset,
    ImmutableDictionary<CapturedSlot, FilterCapture> Captures)
{
    public static FilterEnvironmentLayout Empty { get; } = new(
        0,
        0,
        0,
        []);
}
