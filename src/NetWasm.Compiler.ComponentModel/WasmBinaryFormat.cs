using System;

namespace NetWasm.Compiler.ComponentModel;

internal static class WasmBinaryFormat
{
    public static ReadOnlySpan<byte> Header =>
        [0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00];

    public const byte ExportSection = 7;
    public const byte MemoryExternalKind = 2;
}
