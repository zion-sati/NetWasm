using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record FilterEnvironmentLayout(
    int Size,
    int RootSlotCount,
    int RootFrameOffset,
    ImmutableDictionary<CapturedSlot, FilterCapture> Captures)
{
    public ImmutableDictionary<int, int> AnchorOffsets { get; init; } = [];
    public int ParentRootFrameOffset { get; init; }

    // Whether the method has no filter environment: no frame, roots, captures or anchors.
    // Where an empty one would sit does not matter.
    public bool IsEmpty => Size == 0 && RootSlotCount == 0 && Captures.IsEmpty && AnchorOffsets.IsEmpty;

    public static FilterEnvironmentLayout Empty { get; } = new(
        0,
        0,
        0,
        []);
}
