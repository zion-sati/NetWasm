// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record FilterEnvironmentLayout(
    int Size,
    int RootSlotCount,
    int RootFrameOffset,
    ImmutableDictionary<CapturedSlot, FilterCapture> Captures)
{
    public ImmutableDictionary<int, int> AnchorOffsets { get; init; } = [];
    public int ParentRootFrameOffset { get; init; }

    // The method's root map as its own safepoints publish it (after the value frame's map and authoritative slots
    // take their sources), which its filter funclets publish by, into the same root frame.
    public MethodRootMap? ParentRootMap { get; init; }

    // The layout without its own root frame, for a method whose value frame map covers every capture.
    public FilterEnvironmentLayout WithoutRootFrame() => this with
    {
        RootSlotCount = 0,
        Captures = Captures.ToImmutableDictionary(pair => pair.Key, pair => pair.Value with { Roots = [] }),
    };

    // Whether the method has no filter environment: no frame, roots, captures or anchors.
    // Where an empty one would sit does not matter.
    public bool IsEmpty => Size == 0 && RootSlotCount == 0 && Captures.IsEmpty && AnchorOffsets.IsEmpty;

    public static FilterEnvironmentLayout Empty { get; } = new(
        0,
        0,
        0,
        []);
}
