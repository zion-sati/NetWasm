using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.WasmSimdOpcodes;
using static NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors.SimdByteShuffles;
using static NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors.SimdLaneOpcodes;
using static NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors.SimdVectorCode;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors;

// Load and Store at an address.
internal sealed class SimdVectorMemoryIntrinsicEmitter(
    ISimdVectorLaneGuard lanes) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (!lanes.TryResolve(request, code, out var resolved))
        {
            return;
        }
        var lane = resolved.Lane;
        if (request.Intrinsic == RuntimeIntrinsic.SimdLoad)
        {
            ElementAddress(request, code, 0, lane);
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.SimdPrefixed,
                WasmInstructionOperand.PrefixedTriple(V128Load, 0, 0)));
            Set(code, request.Local(0, CliValueKind.V128));
            return;
        }
        // Store.
        ElementAddress(request, code, 1, lane);
        Load(request, code, 0);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.SimdPrefixed,
            WasmInstructionOperand.PrefixedTriple(V128Store, 0, 0)));
    }
}
