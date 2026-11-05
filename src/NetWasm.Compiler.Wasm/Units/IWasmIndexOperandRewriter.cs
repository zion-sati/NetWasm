using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Units;

internal interface IWasmIndexOperandRewriter
{
    // Copies a function body with each scanned index replaced by its remapped value,
    // encoded as the compiler encodes it: minimal LEB128.
    byte[] Rewrite(
        ReadOnlySpan<byte> body,
        ImmutableArray<WasmIndexOperand> operands,
        IWasmIndexRemapping remapping);
}
