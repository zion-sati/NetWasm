using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record FilterCapture(
    CapturedSlot Slot,
    CliTypeIdentity Type,
    int Offset,
    ImmutableArray<(int ByteOffset, int RootSlot)> Roots);
