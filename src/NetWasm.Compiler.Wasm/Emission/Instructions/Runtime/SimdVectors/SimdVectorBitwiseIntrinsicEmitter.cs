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

// The bitwise operations, OnesComplement, ConditionalSelect and the shifts.
internal sealed class SimdVectorBitwiseIntrinsicEmitter(
    ISimdVectorLaneGuard lanes) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (!lanes.TryResolve(request, code, out var resolved))
        {
            return;
        }
        var lane = resolved.Lane;
        switch (request.Intrinsic)
        {
            case RuntimeIntrinsic.SimdShiftLeft:
            case RuntimeIntrinsic.SimdShiftRightArithmetic:
            case RuntimeIntrinsic.SimdShiftRightLogical:
                // Floating lanes shift their bits, as upstream's Scalar<T> does,
                // and Wasm masks the count by the lane width, as .NET does.
                Load(request, code, 0);
                Get(code, request.Local(1, CliValueKind.I4));
                Simd(code, Shift(request.Intrinsic, lane));
                break;
            case RuntimeIntrinsic.SimdConditionalSelect:
                Load(request, code, 1);
                Load(request, code, 2);
                Load(request, code, 0);
                Simd(code, V128BitSelect);
                break;
            case RuntimeIntrinsic.SimdOnesComplement:
                Load(request, code, 0);
                Simd(code, V128Not);
                break;
            default:
                // BitwiseAnd, BitwiseOr, Xor and AndNot.
                Load(request, code, 0);
                Load(request, code, 1);
                Simd(code, Binary(request.Intrinsic, lane));
                break;
        }
        Set(code, request.Local(0, CliValueKind.V128));
    }
}
