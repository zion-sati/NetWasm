using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;
using static NetWasm.Compiler.Wasm.WasmSimdOpcodes;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors;

// The Wasm SIMD opcode of each operation for a lane.
internal static class SimdLaneOpcodes
{
    internal static int Lanes(SimdLane lane) => 16 / LaneSize(lane);

    internal static uint Binary(RuntimeIntrinsic intrinsic, SimdLane lane) => intrinsic switch
    {
        RuntimeIntrinsic.SimdBitwiseAnd => V128And,
        RuntimeIntrinsic.SimdBitwiseOr => V128Or,
        RuntimeIntrinsic.SimdExclusiveOr => V128Xor,
        RuntimeIntrinsic.SimdAndNot => V128AndNot,
        RuntimeIntrinsic.SimdAdd => lane.Shape switch
        {
            SimdLaneShape.I8 => I8x16Add,
            SimdLaneShape.I16 => I16x8Add,
            SimdLaneShape.I32 => I32x4Add,
            SimdLaneShape.I64 => I64x2Add,
            SimdLaneShape.F32 => F32x4Add,
            _ => F64x2Add,
        },
        RuntimeIntrinsic.SimdSubtract => lane.Shape switch
        {
            SimdLaneShape.I8 => I8x16Subtract,
            SimdLaneShape.I16 => I16x8Subtract,
            SimdLaneShape.I32 => I32x4Subtract,
            SimdLaneShape.I64 => I64x2Subtract,
            SimdLaneShape.F32 => F32x4Subtract,
            _ => F64x2Subtract,
        },
        // Byte lanes multiply through widening, in Multiply.
        RuntimeIntrinsic.SimdMultiply => lane.Shape switch
        {
            SimdLaneShape.I16 => I16x8Multiply,
            SimdLaneShape.I32 => I32x4Multiply,
            SimdLaneShape.I64 => I64x2Multiply,
            SimdLaneShape.F32 => F32x4Multiply,
            _ => F64x2Multiply,
        },
        RuntimeIntrinsic.SimdMinimum => (lane.Shape, lane.Unsigned) switch
        {
            (SimdLaneShape.I8, false) => I8x16MinimumSigned,
            (SimdLaneShape.I8, true) => I8x16MinimumUnsigned,
            (SimdLaneShape.I16, false) => I16x8MinimumSigned,
            (SimdLaneShape.I16, true) => I16x8MinimumUnsigned,
            (SimdLaneShape.I32, false) => I32x4MinimumSigned,
            (SimdLaneShape.I32, true) => I32x4MinimumUnsigned,
            // IEEE 754:2019 minimum, as upstream's float.Min and double.Min.
            // 64-bit integer lanes select by comparison, in EmitVector.
            (SimdLaneShape.F32, _) => F32x4Minimum,
            _ => F64x2Minimum,
        },
        RuntimeIntrinsic.SimdMaximum => (lane.Shape, lane.Unsigned) switch
        {
            (SimdLaneShape.I8, false) => I8x16MaximumSigned,
            (SimdLaneShape.I8, true) => I8x16MaximumUnsigned,
            (SimdLaneShape.I16, false) => I16x8MaximumSigned,
            (SimdLaneShape.I16, true) => I16x8MaximumUnsigned,
            (SimdLaneShape.I32, false) => I32x4MaximumSigned,
            (SimdLaneShape.I32, true) => I32x4MaximumUnsigned,
            (SimdLaneShape.F32, _) => F32x4Maximum,
            _ => F64x2Maximum,
        },
        _ => throw Unsupported(intrinsic, lane),
    };

    internal static uint Shift(RuntimeIntrinsic intrinsic, SimdLane lane)
    {
        var arithmetic = intrinsic == RuntimeIntrinsic.SimdShiftRightArithmetic && !lane.Unsigned;
        return (intrinsic, lane.Bits) switch
        {
            (RuntimeIntrinsic.SimdShiftLeft, SimdLaneShape.I8) => I8x16ShiftLeft,
            (RuntimeIntrinsic.SimdShiftLeft, SimdLaneShape.I16) => I16x8ShiftLeft,
            (RuntimeIntrinsic.SimdShiftLeft, SimdLaneShape.I32) => I32x4ShiftLeft,
            (RuntimeIntrinsic.SimdShiftLeft, _) => I64x2ShiftLeft,
            (_, SimdLaneShape.I8) => arithmetic ? I8x16ShiftRightSigned : I8x16ShiftRightUnsigned,
            (_, SimdLaneShape.I16) => arithmetic ? I16x8ShiftRightSigned : I16x8ShiftRightUnsigned,
            (_, SimdLaneShape.I32) => arithmetic ? I32x4ShiftRightSigned : I32x4ShiftRightUnsigned,
            _ => arithmetic ? I64x2ShiftRightSigned : I64x2ShiftRightUnsigned,
        };
    }

    internal static uint Splat(SimdLane lane) => lane.Shape switch
    {
        SimdLaneShape.I8 => I8x16Splat,
        SimdLaneShape.I16 => I16x8Splat,
        SimdLaneShape.I32 => I32x4Splat,
        SimdLaneShape.I64 => I64x2Splat,
        SimdLaneShape.F32 => F32x4Splat,
        _ => F64x2Splat,
    };

    internal static uint ReplaceLane(SimdLane lane) => lane.Shape switch
    {
        SimdLaneShape.I8 => I8x16ReplaceLane,
        SimdLaneShape.I16 => I16x8ReplaceLane,
        SimdLaneShape.I32 => I32x4ReplaceLane,
        SimdLaneShape.I64 => I64x2ReplaceLane,
        SimdLaneShape.F32 => F32x4ReplaceLane,
        _ => F64x2ReplaceLane,
    };

    internal static uint ExtractLane(SimdLane lane) => (lane.Shape, lane.Unsigned) switch
    {
        (SimdLaneShape.I8, false) => I8x16ExtractLaneSigned,
        (SimdLaneShape.I8, true) => I8x16ExtractLaneUnsigned,
        (SimdLaneShape.I16, false) => I16x8ExtractLaneSigned,
        (SimdLaneShape.I16, true) => I16x8ExtractLaneUnsigned,
        (SimdLaneShape.I32, _) => I32x4ExtractLane,
        (SimdLaneShape.I64, _) => I64x2ExtractLane,
        (SimdLaneShape.F32, _) => F32x4ExtractLane,
        _ => F64x2ExtractLane,
    };

    internal static int LaneSize(SimdLane lane) => lane.Shape switch
    {
        SimdLaneShape.I8 => 1,
        SimdLaneShape.I16 => 2,
        SimdLaneShape.I32 or SimdLaneShape.F32 => 4,
        _ => 8,
    };

    internal static uint AllTrue(SimdLaneShape bits) => bits switch
    {
        SimdLaneShape.I8 => I8x16AllTrue,
        SimdLaneShape.I16 => I16x8AllTrue,
        SimdLaneShape.I32 => I32x4AllTrue,
        _ => I64x2AllTrue,
    };

    internal static uint BitMask(SimdLaneShape bits) => bits switch
    {
        SimdLaneShape.I8 => I8x16BitMask,
        SimdLaneShape.I16 => I16x8BitMask,
        SimdLaneShape.I32 => I32x4BitMask,
        _ => I64x2BitMask,
    };

    internal static InvalidOperationException Unsupported(RuntimeIntrinsic intrinsic, SimdLane lane) =>
        new($"{intrinsic} has no lowering for {lane.Shape} lanes.");
}
