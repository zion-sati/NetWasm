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

// Convert, WidenLower, WidenUpper, Narrow and Reinterpret.
internal sealed class SimdVectorConversionIntrinsicEmitter(
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
            case RuntimeIntrinsic.SimdConvert:
                EmitConversion(request, code, lane, resolved.Result);
                break;
            case RuntimeIntrinsic.SimdWidenLower:
            case RuntimeIntrinsic.SimdWidenUpper:
                EmitWiden(request, code, lane, request.Intrinsic == RuntimeIntrinsic.SimdWidenUpper);
                break;
            case RuntimeIntrinsic.SimdNarrow:
                EmitNarrow(request, code, lane);
                break;
            default:
                // Reinterpret: the same bits.
                Load(request, code, 0);
                break;
        }
        Set(code, request.Local(0, CliValueKind.V128));
    }

    // The conversions between float and int lanes have SIMD instructions; the
    // 64-bit ones convert lane by lane. Floating values truncate toward zero and
    // saturate, with NaN becoming 0, as ConvertToInt32 and the rest do.
    private static void EmitConversion(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        SimdLane lane,
        SimdLane result)
    {
        switch (lane.Shape, result.Shape)
        {
            case (SimdLaneShape.F32, SimdLaneShape.I32):
                Load(request, code, 0);
                Simd(code, result.Unsigned ? I32x4TruncateSaturateF32x4Unsigned : I32x4TruncateSaturateF32x4Signed);
                return;
            case (SimdLaneShape.I32, SimdLaneShape.F32):
                Load(request, code, 0);
                Simd(code, lane.Unsigned ? F32x4ConvertI32x4Unsigned : F32x4ConvertI32x4Signed);
                return;
            case (SimdLaneShape.I64, SimdLaneShape.F64):
                PerLane(code, result, index =>
                {
                    LoadLane(request, code, 0, lane, index);
                    code.Write(WasmInstruction.NoOperand(
                        lane.Unsigned ? WasmOpcodes.F64ConvertI64Unsigned : WasmOpcodes.F64ConvertI64Signed));
                });
                return;
            case (SimdLaneShape.F64, SimdLaneShape.I64):
                PerLane(code, result, index =>
                {
                    LoadLane(request, code, 0, lane, index);
                    Saturate(code, result.Unsigned
                        ? WasmOpcodes.I64TruncateSaturateF64Unsigned
                        : WasmOpcodes.I64TruncateSaturateF64Signed);
                });
                return;
            default:
                throw Unsupported(request.Intrinsic, lane);
        }
    }

    private static void EmitWiden(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        SimdLane lane,
        bool upper)
    {
        Load(request, code, 0);
        if (lane.Shape == SimdLaneShape.F32)
        {
            if (upper)
            {
                Load(request, code, 0);
                Shuffle(code, UpperWords);
            }
            Simd(code, F64x2PromoteLowF32x4);
            return;
        }

        Simd(code, (lane.Shape, upper, lane.Unsigned) switch
        {
            (SimdLaneShape.I8, false, false) => I16x8ExtendLowI8x16Signed,
            (SimdLaneShape.I8, true, false) => I16x8ExtendHighI8x16Signed,
            (SimdLaneShape.I8, false, true) => I16x8ExtendLowI8x16Unsigned,
            (SimdLaneShape.I8, true, true) => I16x8ExtendHighI8x16Unsigned,
            (SimdLaneShape.I16, false, false) => I32x4ExtendLowI16x8Signed,
            (SimdLaneShape.I16, true, false) => I32x4ExtendHighI16x8Signed,
            (SimdLaneShape.I16, false, true) => I32x4ExtendLowI16x8Unsigned,
            (SimdLaneShape.I16, true, true) => I32x4ExtendHighI16x8Unsigned,
            (SimdLaneShape.I32, false, false) => I64x2ExtendLowI32x4Signed,
            (SimdLaneShape.I32, true, false) => I64x2ExtendHighI32x4Signed,
            (SimdLaneShape.I32, false, true) => I64x2ExtendLowI32x4Unsigned,
            (SimdLaneShape.I32, true, true) => I64x2ExtendHighI32x4Unsigned,
            _ => throw Unsupported(request.Intrinsic, lane),
        });
    }

    // Narrow truncates each lane, so it keeps the low half of every lane of
    // both vectors; Wasm's narrowing instructions saturate instead.
    private static void EmitNarrow(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        SimdLane lane)
    {
        if (lane.Shape == SimdLaneShape.F64)
        {
            Load(request, code, 0);
            Simd(code, F32x4DemoteF64x2Zero);
            Load(request, code, 1);
            Simd(code, F32x4DemoteF64x2Zero);
            Shuffle(code, LowWords);
            return;
        }

        Load(request, code, 0);
        Load(request, code, 1);
        Shuffle(code, lane.Shape switch
        {
            SimdLaneShape.I16 => EvenBytes,
            SimdLaneShape.I32 => EvenHalfwords,
            SimdLaneShape.I64 => EvenWords,
            _ => throw Unsupported(request.Intrinsic, lane),
        });
    }
}
