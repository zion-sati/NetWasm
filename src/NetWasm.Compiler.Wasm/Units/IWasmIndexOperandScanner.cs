using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Units;

internal interface IWasmIndexOperandScanner
{
    // Lists, in body order, every module index a function body (its local declarations
    // and code) encodes. Throws InvalidDataException for an instruction it cannot decode.
    ImmutableArray<WasmIndexOperand> Scan(ReadOnlySpan<byte> body);
}
