using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

// A SIMD vector's slot is aligned as RyuJIT aligns a SIMD local's stack home
// (getSIMDTypeAlignment): an 8-byte Vector2 on 8, and the 12- and 16-byte
// vectors on 16. Frames start on 16 bytes, so no access to a vector's slot
// straddles a cache line; on x64 a store across a line followed by a load of
// the same bytes waits for the store to reach the cache. Every other slot keeps
// its storage's alignment. Only frame slots take this: a vector inside an
// object, an array or another struct keeps the layout of its fields.
internal sealed class SimdValueFrameSlotAlignmentResolver : IValueFrameSlotAlignmentResolver
{
    public int Resolve(CliTypeIdentity type, int alignment)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(alignment);
        return Math.Max(alignment, Preferred(type));
    }

    private static int Preferred(CliTypeIdentity type) => type switch
    {
        { FullName: "System.Numerics.Vector2" } => sizeof(long),
        { StackKind: CliValueKind.V128 } => 16,
        _ => 1,
    };
}
