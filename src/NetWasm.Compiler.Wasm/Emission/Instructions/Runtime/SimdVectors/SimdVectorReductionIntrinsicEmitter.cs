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

// The scalars of a vector: ExtractMostSignificantBits, ToScalar and Sum.
internal sealed class SimdVectorReductionIntrinsicEmitter(
    ISimdVectorLaneGuard lanes) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (!lanes.TryResolve(request, code, out var resolved))
        {
            return;
        }
        var lane = resolved.Lane;
        var signature = request.Method.Signature;
        switch (request.Intrinsic)
        {
            case RuntimeIntrinsic.SimdExtractMostSignificantBits:
                Load(request, code, 0);
                Simd(code, BitMask(lane.Bits));
                Set(code, request.Local(0, CliValueKind.I4));
                return;
            case RuntimeIntrinsic.SimdToScalar:
                Load(request, code, 0);
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.SimdPrefixed,
                    WasmInstructionOperand.PrefixedPair(ExtractLane(lane), 0)));
                Set(code, request.Local(0, signature.ReturnType));
                return;
            default:
                // Sum.
                SumLanes(request, code, lane, 0, Lanes(lane));
                Normalize(code, lane);
                Set(code, request.Local(0, signature.ReturnType));
                break;
        }
    }

    // Pushes the sum of lanes [first, first + count) as the sum of its halves,
    // the order of the hardware's horizontal adds, so floating lanes sum as on
    // desktop. Integer sums wrap and do not depend on the order.
    private static void SumLanes(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        SimdLane lane,
        int first,
        int count)
    {
        if (count == 1)
        {
            LoadLane(request, code, 0, lane, first);
            return;
        }

        SumLanes(request, code, lane, first, count / 2);
        SumLanes(request, code, lane, first + count / 2, count / 2);
        code.Write(WasmInstruction.NoOperand(lane.Shape switch
        {
            SimdLaneShape.I64 => WasmOpcodes.I64Add,
            SimdLaneShape.F32 => WasmOpcodes.F32Add,
            SimdLaneShape.F64 => WasmOpcodes.F64Add,
            _ => WasmOpcodes.I32Add,
        }));
    }

    // An 8-bit or 16-bit result keeps its element type's range.
    private static void Normalize(IWasmInstructionWriter code, SimdLane lane)
    {
        switch (lane.Shape, lane.Unsigned)
        {
            case (SimdLaneShape.I8, false):
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Extend8Signed));
                break;
            case (SimdLaneShape.I16, false):
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Extend16Signed));
                break;
            case (SimdLaneShape.I8 or SimdLaneShape.I16, true):
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.I32Constant,
                    WasmInstructionOperand.Signed(lane.Shape == SimdLaneShape.I8 ? 0xFF : 0xFFFF)));
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
                break;
        }
    }
}
